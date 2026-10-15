using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ships.Interface.Model;
using Ships.Interface.Model.Gases;
using Ships.Interface.Model.State;
using Ships.Interface.Values;
using Ships.Rooms;
using Ships.Rooms.Model;
using UnityEngine;
using WorldProvision.Interface.Services;
using Xunit;

namespace StellarPerformance.Tests;

[Collection("Diagnostic counters")]
public sealed class IncrementalRoomTests
{
    public class Provider : DispatchProxy
    {
        public float Atmosphere;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "GetAtmosphereDensityAtPosition") return Atmosphere;
            return method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }

    private sealed class World
    {
        internal readonly ShipRoomsVoxelsTree Tree = new();
        internal readonly ShipRoomsGases Gases = new();
        internal readonly ShipRoomsVoxelUpdater Updater;
        internal readonly bool Optimized;
        internal readonly float Atmosphere;
        internal World(bool optimized, float atmosphere = 1)
        {
            Optimized = optimized; Atmosphere = atmosphere;
            var env = DispatchProxy.Create<IWorldEnvironmentProvider, Provider>();
            ((Provider)(object)env).Atmosphere = atmosphere;
            Updater = new(Tree, Gases, env, DispatchProxy.Create<IShipStateRead, Provider>());
        }
        internal void Edit(IEnumerable<Vector3Int>? add = null, IEnumerable<Vector3Int>? remove = null)
        {
            foreach (var coord in remove ?? Array.Empty<Vector3Int>()) Updater.RemoveHullVoxel(new[] { coord }, Seeds(coord));
            foreach (var coord in add ?? Array.Empty<Vector3Int>()) Updater.AddHullVoxel(new[] { coord }, Seeds(coord));
            if (!Optimized) { Updater.UpdateRooms(); return; }
            var previous = IncrementalRooms.Enter(Updater, Atmosphere);
            try { NativeRunner.Update(Updater); }
            finally { IncrementalRooms.AfterUpdate(null, previous); }
        }
        internal void Density(Vector3Int point, float density)
        {
            ushort id = Tree.GetLeaf(point).RoomId;
            Assert.True(Gases.TryGetRoomGasState(id, out var state));
            state.GasState = new GasState { GasItemId = state.GasState.GasItemId, GasDensity = density };
            Gases.SetRoomGasContent(id, state);
        }
    }

    // Execute emitted copies of the actual inspected methods, with our exact transpilers.
    // Redirect recursive calls to those copies, without installing Harmony/MonoMod detours.
    private static class NativeRunner
    {
        private static readonly Action<ShipRoomsVoxelsTreeNode, Vector3Int> AddNode = CloneNode(IncrementalRooms.AddNodeMethod);
        private static readonly Action<ShipRoomsVoxelsTreeNode, Vector3Int> RemoveNode = CloneNode(IncrementalRooms.RemoveNodeMethod);
        private static readonly Action<ShipRoomsVoxelsTree, Vector3Int> AddTree =
            (Action<ShipRoomsVoxelsTree, Vector3Int>)ManagedMethod.Clone(IncrementalRooms.AddTreeMethod, (codes, _) => Map(IncrementalRooms.Transpile(codes, IncrementalRooms.AddTreeMethod)));
        private static readonly Action<ShipRoomsVoxelsTree, IEnumerable<Vector3Int>> AddMany =
            (Action<ShipRoomsVoxelsTree, IEnumerable<Vector3Int>>)ManagedMethod.Clone(IncrementalRooms.AddWallsMethod, (codes, _) => Map(codes));
        private static readonly Action<ShipRoomsVoxelsTree, IEnumerable<Vector3Int>> RemoveMany =
            (Action<ShipRoomsVoxelsTree, IEnumerable<Vector3Int>>)ManagedMethod.Clone(IncrementalRooms.RemoveWallsMethod, (codes, _) => Map(codes));
        internal static readonly Action<ShipRoomsVoxelUpdater> Update =
            (Action<ShipRoomsVoxelUpdater>)ManagedMethod.Clone(IncrementalRooms.UpdateMethod, (codes, _) => Map(IncrementalRooms.Transpile(codes, IncrementalRooms.UpdateMethod)));

        private static Action<ShipRoomsVoxelsTreeNode, Vector3Int> CloneNode(MethodInfo method) =>
            (Action<ShipRoomsVoxelsTreeNode, Vector3Int>)ManagedMethod.Clone(method, (codes, _) => Map(IncrementalRooms.Transpile(codes, method)));
        private static IEnumerable<CodeInstruction> Map(IEnumerable<CodeInstruction> codes)
        {
            foreach (var code in codes)
            {
                string? wrapper = null;
                if (code.Calls(IncrementalRooms.AddNodeMethod)) wrapper = nameof(CallAddNode);
                else if (code.Calls(IncrementalRooms.RemoveNodeMethod)) wrapper = nameof(CallRemoveNode);
                else if (code.Calls(IncrementalRooms.AddTreeMethod)) wrapper = nameof(CallAddTree);
                else if (code.Calls(AccessTools.Method(typeof(IncrementalRooms), nameof(IncrementalRooms.AddWalls)))) wrapper = nameof(CallAddMany);
                else if (code.Calls(AccessTools.Method(typeof(IncrementalRooms), nameof(IncrementalRooms.RemoveWalls)))) wrapper = nameof(CallRemoveMany);
                if (wrapper != null) { code.opcode = OpCodes.Call; code.operand = AccessTools.Method(typeof(NativeRunner), wrapper); }
                yield return code;
            }
        }
        private static void CallAddNode(ShipRoomsVoxelsTreeNode node, Vector3Int coord) => AddNode(node, coord);
        private static void CallRemoveNode(ShipRoomsVoxelsTreeNode node, Vector3Int coord) => RemoveNode(node, coord);
        private static void CallAddTree(ShipRoomsVoxelsTree tree, Vector3Int coord) => AddTree(tree, coord);
        private static void CallAddMany(ShipRoomsVoxelsTree tree, IEnumerable<Vector3Int> coords)
        {
            var previous = IncrementalRooms.EnterMutation(tree);
            try { AddMany(tree, coords); } finally { IncrementalRooms.ExitMutation(previous); }
        }
        private static void CallRemoveMany(ShipRoomsVoxelsTree tree, IEnumerable<Vector3Int> coords)
        {
            var previous = IncrementalRooms.EnterMutation(tree);
            try { RemoveMany(tree, coords); } finally { IncrementalRooms.ExitMutation(previous); }
        }
        internal static void RawAdd(ShipRoomsVoxelsTree tree, Vector3Int coord) => AddMany(tree, new[] { coord });
    }

    private static Vector3Int[] Seeds(Vector3Int c) => new[] { c, c + Vector3Int.left, c + Vector3Int.right, c + Vector3Int.up, c + Vector3Int.down, c + new Vector3Int(0, 0, -1), c + new Vector3Int(0, 0, 1) };
    private static Vector3Int[] Box(int size) => (from x in Enumerable.Range(0, size + 1)
                                                  from y in Enumerable.Range(0, size + 1)
                                                  from z in Enumerable.Range(0, size + 1)
                                                  where x == 0 || y == 0 || z == 0 || x == size || y == size || z == size
                                                  select new Vector3Int(x, y, z)).ToArray();
    private static (World native, World mod) Pair(int size = 8, float atmosphere = 1)
    {
        var native = new World(false, atmosphere); var mod = new World(true, atmosphere);
        native.Edit(Box(size)); mod.Edit(Box(size)); Equivalent(native, mod);
        native.Density(new Vector3Int(1, 1, 1), .37f); mod.Density(new Vector3Int(1, 1, 1), .37f);
        return (native, mod);
    }

    private static void Equivalent(World native, World mod, float densityTolerance = .00002f)
    {
        var leaves = native.Tree.GetLeaves();
        Assert.Equal(leaves.Length, mod.Tree.GetLeaves().Length);
        var ids = new Dictionary<ushort, ushort>();
        var volumes = new Dictionary<ushort, double>();
        foreach (var a in leaves)
        {
            var b = mod.Tree.GetLeaf(a.Coord);
            Assert.Equal(a.Bounds, b.Bounds); Assert.Equal(a.WallCount, b.WallCount);
            if (a.WallCount != 0) continue;
            Assert.Equal(a.RoomId == 0, b.RoomId == 0);
            if (a.RoomId == 0) continue;
            if (ids.TryGetValue(a.RoomId, out var expected)) Assert.Equal(expected, b.RoomId);
            else { Assert.DoesNotContain(b.RoomId, ids.Values); ids.Add(a.RoomId, b.RoomId); }
            volumes.TryGetValue(b.RoomId, out var v); volumes[b.RoomId] = v + (double)b.Bounds * b.Bounds * b.Bounds;
        }
        Assert.Equal(native.Gases.RoomGases.Count, mod.Gases.RoomGases.Count);
        Assert.Equal(volumes.Count, mod.Gases.RoomGases.Count);
        foreach (var id in ids)
        {
            Assert.True(native.Gases.TryGetRoomGasState(id.Key, out var a));
            Assert.True(mod.Gases.TryGetRoomGasState(id.Value, out var b));
            Assert.Equal(a.RoomVolume, b.RoomVolume);
            Assert.InRange(Math.Abs(a.GasState.GasDensity - b.GasState.GasDensity), 0, densityTolerance);
            var seed = mod.Tree.GetLeaf(b.RoomScanSeedCoord);
            Assert.Equal((byte)0, seed.WallCount); Assert.Equal(id.Value, seed.RoomId);
            Assert.Equal(volumes[id.Value] * ShipRoomsConstants.GasVolumeCapacityPerVoxel, b.RoomVolume);
        }
    }

    [Fact]
    public void NativePatchContractsAreNarrowAndFailClosed()
    {
        foreach (var target in new[] { IncrementalRooms.UpdateMethod, IncrementalRooms.AddNodeMethod, IncrementalRooms.RemoveNodeMethod, IncrementalRooms.AddTreeMethod })
        {
            var original = ManagedMethod.Instructions(target);
            Assert.NotEmpty(IncrementalRooms.Transpile(original, target));
            Assert.Throws<InvalidOperationException>(() => IncrementalRooms.Transpile(new[] { new CodeInstruction(OpCodes.Ret) }, target).ToArray());
        }
        var split = ManagedMethod.Instructions(AccessTools.Method(typeof(ShipRoomsVoxelsTreeNode), "CreateChildLeaves"));
        Assert.Equal(8, split.Count(code => code.Calls(AccessTools.PropertySetter(typeof(ShipRoomsVoxelsTreeNode), "TmpGasContent"))));
    }

    [Fact]
    public void InteriorEditsRepairSavedSeedsAndNativeReloadRestoresGas()
    {
        var (native, mod) = Pair();
        var seed = mod.Gases.RoomGases.Single().RoomScanSeedCoord;
        native.Edit(new[] { seed }); mod.Edit(new[] { seed }); Equivalent(native, mod);
        var saved = mod.Gases.RoomGases.ToArray();
        var occupied = mod.Tree.GetLeaves().Where(n => n.WallCount != 0).SelectMany(n => Enumerable.Repeat(n.Coord, n.WallCount)).ToArray();
        var reloaded = new World(false, 0); reloaded.Edit(occupied);
        foreach (var state in saved)
        {
            ushort id = reloaded.Tree.GetLeaf(state.RoomScanSeedCoord).RoomId;
            Assert.NotEqual((ushort)0, id);
            reloaded.Gases.LoadRoom(id, state);
            Assert.True(reloaded.Gases.TryGetRoomGasState(id, out var result));
            Assert.Equal(state.GasState.GasDensity, result.GasState.GasDensity);
        }
    }

    [Fact]
    public void InteriorRemovalAndOverlappingWallsKeepNativeGasAndVolumes()
    {
        var (native, mod) = Pair(); var c = new Vector3Int(3, 3, 3);
        foreach (var batch in new[] { new[] { c }, new[] { c }, Array.Empty<Vector3Int>(), Array.Empty<Vector3Int>() })
        {
            if (batch.Length > 0) { native.Edit(batch); mod.Edit(batch); }
            else { native.Edit(remove: new[] { c }); mod.Edit(remove: new[] { c }); }
            Equivalent(native, mod);
        }
    }

    [Fact]
    public void PartitionsAndUnequalDensityRoomJoinsMatchNative()
    {
        var (native, mod) = Pair();
        var plane = (from y in Enumerable.Range(1, 7) from z in Enumerable.Range(1, 7) select new Vector3Int(4, y, z)).ToArray();
        native.Edit(plane); mod.Edit(plane); Equivalent(native, mod);
        Assert.Equal(2, mod.Gases.RoomGases.Count);
        foreach (var world in new[] { native, mod }) { world.Density(new Vector3Int(2, 2, 2), .2f); world.Density(new Vector3Int(6, 2, 2), .8f); }
        native.Edit(remove: plane); mod.Edit(remove: plane); Equivalent(native, mod);
        Assert.Single(mod.Gases.RoomGases);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void HullBreachResealAndOctreeExpansionMatchNative(float atmosphere)
    {
        var (native, mod) = Pair(atmosphere: atmosphere);
        var door = new Vector3Int(0, 3, 3);
        native.Edit(remove: new[] { door }); mod.Edit(remove: new[] { door }); Equivalent(native, mod);
        Assert.Empty(mod.Gases.RoomGases);
        native.Edit(new[] { door }); mod.Edit(new[] { door }); Equivalent(native, mod);
        var exterior = new[] { new Vector3Int(20, 2, 2), new Vector3Int(-12, -3, -1) };
        native.Edit(exterior); mod.Edit(exterior); Equivalent(native, mod);
        native.Edit(remove: exterior); mod.Edit(remove: exterior); Equivalent(native, mod);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(19)]
    [InlineData(794)]
    public void RandomMixedEditSequencesMatchNative(int seed)
    {
        var (native, mod) = Pair(6);
        var occupied = Box(6).ToHashSet(); var random = new System.Random(seed);
        for (int step = 0; step < 100; step++)
        {
            var add = new List<Vector3Int>(); var remove = new List<Vector3Int>(); var chosen = new HashSet<Vector3Int>();
            for (int i = 0, count = random.Next(1, 5); i < count; i++)
            {
                var c = new Vector3Int(random.Next(-1, 8), random.Next(-1, 8), random.Next(-1, 8));
                if (!chosen.Add(c)) continue;
                if (occupied.Remove(c)) remove.Add(c); else { occupied.Add(c); add.Add(c); }
            }
            native.Edit(add, remove); mod.Edit(add, remove);
            try { Equivalent(native, mod); }
            catch (Exception error) { throw new Exception($"Seed {seed}, step {step}", error); }
        }
    }

    [Theory]
    [InlineData(12)]
    [InlineData(83)]
    [InlineData(901)]
    public void RepeatedInteriorPartitionsKeepGasAndEverySavedSeedValid(int seed)
    {
        var (native, mod) = Pair(6);
        var occupied = new HashSet<Vector3Int>(); var random = new System.Random(seed);
        for (int step = 0; step < 180; step++)
        {
            var c = new Vector3Int(random.Next(1, 6), random.Next(1, 6), random.Next(1, 6));
            if (occupied.Remove(c)) { native.Edit(remove: new[] { c }); mod.Edit(remove: new[] { c }); }
            else { occupied.Add(c); native.Edit(new[] { c }); mod.Edit(new[] { c }); }
            try { Equivalent(native, mod); }
            catch (Exception error) { throw new Exception($"Interior seed {seed}, step {step}", error); }
        }
    }

    [Fact]
    public void FillingLastAirVoxelRemovesRoomAndFreeingItCreatesNativeVacuum()
    {
        var (native, mod) = Pair(2); var c = new Vector3Int(1, 1, 1);
        native.Edit(new[] { c }); mod.Edit(new[] { c }); Equivalent(native, mod);
        Assert.Empty(mod.Gases.RoomGases);
        native.Edit(remove: new[] { c }); mod.Edit(remove: new[] { c }); Equivalent(native, mod);
        Assert.Equal(0f, mod.Gases.RoomGases.Single().GasState.GasDensity);
    }

    [Fact]
    public void LargeRoomLocalAccountingUsesNativeVacuumRuleWithoutRepeatedFloatFoldDrift()
    {
        var (native, mod) = Pair(40); var c = new[] { new Vector3Int(20, 20, 20) };
        const double voxels = 39 * 39 * 39;
        for (int i = 0; i < 24; i++)
        {
            native.Edit(c); mod.Edit(c);
            native.Edit(remove: c); mod.Edit(remove: c);
        }
        // Native folds thousands of float gas contributions on each rebuild. The local
        // path computes the same weighted-gas rule with double aggregates. Quantify its
        // rounding difference instead of silently claiming bit-identical oxygen values.
        Equivalent(native, mod, .001f);
        double expected = .37f * Math.Pow((voxels - 1) / voxels, 24);
        Assert.InRange(Math.Abs(mod.Gases.RoomGases.Single().GasState.GasDensity - expected), 0, .000001);
    }

    [Fact]
    public void VolumesBeyondExactFloatVoxelCountsUseGeneralReconstruction()
    {
        var previous = DiagnosticCounters.Active;
        DiagnosticCounters.Active = true;
        try
        {
            var (native, mod) = Pair();
            foreach (var world in new[] { native, mod })
            {
                ushort id = world.Tree.GetLeaf(new Vector3Int(1, 1, 1)).RoomId;
                Assert.True(world.Gases.TryGetRoomGasState(id, out var state));
                state.RoomVolume = 33554432f * ShipRoomsConstants.GasVolumeCapacityPerVoxel;
                world.Gases.SetRoomGasContent(id, state);
            }
            long local = IncrementalRooms.LocalEdits;
            var edit = new[] { new Vector3Int(3, 3, 3) };
            native.Edit(edit); mod.Edit(edit); Equivalent(native, mod);
            Assert.Equal(local, IncrementalRooms.LocalEdits);
        }
        finally { DiagnosticCounters.Active = previous; }
    }

    [Fact]
    public void NestedUpdatesAndUnrelatedTreeCallbacksKeepScopesIsolated()
    {
        var (native, mod) = Pair(); var inner = Pair(); var unrelated = new ShipRoomsVoxelsTree();
        bool fired = false;
        mod.Tree.OnUpdate = () =>
        {
            if (fired) return; fired = true;
            var c = new Vector3Int(2, 2, 2);
            inner.native.Edit(new[] { c }); inner.mod.Edit(new[] { c });
            NativeRunner.RawAdd(unrelated, new Vector3Int(2, 2, 2));
        };
        var edit = new[] { new Vector3Int(3, 3, 3) };
        native.Edit(edit); mod.Edit(edit); Equivalent(native, mod); Equivalent(inner.native, inner.mod);
        Assert.True(fired);
        Assert.All(unrelated.GetLeaves(), node => Assert.Equal((ushort)0, node.RoomId));
        mod.Tree.OnUpdate = () => throw new InvalidOperationException("test callback");
        Assert.Throws<InvalidOperationException>(() => mod.Edit(new[] { new Vector3Int(3, 3, 4) }));
        var clean = Pair(); clean.native.Edit(edit); clean.mod.Edit(edit); Equivalent(clean.native, clean.mod);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalAndExteriorEditsAvoidWholeRegionSearches(bool capture)
    {
        var previous = DiagnosticCounters.Active;
        DiagnosticCounters.Active = capture;
        try
        {
            var (native, mod) = Pair(20);
            long local = IncrementalRooms.LocalEdits, visits = IncrementalRooms.VisitedNodes;
            var interior = new[] { new Vector3Int(10, 10, 10) };
            native.Edit(interior); mod.Edit(interior); Equivalent(native, mod);
            Assert.Equal(local + (capture ? 1 : 0), IncrementalRooms.LocalEdits);
            Assert.InRange(IncrementalRooms.VisitedNodes - visits, 0, 100);
            if (!capture) Assert.Equal(visits, IncrementalRooms.VisitedNodes);
            visits = IncrementalRooms.VisitedNodes;
            var exterior = new[] { new Vector3Int(21, 10, 10) };
            native.Edit(exterior); mod.Edit(exterior); Equivalent(native, mod);
            Assert.InRange(IncrementalRooms.VisitedNodes - visits, capture ? 1 : 0, capture ? 500 : 0);
        }
        finally { DiagnosticCounters.Active = previous; }
    }
}
