using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ships.Cables;
using Ships.Cables.Model;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Services;
using Ships.Interface.Settings;

namespace StellarPerformance;

internal static class SignalInputProjection
{
    static SignalInputProjection() { }

    private static readonly Type[] PureProcessors = { typeof(DeltaState), typeof(MathGateState), typeof(SignalProcessorState) };
    private static readonly Func<SignalConnectionGroupValues, ConnectionGroupMap> Groups = Bind<ConnectionGroupMap>("_groupMap");
    private static readonly Func<SignalConnectionGroupValues, IShipsProvider> Ships = Bind<IShipsProvider>("_ships");
    private static readonly MethodInfo NativeRead = AccessTools.Method(typeof(SignalConnectionGroupValues), nameof(SignalConnectionGroupValues.GetGroupInputsAndOutputs));
    private static readonly MethodInfo ProjectedRead = AccessTools.Method(typeof(SignalInputProjection), nameof(ReadInputs));
    private static readonly MethodInfo NativeGetSockets = AccessTools.Method(typeof(PartSettings), nameof(PartSettings.GetSockets));
    private static readonly MethodInfo CachedGetSignalSockets = AccessTools.Method(typeof(SignalInputProjection), nameof(GetSignalSockets));
    private static readonly ReadOnlyCollection<SocketObject> EmptySockets = Array.AsReadOnly(Array.Empty<SocketObject>());
    private static readonly ConditionalWeakTable<PartSettings, IReadOnlyCollection<SocketObject>> SignalSocketsCache = new ConditionalWeakTable<PartSettings, IReadOnlyCollection<SocketObject>>();
    private static readonly MethodInfo[] Guarded = new[] { NativeRead, AccessTools.PropertyGetter(typeof(ConnectionGroup), nameof(ConnectionGroup.AllConnections)), AccessTools.Method(typeof(SignalConnectionGroupValues), "TryAddSocket"), AccessTools.Method(typeof(ISignalProcessorState), nameof(ISignalProcessorState.HasListeningSocketId)), AccessTools.Method(typeof(SocketConnection), nameof(SocketConnection.GetHashCode)), AccessTools.Method(typeof(SocketConnection), nameof(SocketConnection.Equals), new[] { typeof(SocketConnection) }) }.Concat(PureProcessors.Select(type => AccessTools.PropertyGetter(type, nameof(ISignalProcessorState.ListeningSockets)))).Concat(new[] { typeof(EntityPartSocket), typeof(PartSocket) }.SelectMany(type => new[] { AccessTools.Method(type, nameof(GetHashCode)), AccessTools.Method(type, nameof(Equals), new[] { type }) })).ToArray();
    [ThreadStatic] private static bool _active;

    private static Func<SignalConnectionGroupValues, T> Bind<T>(string name)
    {
        var instance = Expression.Parameter(typeof(SignalConnectionGroupValues), "values");
        var field = AccessTools.Field(typeof(SignalConnectionGroupValues), name) ?? throw new MissingFieldException(name);
        if (field.FieldType != typeof(T)) throw new InvalidOperationException("Signal provider field changed: " + name);
        return Expression.Lambda<Func<SignalConnectionGroupValues, T>>(Expression.Field(instance, field), instance).Compile();
    }

