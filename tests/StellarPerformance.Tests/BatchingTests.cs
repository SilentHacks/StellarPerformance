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

public sealed class BatchingTests
{
    [Fact]
    public void RecoveryWaitsForEveryNativeHoverAndHighlightToEnd()
    {
        var holds = new BatchInteractionHolds();
        Assert.True(holds.Enter(7, "body"));
        Assert.True(holds.Enter(7, "socket"));
        Assert.True(holds.Highlight(7));
        Assert.True(holds.Highlight(7)); // Native highlight is idempotent.
        holds.Leave(7, "body"); holds.Unhighlight(7);
        Assert.True(holds.Held(7));
        holds.Leave(7, "unrelated"); Assert.True(holds.Held(7));
        holds.Leave(7, "socket"); Assert.False(holds.Held(7));
        holds.Leave(7, "socket"); holds.Unhighlight(7);
        Assert.False(holds.Held(7));
    }

    [Fact]
    public void NestedHoverRemovalAndTrackingOverflowFailClosed()
    {
        var holds = new BatchInteractionHolds();
        Assert.True(holds.Enter(1, "same")); Assert.True(holds.Enter(1, "same"));
        holds.Leave(1, "same"); Assert.True(holds.Held(1));
        holds.Remove(1); Assert.False(holds.Held(1));
        for (ushort id = 0; id < BatchInteractionHolds.Limit; id++) Assert.True(holds.Highlight(id));
        Assert.False(holds.Highlight(500));
        Assert.True(holds.Highlight(1)); // Existing idempotent state needs no storage.
        holds.Unhighlight(1); Assert.True(holds.Highlight(500));
        holds.Clear(); Assert.False(holds.Held(500));
    }

    [Fact]
    public void RecoveryDeadlinesHandleFrameCounterWrap()
    {
        Assert.False(ShipPartBatching.Ready(int.MaxValue - 1, int.MinValue + 3));
        Assert.True(ShipPartBatching.Ready(int.MinValue + 3, int.MinValue + 3));
        Assert.True(ShipPartBatching.Ready(int.MinValue + 4, int.MinValue + 3));
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
