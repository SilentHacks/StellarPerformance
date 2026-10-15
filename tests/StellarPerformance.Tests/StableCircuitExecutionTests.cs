using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
using Ships.Interface.Model.State;
using Ships.Services;
using Xunit;

namespace StellarPerformance.Tests;

[Collection("Diagnostic counters")]
public sealed class StableCircuitExecutionTests
{
    private sealed class Circuit
    {
        internal readonly ShipState State = new ShipState(1, 1, null!);
        internal readonly TrackedShipServer Ship = Bare<TrackedShipServer>();
        internal readonly ShipsServerTracker Ships = Bare<ShipsServerTracker>();
        internal readonly ConnectionsServerTracker Connections = Bare<ConnectionsServerTracker>();
        internal readonly SignalConnectionsServerUpdater Updater = Bare<SignalConnectionsServerUpdater>();
        internal readonly ConnectionGroupMap Map = new ConnectionGroupMap();
        internal readonly Dictionary<ushort, StatefulPart> Parts;
        internal int Updates;
        private readonly IList _operations;
        private readonly Type _op;
        internal Circuit()
        {
            StableSignals.Bind();
            Parts = (Dictionary<ushort, StatefulPart>)AccessTools.Field(typeof(ShipState), "_statefulParts").GetValue(State)!;
            Set(typeof(TrackedShip), Ship, "<State>k__BackingField", State);
            Set(typeof(ShipsServerTracker), Ships, "_shipsDict", new Dictionary<uint, TrackedShipServer> { [1] = Ship });
            var values = new SignalConnectionGroupValues(Map, Ships);
            Set(typeof(ConnectionsTracker), Connections, "SignalConnectionGroupValues", values);
            Set(typeof(ConnectionsTracker), Connections, "SignalGroupMap", Map);
            Set(typeof(ConnectionsTracker), Connections, "Ships", Ships);
            Set(typeof(SignalConnectionsServerUpdater), Updater, "_connections", Connections);
            Set(typeof(SignalConnectionsServerUpdater), Updater, "_ships", Ships);
            _op = typeof(SignalConnectionsServerUpdater).GetNestedType("SignalEvaluationOperation", BindingFlags.NonPublic)!;
            _operations = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(_op))!;
            Set(typeof(SignalConnectionsServerUpdater), Updater, "_operations", _operations);
            State.StatefulPartUpdated += _ => Updates++;
        }
        internal void Add(ushort id, IStatefulPartState state)
        { var part = Bare<StatefulPart>(); part.Id = id; part.State = state; Parts[id] = part; }
        internal void Connect(uint id, ushort a, byte socketA, ushort b, byte socketB)
        {
            var group = new ConnectionGroup(id);
            var connection = new SocketConnection(new EntityPartSocket(1, a, socketA), new EntityPartSocket(1, b, socketB), SocketConnectionType.Cable) { CableId = id };
            group.AddConnection(connection);
            ((Dictionary<uint, ConnectionGroup>)AccessTools.Field(typeof(ConnectionGroupMap), "_connectionGroups").GetValue(Map)!)[id] = group;
            var lookup = (Dictionary<EntityPartSocket, uint>)AccessTools.Field(typeof(ConnectionGroupMap), "_socketToGroupId").GetValue(Map)!;
            lookup[connection.SocketA!.Value] = id; lookup[connection.SocketB!.Value] = id;
        }
        internal void Op(int kind, uint id)
        {
            var value = Activator.CreateInstance(_op)!;
            var field = _op.GetField("Operation")!;
            field.SetValue(value, Enum.ToObject(field.FieldType, kind));
            _op.GetField("Part")!.SetValue(value, new PartContext { ShipId = 1, PartId = (ushort)id });
            _op.GetField("CableGroupId")!.SetValue(value, id);
            _operations.Add(value);
        }
        internal void Step(bool sleep, float dt = .02f)
        {
            if (!sleep) { Native(Updater, dt); return; }
            StableSignals.Begin(Updater, dt, out var cookie);
            try { Sleeping(Updater, dt); }
            catch (Exception error) { StableSignals.End(error, Updater, cookie); throw; }
            StableSignals.End(null, Updater, cookie);
        }
        internal double Output(ushort id) => ((MathGateState)Parts[id].State).Output;
    }
    private static T Bare<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Set(Type owner, object instance, string name, object value) => AccessTools.Field(owner, name).SetValue(instance, value);
    private static readonly MethodInfo Execute = AccessTools.Method(typeof(SignalConnectionsServerUpdater), "ExecuteOperations");
    private static readonly Action<SignalConnectionsServerUpdater, float> Native = (Action<SignalConnectionsServerUpdater, float>)ManagedMethod.Clone(Execute, (codes, _) => NativeStateReuse.Transpile(codes, Execute));
    private static readonly Action<SignalConnectionsServerUpdater, float> Sleeping = (Action<SignalConnectionsServerUpdater, float>)ManagedMethod.Clone(Execute, (codes, _) => StableSignals.Transpile(NativeStateReuse.Transpile(codes, Execute)));

    private static Circuit Chain()
    {
        var c = new Circuit();
        c.Add(1, new ConstantState { ConstantValue = 2 });
        c.Add(2, new MathGateState { Operation = MathGateOperation.Add });
        c.Add(3, new MathGateState { Operation = MathGateOperation.Multiply });
        c.Add(4, new DeltaState());
        c.Add(5, new ConstantState { ConstantValue = 3 });
        c.Connect(10, 1, 0, 2, 0);
        c.Connect(11, 5, 0, 2, 1);
        c.Connect(12, 2, 2, 3, 0);
        c.Connect(13, 3, 2, 4, 0);
        // The second gate uses an unconnected input (native default 0), with Add.
        c.Parts[3].State = new MathGateState { Operation = MathGateOperation.Add };
        c.Op(1, 10); c.Op(1, 11); c.Op(0, 2); c.Op(1, 12); c.Op(0, 3); c.Op(1, 13); c.Op(0, 4);
        return c;
    }

    [Fact]
    public void EmittedNativeLoopSleepsStableGatesAndPropagatesChangedInputsThroughTheChainInOneTick()
    {
        var expected = Chain(); var actual = Chain();
        for (var tick = 0; tick < 30; tick++)
        {
            if (tick == 5) { expected.State.SetPartState(1, new ConstantState { ConstantValue = 10 }); actual.State.SetPartState(1, new ConstantState { ConstantValue = 10 }); }
            if (tick == 10) { expected.State.SetPartState(2, new MathGateState { Operation = MathGateOperation.Multiply }); actual.State.SetPartState(2, new MathGateState { Operation = MathGateOperation.Multiply }); }
            expected.Step(false); actual.Step(true);
            Assert.Equal(expected.Output(2), actual.Output(2)); Assert.Equal(expected.Output(3), actual.Output(3));
            Assert.Equal(expected.Parts[4].State, actual.Parts[4].State);
            if (tick == 5) Assert.Equal(13, actual.Output(3));
            if (tick == 10) Assert.Equal(30, actual.Output(3));
        }
        Assert.True(actual.Updates < expected.Updates / 2);
    }

    [Fact]
    public void FeedbackLoopRemainsAwakeAndTopologyInvalidationRecomputesBeforeSleepingAgain()
    {
        Circuit Feedback()
        {
            var c = new Circuit(); c.Add(1, new ConstantState { ConstantValue = 2 }); c.Add(2, new MathGateState { Operation = MathGateOperation.Add });
            c.Connect(10, 2, 2, 2, 0); c.Connect(11, 1, 0, 2, 1);
            c.Op(1, 10); c.Op(1, 11); c.Op(0, 2); return c;
        }
        var expected = Feedback(); var actual = Feedback();
        for (var i = 1; i <= 30; i++) { expected.Step(false); actual.Step(true); Assert.Equal(2 * i, actual.Output(2)); Assert.Equal(expected.Output(2), actual.Output(2)); }
        var stable = Chain(); stable.Step(true); stable.Step(true);
        var before = stable.Updates;
        StableSignals.Invalidate(stable.Updater); stable.Step(true);
        Assert.Equal(before + 3, stable.Updates);
    }

    [Fact]
    public void ExceptionsAndNestedExecutionCannotRetainPartiallyComputedSleepObservations()
    {
        var c = Chain(); c.Step(true); c.Step(true);
        StableSignals.Begin(c.Updater, .02f, out var outer); StableSignals.Begin(c.Updater, .02f, out var inner);
        StableSignals.End(new InvalidOperationException(), c.Updater, inner); StableSignals.End(null, c.Updater, outer);
        var before = c.Updates; c.Step(true); Assert.Equal(before + 3, c.Updates);
    }

    [Fact]
    public void EveryNativeMathAndUnaryOperationKeepsItsResultsAcrossStableTinyAndNonfiniteInputs()
    {
        foreach (var operation in Enum.GetValues<MathGateOperation>())
            Compare(new MathGateState { Operation = operation });
        foreach (var operation in Enum.GetValues<SignalProcessorOperation>())
            Compare(new SignalProcessorState { Operation = operation });
        Compare(new SignalTransmitterState());

        static void Compare(IStatefulPartState processor)
        {
            Circuit Create()
            {
                var circuit = new Circuit(); circuit.Add(1, new ConstantState { ConstantValue = .25 }); circuit.Add(2, processor);
                circuit.Connect(10, 1, 0, 2, 0); circuit.Op(1, 10); circuit.Op(0, 2); return circuit;
            }
            var expected = Create(); var actual = Create();
            foreach (var input in new[] { .25, .25 + 1e-12, -0d, double.NaN, double.PositiveInfinity, -.5 })
            {
                expected.State.SetPartState(1, new ConstantState { ConstantValue = input }); actual.State.SetPartState(1, new ConstantState { ConstantValue = input });
                for (var tick = 0; tick < 16; tick++)
                {
                    var dt = tick % 2 == 0 ? .02f : .04f;
                    expected.Step(false, dt); actual.Step(true, dt);
                    Assert.Equal(expected.Parts[2].State, actual.Parts[2].State);
                }
            }
        }
    }

    [Fact]
    public void BusyCircuitsKeepNativeValuesAndReturnToSleepingAfterInputsSettle()
    {
        var expected = Chain(); var actual = Chain();
        var table = (ConditionalWeakTable<SignalConnectionsServerUpdater, StableSignals.Circuit>)AccessTools.Field(typeof(StableSignals), "Circuits").GetValue(null)!;
        var backedOff = false;
        for (var tick = 0; tick < 40; tick++)
        {
            expected.State.SetPartState(1, new ConstantState { ConstantValue = tick + 2 }); actual.State.SetPartState(1, new ConstantState { ConstantValue = tick + 2 });
            expected.Step(false); actual.Step(true);
            Assert.Equal(expected.Output(3), actual.Output(3));
            Assert.True(table.TryGetValue(actual.Updater, out var circuit));
            backedOff |= circuit.Parts.Values.Any(entry => entry.BusyUntil > circuit.Time);
        }
        Assert.True(backedOff);
        for (var tick = 0; tick < 40; tick++) { expected.Step(false); actual.Step(true); Assert.Equal(expected.Output(3), actual.Output(3)); }
        var before = actual.Updates;
        for (var tick = 0; tick < 10; tick++) actual.Step(true);
        Assert.True(actual.Updates - before < 20); // Delta stays awake; stable gates sleep again.
    }

    [Fact]
    public void NestedDifferentUpdaterRestoresTheOuterOperationScope()
    {
        var outer = Chain(); var inner = Chain(); outer.Step(true); inner.Step(true);
        StableSignals.Begin(outer.Updater, .02f, out var cookie);
        var before = outer.Updates;
        try { inner.Step(true); Sleeping(outer.Updater, .02f); }
        finally { StableSignals.End(null, outer.Updater, cookie); }
        Assert.Equal(before + 1, outer.Updates); // Only the native Delta operation runs.
        Assert.Equal(5, outer.Output(3));
    }

    [Fact]
    public void PeriodicValueRefreshKeepsWiringArraysAndTopologyChangesRebuildThem()
    {
        var c = Chain(); c.Step(true);
        var table = (ConditionalWeakTable<SignalConnectionsServerUpdater, StableSignals.Circuit>)AccessTools.Field(typeof(StableSignals), "Circuits").GetValue(null)!;
        Assert.True(table.TryGetValue(c.Updater, out var circuit));
        var groups = (IDictionary)AccessTools.Field(typeof(StableSignals.Circuit), "_groups").GetValue(circuit)!;
        var group = groups[10u]!;
        var sources = AccessTools.Field(group.GetType(), "Sources").GetValue(group);
        for (var i = 0; i < 100; i++) c.Step(true);
        Assert.Same(sources, AccessTools.Field(group.GetType(), "Sources").GetValue(groups[10u]));
        c.Add(6, new ConstantState { ConstantValue = 17 });
        c.Connect(10, 6, 0, 2, 0);
        StableSignals.Invalidate(c.Updater);
        c.Step(true);
        Assert.Equal(20, c.Output(3));
        Assert.NotSame(group, groups[10u]);
    }
}
