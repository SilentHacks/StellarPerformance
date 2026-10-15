using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.State;
using UnityEngine;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class WorldUiAndBatchingTests
{
    [Fact]
    public void HiddenDisplayCoalescesChangesAndRevertingToDisplayedValueCancelsPendingWork()
    {
        var original = new SignalDisplayState { SignalValue = 1 };
        var state = new WorldUiVisibility.PendingDisplay { Displayed = original };
        Assert.False(state.Observe(new SignalDisplayState { SignalValue = 2 }, false));
        Assert.True(state.Pending);
        Assert.False(state.Observe(new SignalDisplayState { SignalValue = 3 }, false));
        Assert.Equal(3, ((SignalDisplayState)state.Latest!).SignalValue);
        Assert.False(state.Observe(original, false));
        Assert.False(state.Pending);
        Assert.True(state.Observe(new SignalDisplayState { SignalValue = 4 }, true));
        Assert.False(state.Pending);
        Assert.Equal(4, ((SignalDisplayState)state.Displayed!).SignalValue);
    }

    [Fact]
    public void DisplayComparisonIgnoresUnrenderedSignalValuesButKeepsOperationAndExactNumberChanges()
    {
        Assert.True(WorldUiVisibility.SamePresentation(new SignalProcessorState { PreviousValue = 1, Output = 2 }, new SignalProcessorState { PreviousValue = 2, Output = 3 }));
        Assert.False(WorldUiVisibility.SamePresentation(new SignalProcessorState(), new SignalProcessorState { Operation = SignalProcessorOperation.Delta }));
        Assert.False(WorldUiVisibility.SamePresentation(new SignalDisplayState { SignalValue = 0 }, new SignalDisplayState { SignalValue = -0f }));
        Assert.False(WorldUiVisibility.SamePresentation(new ThrusterState(), new ThrusterState()));
    }

    [Fact]
    public void ConservativeFrustumTestKeepsIntersectingAndBoundaryTouchingBounds()
    {
        var planes = new[] { new Plane { normal = Vector3.right, distance = 1 }, new Plane { normal = Vector3.left, distance = 1 } };
        Assert.True(WorldUiVisibility.Intersects(new Bounds(Vector3.zero, Vector3.one), planes));
        Assert.True(WorldUiVisibility.Intersects(new Bounds(new Vector3(1.5f, 0, 0), Vector3.one), planes));
        Assert.False(WorldUiVisibility.Intersects(new Bounds(new Vector3(1.51f, 0, 0), Vector3.one), planes));
    }

    [Fact]
    public void CachedBoundsTransformContainsAllEightCornersUnderRotationScaleAndTranslation()
    {
        var matrix = Matrix4x4.identity;
        matrix.m00 = 0; matrix.m01 = -2; matrix.m10 = 3; matrix.m11 = 0; matrix.m22 = -4;
        matrix.m03 = 10; matrix.m13 = -20; matrix.m23 = 30;
        var original = new Bounds(new Vector3(1, 2, 3), new Vector3(4, 6, 8));
        var transformed = WorldUiVisibility.TransformBounds(original, matrix);
        var min = transformed.min; var max = transformed.max;
        foreach (var x in new[] { original.min.x, original.max.x })
            foreach (var y in new[] { original.min.y, original.max.y })
                foreach (var z in new[] { original.min.z, original.max.z })
                {
                    var p = matrix.MultiplyPoint3x4(new Vector3(x, y, z));
                    Assert.InRange(p.x, min.x, max.x); Assert.InRange(p.y, min.y, max.y); Assert.InRange(p.z, min.z, max.z);
                }
    }

    [Fact]
    public void VisualEventGuardsKeepThrusterTransitionsAndOnlySkipIdleWritesAndUnchangedValveAngle()
    {
        var idle = new ThrusterState(); var active = new ThrusterState { ThrusterStrength = 1 };
        Assert.False(PartVisualUpdates.BeforeThrusterStateChange(new StatefulPartStateChangeEvent(null!, idle, idle)));
        Assert.True(PartVisualUpdates.BeforeThrusterStateChange(new StatefulPartStateChangeEvent(null!, active, idle)));
        Assert.True(PartVisualUpdates.BeforeThrusterStateChange(new StatefulPartStateChangeEvent(null!, idle, active)));
        Assert.True(PartVisualUpdates.BeforeThrusterStateChange(new StatefulPartStateChangeEvent(null!, active, active)));
        Assert.False(PartVisualUpdates.BeforeValveStateChange(new StatefulPartStateChangeEvent(null!, new FluidValveState { ValveOpenedRate = 0.5f }, new FluidValveState { ValveOpenedRate = 0.5f, CurrentTransferRate = 1 })));
        Assert.True(PartVisualUpdates.BeforeValveStateChange(new StatefulPartStateChangeEvent(null!, new FluidValveState(), new FluidValveState { ValveOpenedRate = 1 })));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(15.9f, 0)]
    [InlineData(16, 1)]
    [InlineData(-0.01f, -1)]
    [InlineData(-16, -1)]
    [InlineData(-16.01f, -2)]
    public void BatchesUseBoundedShipLocalCells(float coordinate, int expected) => Assert.Equal(expected, ShipPartBatching.Cell(coordinate));

    [Fact]
    public void BatchingExcludesUnknownShadersAndDoesNotUseTolerantUnityTransformEquality()
    {
        Assert.True(ShipPartBatching.SupportedShader("Universal Render Pipeline/Lit"));
        Assert.False(ShipPartBatching.SupportedShader("Shader Graphs/HullArray"));
        Assert.False(ShipPartBatching.SupportedShader("Hidden/ObjectId"));
        Assert.False(ShipPartBatching.SupportedShader("Custom/PlanetObject"));
        Assert.True(ShipPartBatching.SupportedShader("Custom/PlanetObject", true));
        Assert.False(ShipPartBatching.Equal(Vector3.zero, new Vector3(1e-8f, 0, 0)));
        Assert.False(ShipPartBatching.Equal(Quaternion.identity, new Quaternion(1e-8f, 0, 0, 1)));
    }

    [Fact]
    public void BatchingRejectsNonuniformMirroredShearedAndNonfiniteTransforms()
    {
        var matrix = Matrix4x4.identity;
        Assert.True(ShipPartBatching.UniformPositive(matrix));
        matrix.m00 = matrix.m11 = matrix.m22 = 2;
        Assert.True(ShipPartBatching.UniformPositive(matrix));
        matrix.m00 = 3; Assert.False(ShipPartBatching.UniformPositive(matrix));
        matrix = Matrix4x4.identity; matrix.m00 = -1; Assert.False(ShipPartBatching.UniformPositive(matrix));
        matrix = Matrix4x4.identity; matrix.m01 = 0.5f; Assert.False(ShipPartBatching.UniformPositive(matrix));
        matrix = Matrix4x4.identity; matrix.m00 = float.NaN; Assert.False(ShipPartBatching.UniformPositive(matrix));
        matrix.m00 = float.PositiveInfinity; Assert.False(ShipPartBatching.UniformPositive(matrix));
    }

    [Fact]
    public void InspectedGameShaderAssetFingerprintMatchesInstalledBytes()
    {
        var game = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "GameDirectory").Value!;
        Assert.True(ShipPartBatching.MatchesShaderAsset(Path.Combine(game, "StellarDrive_Data", "sharedassets0.assets")));
    }

    [Fact]
    public void NativeValvePoseChangesInvalidateBatchesButFluidAndSignalChangesDoNot()
    {
        Assert.True(ShipPartBatching.NeedsPoseInvalidation(new StatefulPartStateChangeEvent(null!, new FluidValveState(), new FluidValveState { ValveOpenedRate = 0.5f })));
        Assert.False(ShipPartBatching.NeedsPoseInvalidation(new StatefulPartStateChangeEvent(null!, new FluidValveState(), new FluidValveState { CurrentTransferRate = 1 })));
        Assert.False(ShipPartBatching.NeedsPoseInvalidation(new StatefulPartStateChangeEvent(null!, new FluidTankState(), new FluidTankState { FluidQuantity = 1 })));
        Assert.False(ShipPartBatching.NeedsPoseInvalidation(new StatefulPartStateChangeEvent(null!, new SignalDisplayState(), new SignalDisplayState { SignalValue = 1 })));
    }
}
