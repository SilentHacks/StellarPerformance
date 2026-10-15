using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ships;
using Ships.Cables;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Model.State;
using Ships.Interface.Services;
using Xunit;

namespace StellarPerformance.Tests;

[Collection("Diagnostic counters")]
public sealed class StableSignalTests
{
    public class Provider : DispatchProxy
    {
        public IShipStateRead? Ship;
        public StatefulPart? Part;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "TryGetShip") { args![1] = Ship; return Ship != null; }
            if (method.Name == "TryGetStatefulPart") { args![1] = Part; return Part != null; }
            if (method.Name == "get_Id") return 1u;
            return method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }
    private static StatefulPart MakePart(IStatefulPartState state)
    { var part = (StatefulPart)RuntimeHelpers.GetUninitializedObject(typeof(StatefulPart)); part.Id = 1; part.State = state; return part; }

    [Fact]
    public void StableObservationsKeepTinyChangesAndConfigurationChangesAndPeriodicRefresh()
    {
        var state = new MathGateState { Output = 1 };
        var entry = new StableSignals.Observation();
        entry.Observe(1, 2, 1); entry.Complete(state);
        Assert.True(entry.Unchanged(state, 1, 2, 1.02));
        Assert.False(entry.Unchanged(state, 1 + 1e-12, 2, 1.02));
        Assert.False(entry.Unchanged(state with { Operation = MathGateOperation.Add }, 1, 2, 1.02));
        Assert.False(entry.Unchanged(state, 1, 2, 1.25));
        Assert.False(entry.Unchanged(state, double.NaN, 2, 1.02));
        Assert.False(StableSignals.EligibleProcessor(new DeltaState()));
        Assert.False(StableSignals.EligibleProcessor(new SignalProcessorState { Operation = SignalProcessorOperation.Delta }));
        Assert.True(StableSignals.EligibleProcessor(new SignalProcessorState { Operation = SignalProcessorOperation.Sin }));
    }

    [Fact]
    public void CachedEmitterMappingMatchesNativeForEveryInspectedSocketIncludingSpecialValues()
    {
        var ship = DispatchProxy.Create<IShipStateRead, Provider>();
        var provider = DispatchProxy.Create<IShipsProvider, Provider>();
        ((Provider)(object)provider).Ship = ship;
        var values = new SignalConnectionGroupValues(new ConnectionGroupMap(), provider);
        var native = (Func<EntityPartSocket, double>)Delegate.CreateDelegate(typeof(Func<EntityPartSocket, double>), values, AccessTools.Method(typeof(SignalConnectionGroupValues), "GetSocketSignal"));
        foreach (var number in new[] { 0d, -0d, 1.25, -42, double.NaN, double.PositiveInfinity })
        {
            var states = new IStatefulPartState[] { new MathGateState { Output = number }, new SignalProcessorState { Output = number }, new ConstantState { ConstantValue = number },
                new DeltaState { DeltaValue = number }, new MemoryRegisterState { Value = number }, new FluidTankState { FluidQuantity = (float)number },
                new GasSensorState { Output = (float)number }, new LeverState { LeverHeight = (float)number }, new VelocitySensorState { Velocity = (float)number },
                new AngularVelSensorState { AngularVel = (float)number }, new PilotSeatState(), new PilotSeatAdvState(), new TargetingComputerState(), new FluidPumpState() };
            foreach (var state in states)
            {
                ((Provider)(object)ship).Part = MakePart(state);
                for (byte socket = 0; socket < 8; socket++)
                    Assert.Equal(BitConverter.DoubleToInt64Bits(native(new EntityPartSocket(1, 1, socket))), BitConverter.DoubleToInt64Bits(StableSignals.ReadEmitter(state, socket)));
            }
        }
    }

    [Fact]
    public void NativeOperationLoopComposesWithStateReuseAndOnlyAddsAnEnumeratorDecision()
    {
        StableSignals.Bind();
        var method = AccessTools.Method(typeof(SignalConnectionsServerUpdater), "ExecuteOperations");
        var original = ManagedMethod.Instructions(method);
        var transformed = StableSignals.Transpile(NativeStateReuse.Transpile(original, method)).ToArray();
        Assert.Single(transformed, code => code.operand is MethodInfo m && m.DeclaringType == typeof(StableSignals) && m.Name == nameof(StableSignals.MoveNext));
        Assert.Equal(original.Count + 1, transformed.Length);
        Assert.Throws<InvalidOperationException>(() => StableSignals.Transpile(Array.Empty<CodeInstruction>()).ToArray());
    }
}