    private static void ValidateEnvironment()
    {
        var collections = typeof(HashSet<SocketConnection>).Assembly;
        if (collections.GetName().Name != "System.Core" || !Compatibility.Matches("System.Core", collections.ManifestModule.ModuleVersionId)) throw new InvalidOperationException("Signal membership requires the inspected Mono HashSet implementation.");
        SignalConnectionReads.Validate();
        SignalConnectionReads.Reset();
        foreach (var type in PureProcessors)
        {
            var getter = AccessTools.PropertyGetter(type, nameof(ISignalProcessorState.ListeningSockets));
            var body = PatchProcessor.GetOriginalInstructions(getter);
            if (body.Count != 2 || body[0].opcode != OpCodes.Ldsfld || !(body[0].operand is FieldInfo field) || field.DeclaringType != type || field.FieldType != typeof(byte[]) || body[1].opcode != OpCodes.Ret)
                throw new InvalidOperationException("Native signal listening-socket getter changed.");
        }
    }

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        ValidateEnvironment();
        harmony.Patch(AccessTools.Method(typeof(SignalConnectionGroupValues), nameof(SignalConnectionGroupValues.UpdateGroupSignalValue)), transpiler: new HarmonyMethod(typeof(SignalInputProjection), nameof(Transpile)));
        harmony.Patch(AccessTools.Method(typeof(SignalConnectionsServerUpdater), nameof(SignalConnectionsServerUpdater.UpdateSignalValues)), prefix: new HarmonyMethod(typeof(SignalInputProjection), nameof(Begin)) { priority = Priority.First }, finalizer: new HarmonyMethod(typeof(SignalInputProjection), nameof(End)) { priority = Priority.Last });
    }

    internal static void InstallClient(HarmonyLib.Harmony harmony)
    {
        ValidateEnvironment();
        harmony.Patch(AccessTools.Method(typeof(SignalConnectionGroupValues), nameof(SignalConnectionGroupValues.UpdateGroupSignalValue)), transpiler: new HarmonyMethod(typeof(SignalInputProjection), nameof(Transpile)));
        harmony.Patch(AccessTools.Method(typeof(SignalCablesVisualUpdater), nameof(SignalCablesVisualUpdater.UpdateSignalCables)),
            prefix: new HarmonyMethod(typeof(SignalInputProjection), nameof(Begin)) { priority = Priority.First },
            finalizer: new HarmonyMethod(typeof(SignalInputProjection), nameof(End)) { priority = Priority.Last },
            transpiler: new HarmonyMethod(typeof(SignalInputProjection), nameof(TranspileClientSignalCables)));
    }

    internal static IReadOnlyCollection<SocketObject> GetSignalSockets(PartSettings settings)
    {
        if (ReferenceEquals(settings, null)) return EmptySockets;
        return SignalSocketsCache.GetValue(settings, static partSettings =>
        {
            var all = partSettings.GetSockets();
            if (all == null || all.Count == 0) return EmptySockets;
            var filtered = new List<SocketObject>(all.Count);
            foreach (var socket in all)
                if (!ReferenceEquals(socket, null) && socket.Type == CableType.Signal)
                    filtered.Add(socket);
            return filtered.Count == 0 ? EmptySockets : Array.AsReadOnly(filtered.ToArray());
        });
    }

    internal static IEnumerable<CodeInstruction> TranspileClientSignalCables(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.Select(code => new CodeInstruction(code)).ToList();
        var matches = codes.Where(code => code.Calls(NativeGetSockets)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("SignalCablesVisualUpdater.UpdateSignalCables GetSockets call pattern changed.");
        matches[0].opcode = OpCodes.Call;
        matches[0].operand = CachedGetSignalSockets;
        return codes;
    }

    internal static bool Available() => Guarded.All(method => HarmonyLib.Harmony.GetPatchInfo(method)?.Owners.Any() != true);
    internal static bool Active => _active;
    internal static void Begin(out bool __state) { SignalConnectionReads.Prune(); Enter(Available(), out __state); }
    internal static void Enter(bool enabled, out bool previous) { previous = _active; _active = enabled; }
    internal static Exception? End(Exception? __exception, bool __state) { _active = __state; return __exception; }

    internal static void ReadInputs(SignalConnectionGroupValues values, uint groupId, out List<EntityPartSocket> inputs, out List<EntityPartSocket> outputs)
    {
        if (!_active) { values.GetGroupInputsAndOutputs(groupId, out inputs, out outputs); return; }
        inputs = new List<EntityPartSocket>();
        outputs = new List<EntityPartSocket>();
        if (!Groups(values).TryGetConnectionGroupWithId(groupId, out var group)) return;
        foreach (var connection in SignalConnectionReads.Read(group))
        {
            if (connection.SocketA.HasValue) AddInput(values, connection.SocketA.Value, inputs, outputs);
            if (connection.SocketB.HasValue) AddInput(values, connection.SocketB.Value, inputs, outputs);
        }
    }

    internal static void AddInput(SignalConnectionGroupValues values, EntityPartSocket socket, List<EntityPartSocket> inputs, List<EntityPartSocket> outputs)
    {
        if (!Ships(values).TryGetShip(socket.EntityId, out var ship) || !ship.TryGetStatefulPart(socket.PartContext.PartId, out var part)) return;
        var id = socket.Socket.SocketId;
        if (part.State is ISignalEmitterState emitter && emitter.HasEmissionSocketId(id)) inputs.Add(socket);
        if (part.State is ISignalProcessorState processor)
        {
            var type = processor.GetType();
            if (type != typeof(DeltaState) && type != typeof(MathGateState) && type != typeof(SignalProcessorState) && processor.HasListeningSocketId(id)) outputs.Add(socket);
            if (processor.HasEmissionSocketId(id)) inputs.Add(socket);
        }
    }

    private static bool RefersToLocal(CodeInstruction code, int index)
    {
        if (code.operand is LocalBuilder local) return local.LocalIndex == index;
        var opcode = code.opcode;
        if (opcode == OpCodes.Ldloc || opcode == OpCodes.Ldloc_S || opcode == OpCodes.Ldloca || opcode == OpCodes.Ldloca_S || opcode == OpCodes.Stloc || opcode == OpCodes.Stloc_S) return Convert.ToInt32(code.operand) == index;
        return index == 0 && (opcode == OpCodes.Ldloc_0 || opcode == OpCodes.Stloc_0) || index == 1 && (opcode == OpCodes.Ldloc_1 || opcode == OpCodes.Stloc_1) || index == 2 && (opcode == OpCodes.Ldloc_2 || opcode == OpCodes.Stloc_2) || index == 3 && (opcode == OpCodes.Ldloc_3 || opcode == OpCodes.Stloc_3);
    }

    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.Select(code => new CodeInstruction(code)).ToList();
        if (codes.Count(code => code.Calls(NativeRead)) == 0 && codes.Count(code => code.Calls(ProjectedRead)) == 1) return codes;
        var matches = codes.Select((code, index) => new { code, index }).Where(item => item.code.Calls(NativeRead)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Signal input projection call changed.");
        var index = matches[0].index;
        if (index == 0 || (codes[index - 1].opcode != OpCodes.Ldloca && codes[index - 1].opcode != OpCodes.Ldloca_S) || !(codes[index - 1].operand is LocalBuilder discarded)) throw new InvalidOperationException("Signal output is no longer a discarded local.");
        if (codes.Where((_, position) => position != index - 1).Any(code => RefersToLocal(code, discarded.LocalIndex))) throw new InvalidOperationException("Signal outputs are now consumed.");
        matches[0].code.opcode = OpCodes.Call;
        matches[0].code.operand = ProjectedRead;
        return codes;
    }
}

