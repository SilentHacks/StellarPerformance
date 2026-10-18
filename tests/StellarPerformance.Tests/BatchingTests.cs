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
