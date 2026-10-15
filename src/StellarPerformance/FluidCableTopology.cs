using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Items.Model;
using Ships;
using Ships.Cables;
using Ships.Cables.Fluids;
using Ships.Cables.VisualObjects;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Model.State;
using Ships.Services;
using UnityEngine;

namespace StellarPerformance;

internal static class FluidCableTopology
{
    static FluidCableTopology() { }

    internal readonly struct ConnectedPartEntry
    {
        internal readonly PartContext PartContext;
        internal readonly bool HasSocket2;
        internal readonly bool HasSocket3;

        internal ConnectedPartEntry(PartContext partContext, List<byte> sockets)
        {
            PartContext = partContext;
            HasSocket2 = sockets.Contains(2);
            HasSocket3 = sockets.Contains(3);
        }
    }

    internal sealed class CachedNode
    {
        internal readonly Dictionary<PartContext, List<byte>> BuiltParts = new Dictionary<PartContext, List<byte>>();
        internal ConnectedPartEntry[] Parts = Array.Empty<ConnectedPartEntry>();
        internal readonly List<CachedCable> ConnectedCables = new List<CachedCable>();
        internal float FluidFlow;
        internal uint FluidItemId;
    }

    internal sealed class CachedCable
    {
        internal readonly uint Id;
        internal readonly CableState State;
        internal readonly FluidCableVisual Visual;
        internal CachedNode? NodeA;
        internal CachedNode? NodeB;
        internal float Flow;

        internal CachedCable(uint id, CableState state, FluidCableVisual visual)
        {
            Id = id;
            State = state;
            Visual = visual;
        }
    }

    internal readonly struct VisualSlot
    {
        internal readonly uint Id;
        internal readonly FluidCableVisual Visual;
        internal readonly bool HasState;
        internal readonly CableState State;

        internal VisualSlot(uint id, FluidCableVisual visual, bool hasState, CableState state)
        {
            Id = id;
            Visual = visual;
            HasState = hasState;
            State = state;
        }
    }

    internal sealed class UpdaterCache
    {
        internal int TopologyVersion = -1;
        internal int LoadedCount = -1;
        internal VisualSlot[] Visuals = Array.Empty<VisualSlot>();
        internal CachedCable[] Cables = Array.Empty<CachedCable>();
        internal CachedNode[] Nodes = Array.Empty<CachedNode>();
        internal readonly Dictionary<SocketConnection, CachedCable> VisibleByConnection = new Dictionary<SocketConnection, CachedCable>();
        internal readonly Dictionary<EntityPartSocket, CachedNode> SocketsToNode = new Dictionary<EntityPartSocket, CachedNode>();
        internal readonly Queue<SocketConnection> ToTraverse = new Queue<SocketConnection>();
        internal readonly HashSet<SocketConnection> Traversed = new HashSet<SocketConnection>();
        internal readonly HashSet<uint> VisitedCableIds = new HashSet<uint>();
        internal readonly CableVisualCadence.Box NextValues = new CableVisualCadence.Box();
    }

    private static volatile int _topologyVersion = 1;
    private static readonly ConditionalWeakTable<FluidCablesVisualUpdater, UpdaterCache> Cache = new ConditionalWeakTable<FluidCablesVisualUpdater, UpdaterCache>();