internal static class SignalConnectionReads
{
    private readonly struct Source
    {
        internal readonly EntityPartSocket Socket;
        internal readonly WeakReference<HashSet<SocketConnection>> Connections;
        internal readonly int Version, Count;
        internal Source(EntityPartSocket socket, HashSet<SocketConnection> connections) { Socket = socket; Connections = new WeakReference<HashSet<SocketConnection>>(connections); Version = VersionOf(connections); Count = connections.Count; }
    }

    private sealed class Entry
    {
        internal Source[] Sources = Array.Empty<Source>();
        internal SocketConnection[] Connections = Array.Empty<SocketConnection>();
        internal long Bytes => (long)Sources.Length * 64 + (long)Connections.Length * 64;
        internal bool Current(ConnectionGroup group)
        {
            if (Sources.Length != group.Sockets.Count) return false;
            var index = 0;
            foreach (var pair in group.Sockets)
            {
                var source = Sources[index++];
                if (!source.Socket.Equals(pair.Key) || !source.Connections.TryGetTarget(out var previous) || !ReferenceEquals(previous, pair.Value) || source.Count != pair.Value.Count || source.Version != VersionOf(pair.Value)) return false;
            }
            return true;
        }
    }

    private static readonly Func<HashSet<SocketConnection>, int> VersionOf = BindVersion();
    [ThreadStatic] private static ConditionalWeakTable<ConnectionGroup, Entry>? _cache;
    [ThreadStatic] private static List<WeakReference>? _retained;
    [ThreadStatic] private static long _bytes;
    [ThreadStatic] private static uint _ticks;
    internal const long MaximumBytes = 4 * 1024 * 1024;
    internal static long RetainedBytes => _bytes;
    internal static void Validate()
    {
        var set = new HashSet<SocketConnection>();
        var first = new SocketConnection(null, null, SocketConnectionType.Cable) { CableId = 1 };
        var second = first; second.CableId = 2;
        var version = VersionOf(set);
        set.Add(first);
        if (VersionOf(set) == version) throw new InvalidOperationException("HashSet mutation version is unavailable.");
        version = VersionOf(set);
        set.Remove(first); set.Add(second);
        if (VersionOf(set) == version) throw new InvalidOperationException("HashSet same-count mutation is not detectable.");
        version = VersionOf(set);
        set.Clear(); set.Add(first);
        if (VersionOf(set) == version) throw new InvalidOperationException("HashSet clear/refill mutation is not detectable.");
    }

