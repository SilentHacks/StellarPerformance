using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Xunit;

namespace StellarPerformance.Tests;

[Collection("Diagnostic counters")]
public sealed class NativeStateReuseTests
{
    private static ThrusterState NativeThruster(ThrusterState previous, float strength) => previous with { ThrusterStrength = strength };
    private static FluidValveState NativeValve(FluidValveState previous, float transfer, float opened) => previous with { CurrentTransferRate = transfer, ValveOpenedRate = opened };
    private static SignalProcessorState NativeProcessor(SignalProcessorState previous, double input, double output) => previous with { PreviousValue = input, Output = output };
    private record CustomThruster : ThrusterState { public int Extra { get; init; } }

    [Fact]
    public void EveryInspectedConstructionSiteTransformsAndRetainsNativeStateNotifications()
    {
        foreach (var method in new[] { NativeStateReuse.Thrusters, NativeStateReuse.Valves, NativeStateReuse.Signals })
        {
            Assert.NotNull(method);
            var original = ManagedMethod.Instructions(method);
            var changed = NativeStateReuse.Transpile(original, method).ToList();
            Assert.Equal(original.Count, changed.Count);
            var replaced = NativeStateReuse.Recipes(method).Sum(r => r.Count);
            Assert.Equal(replaced, changed.Count(c => c.operand is MethodInfo m && m.DeclaringType == typeof(NativeStateReuse)));
            // Neither state publication nor any fluid/signal calculations are removed.
            var removedCalls = original.Where(c => c.operand is MethodInfo).Select(c => (MethodInfo)c.operand).Except(changed.Where(c => c.operand is MethodInfo).Select(c => (MethodInfo)c.operand));
            Assert.All(removedCalls, m => Assert.True(m.Name == "<Clone>$" || m.Name.StartsWith("set_"), m.Name));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmittedNativeWithExpressionsPreserveValuesAndReuseUnchangedRecords(bool capture)
    {
        var previous = DiagnosticCounters.Active;
        DiagnosticCounters.Active = capture;
        var reused = NativeStateReuse.Reused;
        var created = NativeStateReuse.Created;
        try
        {
            var thruster = Rewrite<Func<ThrusterState, float, ThrusterState>>(nameof(NativeThruster), NativeStateReuse.Recipes(NativeStateReuse.Thrusters));
            var valve = Rewrite<Func<FluidValveState, float, float, FluidValveState>>(nameof(NativeValve), new[] { new NativeStateReuse.Recipe(typeof(FluidValveState), 1, nameof(NativeStateReuse.Valve), "CurrentTransferRate", "ValveOpenedRate") });
            var processor = Rewrite<Func<SignalProcessorState, double, double, SignalProcessorState>>(nameof(NativeProcessor), new[] { new NativeStateReuse.Recipe(typeof(SignalProcessorState), 1, nameof(NativeStateReuse.Processor), "PreviousValue", "Output") });
            var a = new ThrusterState { ThrusterStrength = 0.5f };
            Assert.Same(a, thruster(a, 0.5f));
            Assert.Equal(NativeThruster(a, 0.75f), thruster(a, 0.75f));
            Assert.Equal(0.5f, a.ThrusterStrength);
            var b = new FluidValveState { CurrentTransferRate = 0.2f, ValveOpenedRate = 0.4f, IsHeldByPlayer = true, HoldingPlayerId = 123 };
            Assert.Same(b, valve(b, 0.2f, 0.4f));
            Assert.Equal(NativeValve(b, 0.3f, 0.5f), valve(b, 0.3f, 0.5f));
            var c = new SignalProcessorState { PreviousValue = 1, Output = 2, Operation = SignalProcessorOperation.Delta };
            Assert.Same(c, processor(c, 1, 2));
            Assert.Equal(NativeProcessor(c, 2, 3), processor(c, 2, 3));
            Assert.Equal(1, c.PreviousValue);

            // Identity proves that no replacement record was created. A process-wide JIT
            // transition can allocate on this thread, so a zero-byte microbenchmark here
            // would measure the .NET test host as well as the factory, rather than Mono.
            for (var i = 0; i < 100; i++) Assert.Same(a, thruster(a, 0.5f));
            Assert.Equal(capture ? 103L : 0L, NativeStateReuse.Reused - reused);
            Assert.Equal(capture ? 3L : 0L, NativeStateReuse.Created - created);
        }
        finally { DiagnosticCounters.Active = previous; }
    }

    [Fact]
    public void FloatingPointPayloadsAndDerivedRecordsKeepNativeSemantics()
    {
        Assert.False(NativeStateReuse.Same(0f, -0f));
        Assert.False(NativeStateReuse.Same(0d, -0d));
        var nanA = BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000001));
        var nanB = BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000002));
        Assert.True(NativeStateReuse.Same(nanA, nanA));
        Assert.False(NativeStateReuse.Same(nanA, nanB));
        var state = new MathGateState { Output = nanA };
        Assert.Same(state, NativeStateReuse.MathGate(state, nanA));
        Assert.Equal(BitConverter.DoubleToInt64Bits(nanB), BitConverter.DoubleToInt64Bits(NativeStateReuse.MathGate(state, nanB).Output));
        var custom = new CustomThruster { Extra = 321, ThrusterStrength = 0.5f };
        var result = NativeStateReuse.Thruster(custom, 0.5f);
        Assert.NotSame(custom, result);
        Assert.Equal(321, Assert.IsType<CustomThruster>(result).Extra);
    }

    [Fact]
    public void TankNormalizationAndNativePublicationCallbacksArePreserved()
    {
        var tank = new FluidTankState { FluidItemId = 0, FluidQuantity = 0f, FluidFlow = 0f };
        IStatefulPartWithFluidContainer result = null!;
        Assert.False(NativeStateReuse.BeforeTankWithFluid(tank, 99, 0f, 0f, ref result));
        Assert.Same(tank, result);
        Assert.True(NativeStateReuse.BeforeTankWithFluid(tank, 99, 1f, 0f, ref result));
        Assert.True(NativeStateReuse.BeforeTankWithFluid(tank, 0, -0f, 0f, ref result));

        var ship = new ShipState(1, 1, null!);
        var part = (StatefulPart)RuntimeHelpers.GetUninitializedObject(typeof(StatefulPart));
        part.Id = 10; part.State = tank;
        var parts = (Dictionary<ushort, StatefulPart>)AccessTools.Field(typeof(ShipState), "_statefulParts").GetValue(ship)!;
        parts[10] = part;
        var localCalls = 0; var shipCalls = 0;
        ship.StatefulPartUpdated += e => { localCalls++; Assert.Same(tank, e.NewState); };
        ship.ShipStatefulPartUpdated += (id, e) => shipCalls++;
        ship.SetPartState(10, tank);
        Assert.Equal(1, localCalls); Assert.Equal(1, shipCalls);
    }

    [Fact]
    public void ChangedPatternsFailClosed()
    {
        var method = typeof(NativeStateReuseTests).GetMethod(nameof(NativeThruster), BindingFlags.NonPublic | BindingFlags.Static)!;
        var codes = ManagedMethod.Instructions(method);
        codes.Single(c => c.opcode == OpCodes.Dup).opcode = OpCodes.Pop;
        Assert.Throws<InvalidOperationException>(() => NativeStateReuse.Rewrite(codes, NativeStateReuse.Recipes(NativeStateReuse.Thrusters)).ToArray());
    }

    private static T Rewrite<T>(string method, NativeStateReuse.Recipe[] recipes) where T : Delegate => (T)ManagedMethod.Clone(typeof(NativeStateReuseTests).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!, (codes, _) => NativeStateReuse.Rewrite(codes, recipes));
}
