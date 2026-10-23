using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ships;
using Ships.Cables.Model;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Services;

namespace StellarPerformance;

// Server-only pointer projection. Every reuse validates the provider and touched
// part dictionaries through identity and copied mutation-version enumerators.
// Values, state types and emission arrays remain live; no numeric result is cached.
internal static class SignalEndpointPlan
{
    static SignalEndpointPlan() { }
    private const int EntryLimit = 1024, ByteLimit = 8 * 1024 * 1024, ConnectionLimit = 8192;
    private static Func<ShipsServerTracker, Dictionary<uint, TrackedShipServer>> _ships = null!;
    private static Func<ShipState, Dictionary<ushort, StatefulPart>> _parts = null!;
    private static ConditionalWeakTable<ConnectionGroup, Plan> _cache = new ConditionalWeakTable<ConnectionGroup, Plan>();
    private static int _entries, _bytes;
    internal static bool Available { get; private set; }
    internal static bool Initialized { get; private set; }
    internal static int RetainedEntries => _entries;

    private readonly struct Endpoint
    {
        internal readonly StatefulPart Part;
        internal readonly byte Socket;
        internal Endpoint(StatefulPart part, byte socket) { Part = part; Socket = socket; }
    }
    private sealed class ShipStamp
    {
        internal TrackedShipServer Ship = null!;
        internal ShipState State = null!;
        internal Dictionary<ushort, StatefulPart> Parts = null!;
        internal Dictionary<ushort, StatefulPart>.Enumerator Version;
        internal bool Current()
        {
            if (!ReferenceEquals(Ship.State, State) || !ReferenceEquals(_parts(State), Parts) || !ReferenceEquals(Parts.Comparer, EqualityComparer<ushort>.Default)) return false;
            var version = Version;
            try { version.MoveNext(); return true; } catch (InvalidOperationException) { return false; }
        }
    }
    private sealed class Plan
    {
        internal ShipsServerTracker Provider = null!;
        internal Dictionary<uint, TrackedShipServer> Dictionary = null!;
        internal Dictionary<uint, TrackedShipServer>.Enumerator Version;
        internal IReadOnlyCollection<SocketConnection> Source = null!;
        internal Endpoint[] Endpoints = Array.Empty<Endpoint>();
        internal ShipStamp[] Ships = Array.Empty<ShipStamp>();
        internal int Bytes;
        internal bool Current(ShipsServerTracker provider, IReadOnlyCollection<SocketConnection> source, Dictionary<uint, TrackedShipServer> dictionary)
        {
            if (!ReferenceEquals(Provider, provider) || !ReferenceEquals(Source, source) || !ReferenceEquals(dictionary, Dictionary)) return false;
            var version = Version;
            try { version.MoveNext(); } catch (InvalidOperationException) { return false; }
            foreach (var ship in Ships) if (!ship.Current()) return false;
            return true;
        }
    }

    internal static void Initialize(HarmonyLib.Harmony harmony)
    {
        foreach (var type in new[] { typeof(Dictionary<uint, TrackedShipServer>), typeof(Dictionary<ushort, StatefulPart>) })
        {
            PatchSafety.RequireNoForeignOwners(AccessTools.Method(type, "GetEnumerator"), harmony.Id);
            PatchSafety.RequireNoForeignOwners(AccessTools.PropertyGetter(type, "Count"), harmony.Id);
            PatchSafety.RequireNoForeignOwners(AccessTools.PropertyGetter(type, "Comparer"), harmony.Id);
            PatchSafety.RequireNoForeignOwners(AccessTools.Method(type, "TryGetValue"), harmony.Id);
            PatchSafety.RequireNoForeignOwners(AccessTools.PropertySetter(type, "Item"), harmony.Id);
            PatchSafety.RequireNoForeignOwners(AccessTools.Method(type, "Add"), harmony.Id);
            PatchSafety.RequireNoForeignOwners(AccessTools.Method(type, "Remove", new[] { type.GetGenericArguments()[0] }), harmony.Id);
            PatchSafety.RequireNoForeignOwners(AccessTools.Method(type, "Clear"), harmony.Id);
        }
        foreach (var type in new[] { typeof(Dictionary<uint, TrackedShipServer>.Enumerator), typeof(Dictionary<ushort, StatefulPart>.Enumerator) })
            PatchSafety.RequireNoForeignOwners(AccessTools.Method(type, "MoveNext"), harmony.Id);
        // Called only after SignalReduction's native-provider/state guards.
        _ships = NativeAccess.Read<ShipsServerTracker, Dictionary<uint, TrackedShipServer>>("_shipsDict");
        _parts = NativeAccess.Read<ShipState, Dictionary<ushort, StatefulPart>>("_statefulParts");
        Initialized = true;
        Available = SupportsMutationVersions<uint, TrackedShipServer>(1) && SupportsMutationVersions<ushort, StatefulPart>(1);
        Clear();
    }