    private static Func<HashSet<SocketConnection>, int> BindVersion()
    {
        var type = typeof(HashSet<SocketConnection>);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var field = type.GetField("_version", flags) ?? type.GetField("m_version", flags) ?? throw new MissingFieldException("HashSet mutation version");
        if (field.FieldType != typeof(int)) throw new InvalidOperationException("HashSet mutation version changed.");
        var set = Expression.Parameter(type, "set");
        return Expression.Lambda<Func<HashSet<SocketConnection>, int>>(Expression.Field(set, field), set).Compile();
    }

    internal static void Prune()
    {
        if (++_ticks % 128 != 0 || _retained == null) return;
        _retained.RemoveAll(reference => !reference.IsAlive);
        _bytes = _retained.Sum(reference => (reference.Target as Entry)?.Bytes ?? 0);
    }

    internal static IReadOnlyCollection<SocketConnection> Read(ConnectionGroup group, Func<ConnectionGroup, IReadOnlyCollection<SocketConnection>>? native = null)
    {
        if (_cache == null) { _cache = new ConditionalWeakTable<ConnectionGroup, Entry>(); _retained = new List<WeakReference>(); }
        var present = _cache.TryGetValue(group, out var entry);
        if (present && entry!.Current(group)) return entry.Connections;
        var connections = native == null ? group.AllConnections : native(group);
        var bytes = (long)group.Sockets.Count * 64 + (long)connections.Count * 64;
        if (group.Sockets.Count > 1024 || connections.Count > 2048 || _bytes - (entry?.Bytes ?? 0) + bytes > MaximumBytes || (!present && _retained!.Count >= 128)) return connections;
        if (!present) { entry = new Entry(); _cache.Add(group, entry); _retained!.Add(new WeakReference(entry)); }
        _bytes -= entry!.Bytes;
        entry.Sources = group.Sockets.Select(pair => new Source(pair.Key, pair.Value)).ToArray();
        entry.Connections = connections.ToArray();
        _bytes += entry.Bytes;
        return entry.Connections;
    }

    internal static void Reset() { _cache = null; _retained = null; _bytes = 0; _ticks = 0; }
}
