using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ships;
using Ships.Cables;
using Ships.Cables.Model;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Services;

namespace StellarPerformance;

// Filter each operation when the native enumerator reaches it, after earlier operations
// have published their results. Never prefilter the entire circuit from stale inputs.
internal static class StableSignals
{
    static StableSignals() { }
    internal const int MaximumEntries = 8192, MaximumSources = 16384;
    internal static double RecheckSeconds = 0.25;
    internal static long Skipped, Evaluated;
    [ThreadStatic] private static Circuit? _active;
    [ThreadStatic] private static SignalConnectionsServerUpdater? _owner;
    private static Func<SignalConnectionsServerUpdater, ConnectionsServerTracker> ConnectionsOf = null!;
    private static Func<SignalConnectionsServerUpdater, ShipsServerTracker> ShipsOf = null!;
    private static Func<ConnectionsTracker, SignalConnectionGroupValues> ValuesOf = null!;
    private static Func<SignalConnectionGroupValues, ConnectionGroupMap> MapOf = null!;
    private static readonly ConditionalWeakTable<SignalConnectionsServerUpdater, Circuit> Circuits = new ConditionalWeakTable<SignalConnectionsServerUpdater, Circuit>();
    internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    internal sealed class Observation
    {
        internal IStatefulPartState? Expected;
        internal double A, B, Until;
        internal double BusyUntil;
        internal int Changes;
        internal bool Ready;
        internal bool Unchanged(IStatefulPartState state, double a, double b, double now) => Ready && ReferenceEquals(Expected, state) && now < Until &&
            Finite(a) && Finite(b) && NativeStateReuse.Same(A, a) && NativeStateReuse.Same(B, b);
        internal void Observe(double a, double b, double now, double firstInterval = 1) { A = a; B = b; Until = now + RecheckSeconds * (Until == 0 ? firstInterval : 1); Ready = false; }
        internal void Complete(IStatefulPartState state) { Expected = state; Ready = true; }
    }
    private readonly struct Source
    {
        internal readonly StatefulPart Part;
        internal readonly byte Socket;
        internal Source(StatefulPart part, byte socket) { Part = part; Socket = socket; }
    }
    private sealed class Group
    {
        internal ConnectionGroup Topology = null!;
        internal int SocketCount;
        internal Source[] Sources = Array.Empty<Source>();
        internal double[] Last = Array.Empty<double>();
        internal bool Ready, Native;
        internal double Until;
        internal double BusyUntil;
        internal int Changes;
    }
    internal readonly struct Cookie
    {
        internal readonly Circuit? Previous;
        internal readonly SignalConnectionsServerUpdater? Owner;
        internal Cookie(Circuit? previous, SignalConnectionsServerUpdater? owner) { Previous = previous; Owner = owner; }
    }
    internal sealed class Circuit
    {
        internal int Depth;
        internal bool Valid;
        internal double Time;
        internal double NativeUntil;
        internal bool Checking;
        internal int Runs, Sleeps, BusySteps;
        internal readonly Dictionary<PartContext, Observation> Parts = new Dictionary<PartContext, Observation>();
        private readonly Dictionary<uint, Group> _groups = new Dictionary<uint, Group>();
        private int _sources;
        private StatefulPart? _pendingPart;
        private Observation? _pending;
        internal void Clear() { Parts.Clear(); _groups.Clear(); _sources = 0; _pending = null; _pendingPart = null; NativeUntil = 0; BusySteps = 0; }
        internal void Complete()
        {
            if (_pending != null && _pendingPart != null) _pending.Complete(_pendingPart.State);
            _pending = null; _pendingPart = null;
        }
        internal bool Part(PartContext context, ConnectionsServerTracker connections, ShipsServerTracker ships)
        {
            Parts.TryGetValue(context, out var entry);
            if (entry != null && Time < entry.BusyUntil) return true;
            if (entry != null && entry.BusyUntil != 0) { entry.BusyUntil = 0; entry.Changes = 0; }
            var ship = ships.GetShip(context.ShipId);
            if (ship == null || !ship.TryGetStatefulPart(context.PartId, out var part)) return true;
            var state = part.State;
            if (!EligibleProcessor(state)) return true;
            var a = connections.GetSocketSignalValue(new EntityPartSocket(context.ShipId, context.PartId, 0));
            var b = state.GetType() == typeof(MathGateState) ? connections.GetSocketSignalValue(new EntityPartSocket(context.ShipId, context.PartId, 1)) : 0;
            if (entry == null)
            {
                if (Parts.Count + _groups.Count >= MaximumEntries) return true;
                Parts.Add(context, entry = new Observation());
            }
            if (entry.Unchanged(state, a, b, Time)) { entry.Changes = 0; return false; }
            if (entry.Ready && (!NativeStateReuse.Same(entry.A, a) || !NativeStateReuse.Same(entry.B, b)))
            { if (++entry.Changes >= 2) entry.BusyUntil = Time + 0.25; }
            else entry.Changes = 0;
            entry.Observe(a, b, Time, Phase(context.PartId)); _pending = entry; _pendingPart = part;
            return true;
        }
        internal bool CableGroup(uint id, ConnectionsServerTracker connections, ShipsServerTracker ships)
        {
            _groups.TryGetValue(id, out var entry);
            if (entry != null && Time < entry.BusyUntil) return true;
            if (entry != null && entry.BusyUntil != 0) { entry.BusyUntil = 0; entry.Changes = 0; }
            // Native BuildOperationsStackCache invalidates all observations on cable,
            // part and loaded-ship changes. A value refresh must not rebuild wiring.
            if (entry == null)
            {
                if (entry == null && Parts.Count + _groups.Count >= MaximumEntries) return true;
                var values = ValuesOf(connections);
                if (!MapOf(values).TryGetConnectionGroupWithId(id, out var group)) { Clear(); return true; }
                if (group.Sockets.Count > 1024) return true;
                var native = false;
                foreach (var socket in group.Sockets.Keys)
                {
                    if (!ships.TryGetStatefulPart(socket.PartContext, out var part) || !NativeState(part.State)) { native = true; break; }
                }
                var sources = new List<Source>();
                if (!native)
                {
                    values.GetGroupInputsAndOutputs(id, out var inputs, out _);
                    foreach (var socket in inputs)
                    {
                        if (!ships.TryGetStatefulPart(socket.PartContext, out var part) || !NativeState(part.State)) { native = true; break; }
                        sources.Add(new Source(part, socket.Socket.SocketId));
                    }
                }
                var previousCount = entry?.Sources.Length ?? 0;
                if (_sources - previousCount + sources.Count > MaximumSources) return true;
                if (entry == null) _groups.Add(id, entry = new Group());
                _sources += sources.Count - previousCount;
                entry.Topology = group;
                entry.SocketCount = group.Sockets.Count;
                entry.Sources = sources.ToArray(); entry.Last = new double[sources.Count]; entry.Ready = false; entry.Native = native; entry.Until = Time + RecheckSeconds * Phase(id);
            }
            if (entry.Native) return true;
            var refresh = Time >= entry.Until;
            if (refresh)
            {
                if (!MapOf(ValuesOf(connections)).TryGetConnectionGroupWithId(id, out var current) ||
                    !ReferenceEquals(entry.Topology, current) || entry.SocketCount != current.Sockets.Count)
                { Clear(); return true; }
                entry.Until = Time + RecheckSeconds;
            }
            var changed = !entry.Ready || refresh;
            for (var i = 0; i < entry.Sources.Length; i++)
            {
                var source = entry.Sources[i];
                if (!NativeState(source.Part.State)) { entry.Native = true; return true; }
                var value = ReadEmitter(source.Part.State, source.Socket);
                if (!Finite(value) || !NativeStateReuse.Same(value, entry.Last[i])) changed = true;
                entry.Last[i] = value;
            }
            if (entry.Ready && changed) { if (++entry.Changes >= 2) entry.BusyUntil = Time + 0.25; }
            else if (!changed) entry.Changes = 0;
            entry.Ready = true;
            return changed;
        }
    }