    internal static bool SupportsMutationVersions<TKey, TValue>(TKey key) where TKey : notnull
    {
        var probe = new Dictionary<TKey, TValue> { [key] = default! };
        var version = probe.GetEnumerator(); probe[key] = default!;
        if (!Invalid(version)) return false;
        version = probe.GetEnumerator(); probe.Remove(key);
        if (!Invalid(version)) return false;
        probe.Add(key, default!); version = probe.GetEnumerator(); probe.Clear();
        return Invalid(version);
    }
    private static bool Invalid<TKey, TValue>(Dictionary<TKey, TValue>.Enumerator version) where TKey : notnull
    {
        try { version.MoveNext(); return false; } catch (InvalidOperationException) { return true; }
    }

    internal static bool TryReduce(ConnectionGroup group, ShipsServerTracker provider, IReadOnlyCollection<SocketConnection> source, out double result)
    {
        result = 0.0;
        if (!Initialized) return SignalReduction.TryReduceStreaming(provider, source, out result);
        var dictionary = _ships(provider);
        if (!ReferenceEquals(dictionary.Comparer, EqualityComparer<uint>.Default)) return false;
        if (!Available || !SignalConnectionScan.IsRetained(group, source)) return Stream(dictionary, source, out result);
        var present = _cache.TryGetValue(group, out var plan);
        if (!present || !plan!.Current(provider, source, dictionary))
        {
            if (present) { _cache.Remove(group); _entries--; _bytes -= plan!.Bytes; }
            if (_entries >= EntryLimit || source.Count > ConnectionLimit || 256L + source.Count * 64L > ByteLimit - _bytes)
                return Stream(dictionary, source, out result);
            plan = Build(provider, source, dictionary);
            if (plan == null) return false;
            if (plan.Bytes > ByteLimit - _bytes) return Stream(dictionary, source, out result);
            _cache.Add(group, plan); _entries++; _bytes += plan.Bytes;
        }
        foreach (var endpoint in plan.Endpoints)
        {
            if (!SignalReduction.Accumulate(endpoint.Part.State, endpoint.Socket, ref result)) return false;
        }
        return true;
    }
    private static bool Stream(Dictionary<uint, TrackedShipServer> dictionary, IReadOnlyCollection<SocketConnection> source, out double result)
    {
        result = 0.0;
        bool Add(EntityPartSocket socket, ref double value)
        {
            if (!dictionary.TryGetValue(socket.EntityId, out var ship)) return true;
            if (ship.GetType() != typeof(TrackedShipServer)) return false;
            var parts = _parts(ship.State);
            // Do not invoke a custom comparer before deciding to keep the full
            // native method; even its hash/equality callbacks can mutate state.
            if (!ReferenceEquals(parts.Comparer, EqualityComparer<ushort>.Default)) return false;
            return !parts.TryGetValue(socket.PartContext.PartId, out var part) || SignalReduction.Accumulate(part.State, socket.Socket.SocketId, ref value);
        }
        foreach (var connection in source)
        {
            if (connection.SocketA.HasValue && !Add(connection.SocketA.Value, ref result)) return false;
            if (connection.SocketB.HasValue && !Add(connection.SocketB.Value, ref result)) return false;
        }
        return true;
    }
    private static Plan? Build(ShipsServerTracker provider, IReadOnlyCollection<SocketConnection> source, Dictionary<uint, TrackedShipServer> dictionary)
    {
        var plan = new Plan { Provider = provider, Dictionary = dictionary, Version = dictionary.GetEnumerator(), Source = source };
        var endpoints = new List<Endpoint>(source.Count * 2); var stamps = new Dictionary<uint, ShipStamp>();
        var supported = true;
        void Add(EntityPartSocket socket)
        {
            if (!dictionary.TryGetValue(socket.EntityId, out var ship)) return;
            if (ship.GetType() != typeof(TrackedShipServer)) { supported = false; return; }
            if (!stamps.ContainsKey(socket.EntityId))
            {
                var state = ship.State; var parts = _parts(state);
                if (!ReferenceEquals(parts.Comparer, EqualityComparer<ushort>.Default)) { supported = false; return; }
                stamps.Add(socket.EntityId, new ShipStamp { Ship = ship, State = state, Parts = parts, Version = parts.GetEnumerator() });
            }
            if (ship.TryGetStatefulPart(socket.PartContext.PartId, out var part)) endpoints.Add(new Endpoint(part, socket.Socket.SocketId));
        }
        foreach (var connection in source)
        {
            if (connection.SocketA.HasValue) Add(connection.SocketA.Value);
            if (!supported) return null;
            if (connection.SocketB.HasValue) Add(connection.SocketB.Value);
            if (!supported) return null;
        }
        plan.Endpoints = endpoints.ToArray(); plan.Ships = new ShipStamp[stamps.Count]; stamps.Values.CopyTo(plan.Ships, 0);
        plan.Bytes = 256 + plan.Endpoints.Length * 24 + plan.Ships.Length * 160;
        return plan;
    }
    internal static void Clear() { _cache = new ConditionalWeakTable<ConnectionGroup, Plan>(); _entries = _bytes = 0; }
}
