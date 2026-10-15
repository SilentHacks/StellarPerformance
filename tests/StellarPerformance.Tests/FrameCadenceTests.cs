using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Players.Interface.Model;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using UnityEngine;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class FrameCadenceTests
{
    [Fact]
    public void TerrainTraversalTranspilerGuardsOnlyTheNativeTraversalPair()
    {
        var original = ManagedMethod.Instructions(PlanetTraversalCadence.Target);
        var transformed = PlanetTraversalCadence.Transpile(original).ToList();
        Assert.Equal(original.Count + 1, transformed.Count);
        var before = transformed.FindIndex(code => code.Calls(AccessTools.Method(typeof(PlanetTraversalCadence), nameof(PlanetTraversalCadence.GuardedBefore))));
        Assert.True(before > 0);
        Assert.Equal(OpCodes.Ldarg_0, transformed[before - 1].opcode);
        Assert.Single(transformed, code => code.Calls(AccessTools.Method(typeof(PlanetTraversalCadence), nameof(PlanetTraversalCadence.GuardedTraverse))));
        Assert.DoesNotContain(transformed, code => code.Calls(PlanetTraversalCadence.NativeBefore) || code.Calls(PlanetTraversalCadence.NativeTraverse));
        var others = original.Where(code => !code.Calls(PlanetTraversalCadence.NativeBefore) && !code.Calls(PlanetTraversalCadence.NativeTraverse)).ToList();
        var kept = transformed.Where(code => !(code.operand is MethodInfo method && method.DeclaringType == typeof(PlanetTraversalCadence))).ToList();
        kept.RemoveAt(before - 1);
        Assert.Equal(others.Select(code => code.opcode), kept.Select(code => code.opcode));
        Assert.Equal(others.Select(code => code.operand), kept.Select(code => code.operand));
    }

    [Fact]
    public void TerrainTraversalReusesOnlySmallMovesAndRefreshesPeriodically()
    {
        var state = new PlanetTraversalCadence.State();
        Assert.False(PlanetTraversalCadence.ShouldSkip(state, Vector3.zero, 10, 0));
        state.Position = new Vector3(1000, 0, 0); state.Frame = 10;
        Assert.True(PlanetTraversalCadence.ShouldSkip(state, new Vector3(1000.2f, 0, 0), 11, 0));
        Assert.False(PlanetTraversalCadence.ShouldSkip(state, new Vector3(1000.3f, 0, 0), 11, 0));
        Assert.True(PlanetTraversalCadence.ShouldSkip(state, new Vector3(1150, 0, 0), 11, 100000));
        Assert.False(PlanetTraversalCadence.ShouldSkip(state, new Vector3(1250, 0, 0), 11, 100000));
        Assert.False(PlanetTraversalCadence.ShouldSkip(state, state.Position, 10 + PlanetTraversalCadence.MaximumFrames, 0));
        Assert.False(PlanetTraversalCadence.ShouldSkip(state, state.Position, 9, 0));
        Assert.False(PlanetTraversalCadence.ShouldSkip(state, new Vector3(float.NaN, 0, 0), 11, 0));
        Assert.False(PlanetTraversalCadence.ShouldSkip(state, state.Position, 11, float.PositiveInfinity));
    }

    [Fact]
    public void TerrainMeshDeltaIsReusedOnlyAfterAnEmptyDeltaForTheSameTraversal()
    {
        var state = new PlanetTraversalCadence.MeshState();
        Assert.False(PlanetTraversalCadence.CanReuseMeshes(state, 0, 10, false));
        state.Generation = 3; state.Generated = 10; state.Empty = true;
        Assert.True(PlanetTraversalCadence.CanReuseMeshes(state, 3, 10, false));
        Assert.False(PlanetTraversalCadence.CanReuseMeshes(state, 4, 10, false));
        Assert.False(PlanetTraversalCadence.CanReuseMeshes(state, 3, 9, false));
        Assert.False(PlanetTraversalCadence.CanReuseMeshes(state, 3, 10, true));
        state.Empty = false;
        Assert.False(PlanetTraversalCadence.CanReuseMeshes(state, 3, 10, false));
    }

    [Fact]
    public void FactorySimulationCarriesTheSkippedStepIntoTheNextRun()
    {
        var divisor = SimulationCadence.Divisor;
        try
        {
            SimulationCadence.Divisor = 2;
            var dt = 0.02f;
            Assert.False(SimulationCadence.Decide(ref dt, false));
            Assert.Equal(0.02f, dt);
            Assert.True(SimulationCadence.Decide(ref dt, false));
            Assert.Equal(0.04f, dt);
            dt = 0.02f;
            Assert.False(SimulationCadence.Decide(ref dt, false));
            Assert.True(SimulationCadence.Decide(ref dt, true));
            Assert.Equal(0.02f, dt);
            dt = float.NaN;
            Assert.True(SimulationCadence.Decide(ref dt, false));
            SimulationCadence.Divisor = 3;
            dt = 0.02f;
            Assert.False(SimulationCadence.Decide(ref dt, false));
            Assert.False(SimulationCadence.Decide(ref dt, false));
            Assert.True(SimulationCadence.Decide(ref dt, false));
            Assert.Equal(0.06f, dt, 6);
            SimulationCadence.Divisor = 1;
            dt = 0.02f;
            Assert.True(SimulationCadence.Decide(ref dt, false));
            Assert.Equal(0.02f, dt);
        }
        finally { SimulationCadence.Divisor = divisor; }
    }

    [Fact]
    public void FlightControlStatesKeepNativeTickRate()
    {
        Assert.True(SimulationCadence.NeedsEveryTick(PlayerStateType.Piloting));
        Assert.True(SimulationCadence.NeedsEveryTick(PlayerStateType.InteractingWithLever));
        Assert.True(SimulationCadence.NeedsEveryTick(PlayerStateType.WalkingRepositioningShip));
        Assert.False(SimulationCadence.NeedsEveryTick(PlayerStateType.Walking));
        Assert.False(SimulationCadence.NeedsEveryTick(PlayerStateType.InteractingWithChest));
    }

    [Fact]
    public void CableValueCadenceIsTimeBasedAndRecoversFromClockJumps()
    {
        var interval = CableVisualCadence.Interval;
        try
        {
            CableVisualCadence.Interval = 0.05f;
            var box = new CableVisualCadence.Box();
            Assert.True(CableVisualCadence.Due(box, 10f));
            Assert.False(CableVisualCadence.Due(box, 10.01f));
            Assert.False(CableVisualCadence.Due(box, 10.049f));
            Assert.True(CableVisualCadence.Due(box, 10.05f));
            Assert.True(CableVisualCadence.Due(box, 1f));
            CableVisualCadence.Interval = 0;
            Assert.True(CableVisualCadence.Due(box, 1.001f));
            Assert.True(CableVisualCadence.Due(box, 1.002f));
        }
        finally { CableVisualCadence.Interval = interval; }
    }

    [Fact]
    public void CableStateComparisonCoversEveryNativeField()
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Assert.Equal(new[] { "PlugA", "PlugB", "Type" }, typeof(CableState).GetFields(flags).Select(field => field.Name).OrderBy(name => name));
        Assert.Equal(new[] { "EntityPartSocket", "IsHeldByPlayer" }, typeof(CablePlugSocket).GetFields(flags).Select(field => field.Name).OrderBy(name => name));
        var a = new CableState { Type = CableType.Fluid, PlugA = new CablePlugSocket { EntityPartSocket = new EntityPartSocket(1, 2, 3) }, PlugB = new CablePlugSocket { EntityPartSocket = new EntityPartSocket(4, 5, 6) } };
        var variants = new[]
        {
            a,
            a with { },
            new CableState { Type = CableType.Signal, PlugA = a.PlugA, PlugB = a.PlugB },
            new CableState { Type = a.Type, PlugA = new CablePlugSocket { IsHeldByPlayer = true, EntityPartSocket = a.PlugA.EntityPartSocket }, PlugB = a.PlugB },
            new CableState { Type = a.Type, PlugA = a.PlugA, PlugB = new CablePlugSocket { EntityPartSocket = new EntityPartSocket(4, 5, 7) } },
            new CableState { Type = a.Type, PlugA = new CablePlugSocket { EntityPartSocket = new EntityPartSocket(9, 2, 3) }, PlugB = a.PlugB },
        };
        foreach (var left in variants)
            foreach (var right in variants)
                Assert.Equal(left.Equals(right), FluidCableTopology.SameState(left, right));
    }
}
