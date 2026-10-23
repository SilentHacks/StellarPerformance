using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Core.Values;
using HarmonyLib;
using Ships;
using WorldTracking.Spaces;
using Xunit;

namespace StellarPerformance.Tests;

[Collection("Diagnostic counters")]
public sealed class SpatialClusteringTests
{
    private static Array _nodes = null!;
    private static List<int[]> _captured = null!;
    private static readonly Dictionary<object, int> Indices = new Dictionary<object, int>(ReferenceComparer.Instance);
    private static readonly Type NativeNode = typeof(PlanetSurfaceSpacesServerUpdater).GetNestedType("ObjectNeighbors", BindingFlags.NonPublic)!;
    private static readonly Type NodeList = typeof(List<>).MakeGenericType(NativeNode);
    private static readonly FieldInfo Position = AccessTools.Field(NativeNode, "PlanetPos"), Identity = AccessTools.Field(NativeNode, "MainObject"), Neighbors = AccessTools.Field(NativeNode, "Neighbors");
    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceComparer Instance = new ReferenceComparer();
        public new bool Equals(object? a, object? b) => ReferenceEquals(a, b);
        public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
    }
    private sealed class EqualShip : TrackedShipServer
    {
        private EqualShip() : base(null!, null!) { }
        public override bool Equals(object? other) => other is EqualShip;
        public override int GetHashCode() => 1;
    }
    internal static object LoadNodes() => _nodes;
    internal static void Capture(object groups)
    {
        _captured = new List<int[]>();
        foreach (IList group in (IEnumerable)groups)
        {
            var values = new int[group.Count];
            for (var i = 0; i < values.Length; i++) values[i] = Indices[group[i]!];
            _captured.Add(values);
        }
    }
    private static IEnumerable<CodeInstruction> GraphOnly(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.ToList();
        var call = codes.FindIndex(code => code.operand is MethodInfo method && method.Name == "ToArray" && method.IsGenericMethod && method.GetGenericArguments()[0] == NativeNode);
        var output = codes.FindIndex(call + 2, code => code.operand is MethodInfo method && method.DeclaringType == typeof(List<>).MakeGenericType(NodeList) && method.Name == "GetEnumerator");
        Assert.True(call >= 0 && output > call + 2);
        var store = codes[call + 1];
        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SpatialClusteringTests), nameof(LoadNodes)));
        yield return new CodeInstruction(OpCodes.Castclass, NativeNode.MakeArrayType());
        yield return new CodeInstruction(store.opcode, store.operand);
        foreach (var code in codes.Skip(call + 2).Take(output - call - 3)) yield return code;
        yield return codes[output - 1];
        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SpatialClusteringTests), nameof(Capture)));
        yield return new CodeInstruction(OpCodes.Ret);
    }
    private static Action Oracle(bool rewritten)
    {
        var native = ManagedMethod.Clone(SpatialClustering.Target, (codes, generator) => GraphOnly(rewritten ? SpatialClustering.Rewrite(codes, generator) : codes));
        var invoke = native.GetType().GetMethod("Invoke")!;
        return Expression.Lambda<Action>(Expression.Invoke(Expression.Constant(native), invoke.GetParameters().Select(p => Expression.Default(p.ParameterType)))).Compile();
    }
    private static void Fixture(Vector3Double[] positions, Func<int, object>? identity = null)
    {
        _nodes = Array.CreateInstance(NativeNode, positions.Length); Indices.Clear();
        for (var i = 0; i < positions.Length; i++)
        {
            var node = Activator.CreateInstance(NativeNode, true)!;
            Identity.SetValue(node, identity == null ? RuntimeHelpers.GetUninitializedObject(typeof(TrackedShipServer)) : identity(i));
            Position.SetValue(node, positions[i]); Neighbors.SetValue(node, Activator.CreateInstance(NodeList));
            _nodes.SetValue(node, i); Indices.Add(node, i);
        }
    }
    private static List<int[]> Execute(Action oracle)
    {
        foreach (var node in _nodes) ((IList)Neighbors.GetValue(node)!).Clear();
        oracle(); return _captured;
    }
    private static void Same(List<int[]> expected, List<int[]> actual, Vector3Double[] points)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], actual[i]);
            Vector3Double Center(int[] indices)
            {
                var sum = Vector3Double.Zero;
                foreach (var index in indices) sum += points[index]; return sum / indices.Length;
            }
            var a = Center(expected[i]); var b = Center(actual[i]);
            Assert.Equal(BitConverter.DoubleToInt64Bits(a.x), BitConverter.DoubleToInt64Bits(b.x));
            Assert.Equal(BitConverter.DoubleToInt64Bits(a.y), BitConverter.DoubleToInt64Bits(b.y));
            Assert.Equal(BitConverter.DoubleToInt64Bits(a.z), BitConverter.DoubleToInt64Bits(b.z));
        }
    }
    private static Vector3Double[] Cloud(int count, int seed, double range)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, count).Select(_ => new Vector3Double((random.NextDouble() - .5) * range, (random.NextDouble() - .5) * range, (random.NextDouble() - .5) * range)).ToArray();
    }

    [Fact]
    public void ExactNativeBfsOrderAndCenterBitsSurviveRandomGraphsAndScrambledChains()
    {
        SpatialClustering.Initialize(); var native = Oracle(false);
        for (var seed = 0; seed < 120; seed++)
        {
            var points = Cloud(seed + 1, seed, seed % 3 == 0 ? 500 : seed % 3 == 1 ? 3000 : 50000);
            Fixture(points); var expected = Execute(native);
            Assert.True(SpatialClustering.TryCluster(points, out var candidate)); Same(expected, candidate!, points);
            var random = new Random(seed + 1000);
            points = Enumerable.Range(0, 64).Select(i => new Vector3Double(i * 700d, seed % 2 * .000001, 0)).OrderBy(_ => random.Next()).ToArray();
            Fixture(points); expected = Execute(native);
            Assert.True(SpatialClustering.TryCluster(points, out candidate)); Same(expected, candidate!, points);
        }
    }

    [Fact]
    public void ActualShippingBridgeMatchesEmittedNativeGraphForDenseSparseAndAdversarialLayouts()
    {
        SpatialClustering.Initialize(); var native = Oracle(false); var candidate = Oracle(true);
        foreach (var count in new[] { SpatialClustering.MinimumNodes, 512, 1024 })
        {
            var fixtures = new[] {
                Cloud(count, 1, 500), Cloud(count, 2, 150000),
                Enumerable.Range(0, count).Select(i => new Vector3Double(i * 800d, 0, 0)).ToArray(),
                Enumerable.Range(0, count).Select(i => new Vector3Double(i % 2 * 1023d, i / 2 % 2 * 1023d, i / 4 % 2 * 1023d)).ToArray() };
            foreach (var points in fixtures)
            {
                Fixture(points); Assert.NotNull(SpatialClustering.TryBuildGraph(_nodes));
                Same(Execute(native), Execute(candidate), points);
            }
        }
    }

    [Fact]
    public void UnknownEqualityAndDuplicateReferencesRetainTheEntireNativeGraph()
    {
        SpatialClustering.Initialize(); var native = Oracle(false); var candidate = Oracle(true);
        var points = Cloud(1024, 390, 150000);
        Fixture(points, _ => RuntimeHelpers.GetUninitializedObject(typeof(EqualShip)));
        Assert.Null(SpatialClustering.TryBuildGraph(_nodes)); Same(Execute(native), Execute(candidate), points);
        var shared = RuntimeHelpers.GetUninitializedObject(typeof(TrackedShipServer));
        Fixture(points, _ => shared); Assert.Null(SpatialClustering.TryBuildGraph(_nodes)); Same(Execute(native), Execute(candidate), points);
    }

    [Fact]
    public void NativeThresholdRoundingAndFiniteLargeCoordinatesPreserveEveryOrderedComponent()
    {
        SpatialClustering.Initialize(); var native = Oracle(false); var values = new List<Vector3Double>();
        var lo = Math.BitDecrement(1000d); var hi = Math.BitIncrement(1000d);
        foreach (var start in new[] { -1099511625700d, -1048576d, -1024d, -0d, 0d, 1024d, 1048576d, 1099511625700d })
            foreach (var offset in new[] { -1024d, -1000d, -lo, -hi, -1e-12, 0d, 1e-12, lo, 1000d, hi, 1024d })
                values.Add(new Vector3Double(start + offset, start / 2, start / 4));
        foreach (var x in new[] { Math.BitDecrement(600d), 600d, Math.BitIncrement(600d) })
            foreach (var y in new[] { Math.BitDecrement(800d), 800d, Math.BitIncrement(800d) }) values.Add(new Vector3Double(x, y, 0));
        var points = values.ToArray(); Fixture(points); var expected = Execute(native);
        Assert.True(SpatialClustering.TryCluster(points, out var candidate)); Same(expected, candidate!, points);
        points = Enumerable.Range(0, 1024).Select(i => new Vector3Double(1099511627776d - i % 4 * 1000d, 1099511627776d - i / 4 % 4 * 1000d, 1099511627776d - i / 16 * 1000d)).ToArray();
        Fixture(points); Same(Execute(native), Execute(Oracle(true)), points);
    }

    [Fact]
    public void SmallNonfiniteAndOversizeInputsTakeNativeFallbackWithinScratchBounds()
    {
        SpatialClustering.Initialize();
        Fixture(Cloud(37, 1, 1000)); Assert.Null(SpatialClustering.TryBuildGraph(_nodes));
        Fixture(Cloud(127, 2, 1000)); Assert.Null(SpatialClustering.TryBuildGraph(_nodes));
        Fixture(Cloud(128, 3, 1000)); Assert.NotNull(SpatialClustering.TryBuildGraph(_nodes));
        var oversize = Array.CreateInstance(NativeNode, SpatialClustering.NodeLimit + 1); Assert.Null(SpatialClustering.TryBuildGraph(oversize));
        Assert.True(SpatialClustering.ScratchBytes(SpatialClustering.NodeLimit) <= SpatialClustering.ByteLimit);
        Assert.False(SpatialClustering.TryCluster(new Vector3Double[SpatialClustering.NodeLimit + 1], out _));
        foreach (var invalid in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, double.MaxValue, -double.MaxValue })
        {
            var points = Cloud(1024, 90, 150000); points[17] = new Vector3Double(invalid, -0d, 0d);
            Fixture(points); Assert.Null(SpatialClustering.TryBuildGraph(_nodes)); Assert.False(SpatialClustering.TryCluster(points, out _));
        }
        Assert.True(SpatialClustering.TryCluster(Array.Empty<Vector3Double>(), out var empty)); Assert.Empty(empty!);
    }
}