    // Spread the first deadline over thirteen phases; later deadlines retain the
    // configured maximum interval. Inputs are still read on every checking step.
    internal static double Phase(uint id) => (id % 13 + 1) / 13.0;

    internal static bool NativeState(IStatefulPartState state) => state.GetType().Assembly == typeof(MathGateState).Assembly;
    internal static bool EligibleProcessor(IStatefulPartState state) => state.GetType() == typeof(MathGateState) || state.GetType() == typeof(SignalTransmitterState) ||
        state.GetType() == typeof(SignalProcessorState) && ((SignalProcessorState)state).Operation != SignalProcessorOperation.Delta;

    // Exact inspected native socket mapping, including the native default-zero behavior.
    internal static double ReadEmitter(IStatefulPartState state, byte socket)
    {
        switch (state)
        {
            case LeverState s: return s.LeverHeight;
            case PilotSeatState s: return socket == 0 ? s.MoveInputY : socket == 1 ? s.MoveInputZ : 0;
            case PilotSeatAdvState s: return socket == 2 ? s.MoveInput.x : socket == 1 ? s.MoveInput.y : socket == 0 ? s.MoveInput.z : socket == 5 ? s.RotationInput.x : socket == 4 ? s.RotationInput.y : socket == 3 ? s.RotationInput.z : 0;
            case TargetingComputerState s: return socket == 0 ? s.RelativePosition.x : socket == 1 ? s.RelativePosition.y : socket == 2 ? s.RelativePosition.z : 0;
            case MathGateState s: return socket == 2 ? s.Output : 0;
            case SignalProcessorState s: return socket == 1 ? s.Output : 0;
            case ConstantState s: return socket == 0 ? s.ConstantValue : 0;
            case DeltaState s: return socket == 1 ? s.DeltaValue : 0;
            case MemoryRegisterState s: return socket == 2 ? s.Value : 0;
            case VelocitySensorState s: return socket == 0 ? s.Velocity : 0;
            case AngularVelSensorState s: return socket == 0 || socket == 1 ? s.AngularVel : 0;
            case GasSensorState s: return socket == 0 ? s.Output : 0;
            case FluidTankState s: return socket == 2 ? s.FluidQuantity : 0;
            default: return 0;
        }
    }
    private static class Operation<T> where T : struct
    {
        internal static Func<T, int> Kind = null!;
        internal static Func<T, PartContext> Part = null!;
        internal static Func<T, uint> Group = null!;
    }
    internal static void BindOperation<T>() where T : struct
    {
        var op = Expression.Parameter(typeof(T));
        Operation<T>.Kind = Expression.Lambda<Func<T, int>>(Expression.Convert(Expression.Field(op, "Operation"), typeof(int)), op).Compile();
        Operation<T>.Part = Expression.Lambda<Func<T, PartContext>>(Expression.Field(op, "Part"), op).Compile();
        Operation<T>.Group = Expression.Lambda<Func<T, uint>>(Expression.Field(op, "CableGroupId"), op).Compile();
    }
    internal static bool MoveNext<T>(ref List<T>.Enumerator enumerator, SignalConnectionsServerUpdater updater) where T : struct
    {
        var circuit = _active;
        if (circuit == null || !ReferenceEquals(_owner, updater) || circuit.Depth != 1 || !circuit.Valid) return enumerator.MoveNext();
        if (!circuit.Checking)
        {
            var moved = enumerator.MoveNext();
            if (moved && DiagnosticCounters.Active) Evaluated++;
            return moved;
        }
        circuit.Complete();
        while (enumerator.MoveNext())
        {
            var op = enumerator.Current;
            var kind = Operation<T>.Kind(op);
            var run = kind == 0 ? circuit.Part(Operation<T>.Part(op), ConnectionsOf(updater), ShipsOf(updater)) :
                kind != 1 || circuit.CableGroup(Operation<T>.Group(op), ConnectionsOf(updater), ShipsOf(updater));
            if (run) { circuit.Runs++; if (DiagnosticCounters.Active) Evaluated++; return true; }
            circuit.Sleeps++;
            if (DiagnosticCounters.Active) Skipped++;
        }
        return false;
    }
    internal static void Begin(SignalConnectionsServerUpdater __instance, float deltaTime, out Cookie __state)
    {
        __state = new Cookie(_active, _owner);
        var circuit = Circuits.GetValue(__instance, _ => new Circuit());
        _active = circuit; _owner = __instance;
        circuit.Depth++;
        circuit.Valid = circuit.Depth == 1 && deltaTime > 0 && Finite(deltaTime);
        if (!circuit.Valid) { circuit.Clear(); return; }
        circuit.Time += deltaTime;
        circuit.Checking = circuit.Time >= circuit.NativeUntil;
        circuit.Runs = circuit.Sleeps = 0;
    }
    internal static Exception? End(Exception? __exception, SignalConnectionsServerUpdater __instance, Cookie __state)
    {
        try
        {
            var circuit = Circuits.GetValue(__instance, _ => new Circuit());
            if (__exception != null || circuit.Depth > 1) circuit.Clear(); else circuit.Complete();
            if (__exception == null && circuit.Valid && circuit.Depth == 1 && circuit.Checking && circuit.Runs > 0)
            {
                if (circuit.Sleeps > 0) circuit.BusySteps = 0;
                else if (++circuit.BusySteps >= 3) { circuit.NativeUntil = circuit.Time + 0.25; circuit.BusySteps = 0; }
            }
            circuit.Depth--;
            return __exception;
        }
        finally { _active = __state.Previous; _owner = __state.Owner; }
    }
    internal static void Invalidate(SignalConnectionsServerUpdater __instance) => Circuits.GetValue(__instance, _ => new Circuit()).Clear();
    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var result = new List<CodeInstruction>(); var count = 0;
        foreach (var original in instructions)
        {
            var code = new CodeInstruction(original);
            if (code.operand is MethodInfo called && called.Name == "MoveNext" && called.DeclaringType!.IsGenericType && called.DeclaringType.GetGenericTypeDefinition() == typeof(List<>.Enumerator))
            {
                result.Add(new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code).MoveBlocksFrom(code));
                code.opcode = OpCodes.Call;
                code.operand = AccessTools.Method(typeof(StableSignals), nameof(MoveNext)).MakeGenericMethod(called.DeclaringType.GetGenericArguments()[0]); count++;
            }
            result.Add(code);
        }
        if (count != 1) throw new InvalidOperationException("Native signal operation enumerator changed.");
        return result;
    }
    internal static void Bind()
    {
        ConnectionsOf = NativeAccess.Read<SignalConnectionsServerUpdater, ConnectionsServerTracker>("_connections");
        ShipsOf = NativeAccess.Read<SignalConnectionsServerUpdater, ShipsServerTracker>("_ships");
        ValuesOf = NativeAccess.Read<ConnectionsTracker, SignalConnectionGroupValues>("SignalConnectionGroupValues");
        MapOf = NativeAccess.Read<SignalConnectionGroupValues, ConnectionGroupMap>("_groupMap");
        var operation = typeof(SignalConnectionsServerUpdater).GetNestedType("SignalEvaluationOperation", BindingFlags.NonPublic) ?? throw new MissingMemberException("SignalEvaluationOperation");
        AccessTools.Method(typeof(StableSignals), nameof(BindOperation)).MakeGenericMethod(operation).Invoke(null, null);
    }
    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var execute = AccessTools.Method(typeof(SignalConnectionsServerUpdater), "ExecuteOperations");
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(typeof(SignalConnectionsServerUpdater), "BuildOperationsStackCache"), harmony.Id);
        foreach (var name in new[] { "OnEnable", "OnDisable", "CheckCacheOnShipLoad", "CheckCacheOnPartChanged", "CheckCableGroupOnNextTick", "UpdateSignalValues" })
            PatchSafety.RequireOriginalBody(AccessTools.Method(typeof(SignalConnectionsServerUpdater), name), harmony.Id, "stellarperformance.signal-evaluation");
        PatchSafety.RequireNoForeignOwners(execute, harmony.Id, "stellarperformance.native-state-reuse");
        foreach (var method in new[] { AccessTools.Method(typeof(SignalConnectionGroupValues), "GetSocketSignal"), AccessTools.Method(typeof(SignalConnectionGroupValues), "TryAddSocket"),
            AccessTools.Method(typeof(SignalConnectionGroupValues), nameof(SignalConnectionGroupValues.GetGroupInputsAndOutputs)), AccessTools.Method(typeof(ConnectionsTracker), nameof(ConnectionsTracker.GetSocketSignalValue)),
            AccessTools.Method(typeof(ConnectionsTracker), nameof(ConnectionsTracker.UpdateSignalGroupValue)) }) PatchSafety.RequireNoForeignOwners(method, harmony.Id);
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(typeof(SignalConnectionGroupValues), nameof(SignalConnectionGroupValues.UpdateGroupSignalValue)), harmony.Id, "stellarperformance.signal-evaluation", "stellarperformance.client-signal-cables");
        Bind();
        harmony.Patch(execute, prefix: new HarmonyMethod(typeof(StableSignals), nameof(Begin)), transpiler: new HarmonyMethod(typeof(StableSignals), nameof(Transpile)), finalizer: new HarmonyMethod(typeof(StableSignals), nameof(End)));
        harmony.Patch(AccessTools.Method(typeof(SignalConnectionsServerUpdater), "BuildOperationsStackCache"), prefix: new HarmonyMethod(typeof(StableSignals), nameof(Invalidate)));
    }
}