    private static readonly Func<FluidCablesVisualUpdater, Dictionary<uint, CableFluidState>> CableFluidStatesOf = NativeAccess.Read<FluidCablesVisualUpdater, Dictionary<uint, CableFluidState>>("_cableFluidStates");
    private static readonly Func<FluidCablesVisualUpdater, CablesClientTracker> CablesOf = NativeAccess.Read<FluidCablesVisualUpdater, CablesClientTracker>("_cables");
    private static readonly Func<FluidCablesVisualUpdater, ShipsClientTracker> ShipsOf = NativeAccess.Read<FluidCablesVisualUpdater, ShipsClientTracker>("_ships");
    private static readonly Func<FluidCablesVisualUpdater, ConnectionsClientTracker> ConnectionsOf = NativeAccess.Read<FluidCablesVisualUpdater, ConnectionsClientTracker>("_connections");
    private static readonly Func<FluidCablesVisualUpdater, ItemSettingsList> ItemsOf = NativeAccess.Read<FluidCablesVisualUpdater, ItemSettingsList>("_items");
    private static readonly Func<FluidCablesVisualUpdater, Material> LiquidMatOf = NativeAccess.Read<FluidCablesVisualUpdater, Material>("_fluidCableMatLiquid");
    private static readonly Func<FluidCablesVisualUpdater, Material> GasMatOf = NativeAccess.Read<FluidCablesVisualUpdater, Material>("_fluidCableMatGas");

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        PatchSafety.RequireOriginalBody(AccessTools.Method(typeof(FluidCablesVisualUpdater), nameof(FluidCablesVisualUpdater.UpdateFluidCables)), harmony.Id);
        foreach (var name in new[] { "RecurseBuildNode", "RecurseBalanceIncomingFlow" })
            PatchSafety.RequireNoForeignOwners(AccessTools.Method(typeof(FluidCablesVisualUpdater), name), harmony.Id);
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(typeof(ConnectionsTracker), nameof(ConnectionsTracker.GetRelayedSocketConnections)),
            harmony.Id, "sdmodkit.itemcables", "stellarperformance.fluid-sockets");
        if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "SDModKit") &&
            !Compatibility.Validate(new[] { "SDModKit" }, out var reason))
            throw new InvalidOperationException(reason);
        foreach (var name in new[] { nameof(ConnectionsTracker.CreateConnection), nameof(ConnectionsTracker.UpdateConnection), nameof(ConnectionsTracker.DeleteConnection), "OnShipStatefulPartAdded", "OnShipStatefulPartRemoved" })
        {
            var method = AccessTools.Method(typeof(ConnectionsTracker), name) ?? throw new MissingMethodException("ConnectionsTracker." + name);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(FluidCableTopology), nameof(BumpTopologyVersion)));
        }
        foreach (var name in new[] { "AddStatefulPart", "AddStatefulPartWithoutInit", "RemoveStatefulPart" })
        {
            var method = AccessTools.Method(typeof(ShipState), name) ?? throw new MissingMethodException("ShipState." + name);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(FluidCableTopology), nameof(BumpTopologyVersion)));
        }
        foreach (var name in new[] { nameof(CablesVisuals.OnCableCreate), nameof(CablesVisuals.OnCableUpdate), nameof(CablesVisuals.OnCableDelete) })
        {
            var method = AccessTools.Method(typeof(CablesVisuals), name) ?? throw new MissingMethodException("CablesVisuals." + name);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(FluidCableTopology), nameof(BumpTopologyVersion)));
        }
        harmony.Patch(AccessTools.Method(typeof(FluidCablesVisualUpdater), nameof(FluidCablesVisualUpdater.UpdateFluidCables)) ?? throw new MissingMethodException("UpdateFluidCables"),
            prefix: new HarmonyMethod(typeof(FluidCableTopology), nameof(BeforeUpdateFluidCables)));
        CableVisualUpdates.Install(harmony);
    }

    internal static void BumpTopologyVersion() => Interlocked.Increment(ref _topologyVersion);

    internal static bool BeforeUpdateFluidCables(FluidCablesVisualUpdater __instance, IReadOnlyDictionary<uint, CableVisual> loadedCables)
    {
        var cache = Cache.GetValue(__instance, _ => new UpdaterCache());
        var cablesTracker = CablesOf(__instance);
        var ships = ShipsOf(__instance);
        var connections = ConnectionsOf(__instance);
        var version = _topologyVersion;
        var current = IsTopologyCurrent(cache, loadedCables, cablesTracker, version);
        if (!current)
            RebuildTopology(cache, loadedCables, cablesTracker, connections, version);

        var nodes = cache.Nodes;
        var cables = cache.Cables;
        if (current && !CableVisualCadence.Due(cache.NextValues, Time.unscaledTime))
        {
            if (DiagnosticCounters.Active) CableVisualCadence.Reused++;
            for (var i = 0; i < cables.Length; i++)
                cables[i].Visual.UpdateFlow(cables[i].Flow);
            return false;
        }
        if (DiagnosticCounters.Active) CableVisualCadence.Computed++;
        for (var i = 0; i < nodes.Length; i++)
        {
            nodes[i].FluidFlow = 0f;
            nodes[i].FluidItemId = 0u;
        }
        for (var i = 0; i < cables.Length; i++)
            cables[i].Flow = 0f;

        AccumulatePartFlows(nodes, partContext => ships.TryGetStatefulPart(partContext, out var part) ? part : null);
        BalanceFlows(nodes, cache.VisitedCableIds);

        var fluidStates = CableFluidStatesOf(__instance);
        var items = ItemsOf(__instance);
        var liquidMat = LiquidMatOf(__instance);
        var gasMat = GasMatOf(__instance);
        var visuals = cache.Visuals;
        for (var i = 0; i < visuals.Length; i++)
        {
            var slot = visuals[i];
            var density = 0f;
            var color = Color.black;
            var mat = liquidMat;
            if (fluidStates.TryGetValue(slot.Id, out var fluidState))
            {
                density = fluidState.FluidQuantity / fluidState.MaxFluidQuantity;
                var itemSettings = items.GetItemSettingsById(fluidState.FluidItemId);
                color = itemSettings.fluidColor;
                if (itemSettings.isGas) mat = gasMat;
            }
            var previousDefer = CableVisualUpdates.DeferDensityToFlow;
            CableVisualUpdates.DeferDensityToFlow = slot.HasState;
            try
            {
                slot.Visual.UpdateContent(mat, density, color);
            }
            finally
            {
                CableVisualUpdates.DeferDensityToFlow = previousDefer;
            }
        }
        for (var i = 0; i < cables.Length; i++)
            cables[i].Visual.UpdateFlow(cables[i].Flow);

        return false;
    }

    internal static bool IsTopologyCurrent(UpdaterCache cache, IReadOnlyDictionary<uint, CableVisual> loadedCables, CablesClientTracker cablesTracker, int version)
    {
        if (cache.TopologyVersion != version || cache.LoadedCount != loadedCables.Count) return false;
        var visuals = cache.Visuals;
        for (var i = 0; i < visuals.Length; i++)
        {
            var slot = visuals[i];
            if (!loadedCables.TryGetValue(slot.Id, out var currentVisual) || !ReferenceEquals(currentVisual, slot.Visual)) return false;
            var hasState = cablesTracker.TryGetCableState(slot.Id, out var state);
            if (hasState != slot.HasState || (hasState && !SameState(state, slot.State))) return false;
        }
        return true;
    }

    // Field-wise comparison: ValueType.Equals boxes every nested struct field on Mono.
    internal static bool SameState(in CableState a, in CableState b) =>
        a.Type == b.Type && SamePlug(a.PlugA, b.PlugA) && SamePlug(a.PlugB, b.PlugB);

    private static bool SamePlug(in CablePlugSocket a, in CablePlugSocket b) =>
        a.IsHeldByPlayer == b.IsHeldByPlayer && a.EntityPartSocket.Equals(b.EntityPartSocket);

    internal static void RebuildTopology(UpdaterCache cache, IReadOnlyDictionary<uint, CableVisual> loadedCables, CablesClientTracker cablesTracker, ConnectionsClientTracker connections, int version)
    {
        cache.VisibleByConnection.Clear();
        cache.SocketsToNode.Clear();
        var visualsList = new List<VisualSlot>();
        var cablesList = new List<CachedCable>();
        foreach (var pair in loadedCables)
        {
            if (!(pair.Value is FluidCableVisual fluidVisual)) continue;
            var hasState = cablesTracker.TryGetCableState(pair.Key, out var state);
            visualsList.Add(new VisualSlot(pair.Key, fluidVisual, hasState, state));
            if (!hasState) continue;
            var cableData = new CableData { Id = pair.Key, State = state };
            var cachedCable = new CachedCable(pair.Key, state, fluidVisual);
            cache.VisibleByConnection[cableData.ToSocketConnection()] = cachedCable;
            cablesList.Add(cachedCable);
        }

        var nodesList = new List<CachedNode>();
        foreach (var pair in cache.VisibleByConnection)
        {
            var socketConnection = pair.Key;
            var visibleCable = pair.Value;
            if (socketConnection.SocketA.HasValue)
                visibleCable.NodeA = GetOrCreateNode(cache, nodesList, visibleCable, socketConnection.SocketA.Value, connections.GetRelayedSocketConnections);
            if (socketConnection.SocketB.HasValue)
                visibleCable.NodeB = GetOrCreateNode(cache, nodesList, visibleCable, socketConnection.SocketB.Value, connections.GetRelayedSocketConnections);
        }

        for (var i = 0; i < nodesList.Count; i++)
        {
            var node = nodesList[i];
            var parts = new ConnectedPartEntry[node.BuiltParts.Count];
            var index = 0;
            foreach (var partPair in node.BuiltParts)
                parts[index++] = new ConnectedPartEntry(partPair.Key, partPair.Value);
            node.Parts = parts;
            node.BuiltParts.Clear();
        }

        cache.Visuals = visualsList.ToArray();
        cache.Cables = cablesList.ToArray();
        cache.Nodes = nodesList.ToArray();
        cache.LoadedCount = loadedCables.Count;
        cache.TopologyVersion = version;
    }

    internal static CachedNode GetOrCreateNode(UpdaterCache cache, List<CachedNode> nodes, CachedCable visibleCable, EntityPartSocket socket, Func<EntityPartSocket, IReadOnlyCollection<SocketConnection>> getRelayed)
    {
        if (!cache.SocketsToNode.TryGetValue(socket, out var node))
        {
            node = RecurseBuildNode(cache, socket, getRelayed);
            nodes.Add(node);
        }
        node.ConnectedCables.Add(visibleCable);
        return node;
    }

    internal static CachedNode RecurseBuildNode(UpdaterCache cache, EntityPartSocket socket, Func<EntityPartSocket, IReadOnlyCollection<SocketConnection>> getRelayed)
    {
        var node = new CachedNode();
        cache.ToTraverse.Clear();
        cache.Traversed.Clear();
        QueueRelayedConnections(cache, node, socket, getRelayed);
        while (cache.ToTraverse.Count > 0)
        {
            var result = cache.ToTraverse.Dequeue();
            if (cache.Traversed.Add(result) && result.Type != SocketConnectionType.Cable)
            {
                if (result.SocketA.HasValue) QueueRelayedConnections(cache, node, result.SocketA.Value, getRelayed);
                if (result.SocketB.HasValue) QueueRelayedConnections(cache, node, result.SocketB.Value, getRelayed);
            }
        }
        return node;
    }

    private static void QueueRelayedConnections(UpdaterCache cache, CachedNode node, EntityPartSocket socket, Func<EntityPartSocket, IReadOnlyCollection<SocketConnection>> getRelayed)
    {
        var partContext = socket.PartContext;
        if (!node.BuiltParts.TryGetValue(partContext, out var sockets))
        {
            sockets = new List<byte>(4);
            node.BuiltParts[partContext] = sockets;
        }
        sockets.Add(socket.Socket.SocketId);
        foreach (var relayed in getRelayed(socket))
        {
            if (relayed.Type == SocketConnectionType.Cable)
            {
                if (relayed.SocketA.HasValue && relayed.SocketA.Value.PartContext == partContext && !cache.SocketsToNode.ContainsKey(relayed.SocketA.Value))
                    cache.SocketsToNode.Add(relayed.SocketA.Value, node);
                if (relayed.SocketB.HasValue && relayed.SocketB.Value.PartContext == partContext && !cache.SocketsToNode.ContainsKey(relayed.SocketB.Value))
                    cache.SocketsToNode.Add(relayed.SocketB.Value, node);
            }
            else
            {
                cache.ToTraverse.Enqueue(relayed);
            }
        }
    }

    internal static void AccumulatePartFlows(CachedNode[] nodes, Func<PartContext, StatefulPart?> tryGetPart)
    {
        for (var i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i];
            var parts = node.Parts;
            for (var j = 0; j < parts.Length; j++)
            {
                var entry = parts[j];
                var part = tryGetPart(entry.PartContext);
                if (part == null) continue;
                var state = part.State;
                if (state is IStatefulPartWithFluidContainer container)
                {
                    node.FluidFlow += container.FluidFlow / 50f;
                    if (node.FluidItemId == 0) node.FluidItemId = container.FluidItemId;
                }
                if (state is ThrusterState thruster)
                {
                    node.FluidFlow += thruster.ThrusterStrength * 0.05f / 50f;
                }
                if (state is FluidPumpState pump)
                {
                    var pumpDelta = pump.PumpRate * 5f / 50f;
                    if (entry.HasSocket2) node.FluidFlow += pumpDelta;
                    if (entry.HasSocket3) node.FluidFlow -= pumpDelta;
                }
                if (state is FluidValveState valve)
                {
                    var valveDelta = valve.CurrentTransferRate / 50f;
                    if (entry.HasSocket2) node.FluidFlow += valveDelta;
                    if (entry.HasSocket3) node.FluidFlow -= valveDelta;
                }
                if (state is GasExhaustState exhaust)
                {
                    node.FluidFlow += exhaust.OutputRate / 50f;
                }
                if (state is ElectrolyzerState electrolyzer)
                {
                    var electrolyzerRate = electrolyzer.NormalizedProductionRate * 1f;
                    node.FluidFlow -= electrolyzerRate / 50f;
                }
                if (state is GasIntakeState intake)
                {
                    node.FluidFlow -= intake.IntakeRate / 50f;
                }
                if (state is EthanolDistillerState distiller)
                {
                    node.FluidFlow -= distiller.NormalizedProductionRate * 0.16f / 50f;
                }
            }
        }
    }

    internal static void BalanceFlows(CachedNode[] nodes, HashSet<uint> visitedIds)
    {
        for (var i = 0; i < nodes.Length; i++)
        {
            var fluidNode = nodes[i];
            if (!(fluidNode.FluidFlow < 0f)) continue;
            visitedIds.Clear();
            var fluidToBalance = 0f - fluidNode.FluidFlow;
            RecurseBalanceIncomingFlow(fluidNode, ref fluidToBalance, visitedIds);
            fluidNode.FluidFlow = 0f - fluidToBalance;
        }
    }

    private static void RecurseBalanceIncomingFlow(CachedNode node, ref float fluidToBalance, HashSet<uint> visitedIds)
    {
        if (node.FluidFlow > 0f)
        {
            var absorbed = Math.Max(Math.Min(fluidToBalance, node.FluidFlow), 0f);
            node.FluidFlow -= absorbed;
            fluidToBalance -= absorbed;
            if (fluidToBalance <= 0f) return;
        }
        var cables = node.ConnectedCables;
        for (var i = 0; i < cables.Count; i++)
        {
            var connectedCable = cables[i];
            if (!visitedIds.Add(connectedCable.Id)) continue;
            if (fluidToBalance <= 0f) break;
            var sign = 1f;
            CachedNode? otherNode;
            if (ReferenceEquals(connectedCable.NodeA, node))
            {
                otherNode = connectedCable.NodeB;
            }
            else
            {
                otherNode = connectedCable.NodeA;
                sign = -1f;
            }
            if (otherNode != null)
            {
                var before = fluidToBalance;
                RecurseBalanceIncomingFlow(otherNode, ref fluidToBalance, visitedIds);
                var transferred = before - fluidToBalance;
                connectedCable.Flow += sign * transferred;
            }
        }
    }

    internal static int CurrentTopologyVersion => _topologyVersion;
}
