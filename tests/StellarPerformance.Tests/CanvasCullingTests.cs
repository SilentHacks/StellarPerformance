using System;
using UnityEngine;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class CanvasCullingTests
{
    private static Plane[] Box() => new[]
    {
        new Plane(Vector3.right, 1), new Plane(Vector3.left, 1),
        new Plane(Vector3.up, 1), new Plane(Vector3.down, 1),
        new Plane(Vector3.forward, 1), new Plane(Vector3.back, 1)
    };
    [Fact]
    public void IntersectionsRetainEdgesAndLargeBoundsEnclosingTheView()
    {
        var planes = Box();
        Assert.True(CanvasCulling.Intersects(planes, new Bounds(Vector3.zero, Vector3.one)));
        Assert.True(CanvasCulling.Intersects(planes, new Bounds(Vector3.zero, Vector3.one * 100)));
        Assert.True(CanvasCulling.Intersects(planes, new Bounds(new Vector3(2, 0, 0), new Vector3(2, 0, 0))));
        Assert.False(CanvasCulling.Intersects(planes, new Bounds(new Vector3(2.01f, 0, 0), new Vector3(2, 0, 0))));
    }
    [Fact]
    public void AllSixSidesMatchAnIndependentCornerOracle()
    {
        var random = new System.Random(139);
        var planes = Box();
        for (var trial = 0; trial < 10000; trial++)
        {
            var center = new Vector3((float)random.NextDouble() * 12 - 6, (float)random.NextDouble() * 12 - 6, (float)random.NextDouble() * 12 - 6);
            var ext = new Vector3((float)random.NextDouble() * 3, (float)random.NextDouble() * 3, (float)random.NextDouble() * 3);
            var expected = true;
            foreach (var plane in planes)
            {
                var anyInside = false;
                for (var x = -1; x <= 1; x += 2) for (var y = -1; y <= 1; y += 2) for (var z = -1; z <= 1; z += 2)
                {
                    var corner = center + new Vector3(ext.x * x, ext.y * y, ext.z * z);
                    if (Vector3.Dot(plane.normal, corner) + plane.distance >= 0) anyInside = true;
                }
                if (!anyInside) expected = false;
            }
            Assert.Equal(expected, CanvasCulling.Intersects(planes, new Bounds(center, ext * 2)));
        }
    }
    [Fact]
    public void InvalidViewsAndBoundsFailOpen()
    {
        var outside = new Bounds(Vector3.one * 100, Vector3.one);
        Assert.True(CanvasCulling.Intersects(Array.Empty<Plane>(), outside));
        var planes = Box(); planes[5] = new Plane(Vector3.right, float.NaN);
        Assert.True(CanvasCulling.Intersects(planes, outside));
        Assert.True(CanvasCulling.Intersects(Box(), new Bounds(new Vector3(float.PositiveInfinity, 0, 0), Vector3.one)));
    }
    [Fact]
    public void TransformedEnvelopesMatchAllEightCornersIncludingShearAndNegativeScale()
    {
        var random = new System.Random(248);
        for (var trial = 0; trial < 1000; trial++)
        {
            var matrix = Matrix4x4.identity;
            for (var r = 0; r < 3; r++) for (var c = 0; c < 4; c++) matrix[r, c] = (float)(random.NextDouble() * 20 - 10);
            var local = new Bounds(new Vector3(2, -3, 7), new Vector3(3, 8, 2));
            var result = CanvasCulling.TransformBounds(local, matrix);
            var first = true; var oracle = default(Bounds);
            for (var x = -1; x <= 1; x += 2) for (var y = -1; y <= 1; y += 2) for (var z = -1; z <= 1; z += 2)
            {
                var p = matrix.MultiplyPoint3x4(local.center + new Vector3(local.extents.x * x, local.extents.y * y, local.extents.z * z));
                if (first) { oracle = new Bounds(p, Vector3.zero); first = false; } else oracle.Encapsulate(p);
            }
            for (var axis = 0; axis < 3; axis++)
            {
                Assert.True(Math.Abs(result.min[axis] - oracle.min[axis]) < 0.0001f);
                Assert.True(Math.Abs(result.max[axis] - oracle.max[axis]) < 0.0001f);
            }
        }
    }
    [Fact]
    public void InfiniteFarProjectionRetainsTheFiveFinitePlanes()
    {
        var planes = Box(); planes[5] = new Plane(Vector3.zero, float.PositiveInfinity);
        Assert.False(CanvasCulling.Intersects(planes, new Bounds(new Vector3(100, 0, 0), Vector3.one)));
        Assert.True(CanvasCulling.Intersects(planes, new Bounds(new Vector3(0, 0, 100), Vector3.one)));
    }
    [Fact]
    public void UninspectedVisualsAndLayoutScriptsRemainNative()
    {
        Assert.True(CanvasCulling.FixedPart(typeof(Ships.Parts.Constant.ConstantVisuals)));
        Assert.False(CanvasCulling.FixedPart(typeof(Ships.Parts.TargetingComputer.TargetingComputerVisuals)));
        Assert.False(CanvasCulling.FixedCanvasScript(typeof(UnityEngine.UI.ContentSizeFitter)));
        Assert.False(CanvasCulling.FixedCanvasScript(typeof(UnityEngine.UI.ScrollRect)));
        Assert.False(CanvasCulling.FixedCanvasScript(typeof(CanvasCullingTests)));
    }
}
