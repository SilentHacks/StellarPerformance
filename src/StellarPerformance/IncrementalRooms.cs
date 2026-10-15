using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Items.Model;
using Ships.Interface.Model;
using Ships.Interface.Model.Gases;
using Ships.Interface.Model.State;
using Ships.Interface.Values;
using Ships.Rooms;
using Ships.Rooms.Model;
using UnityEngine;
using WorldProvision.Interface.Services;

namespace StellarPerformance;

// Independent implementation against the inspected native room updater. Keep native wall
// mutations, temporary-gas interpolation and cleanup; replace only region searches.
internal static class IncrementalRooms
{
    static IncrementalRooms() { }

    internal const string Owner = "stellarperformance.incremental-rooms";
    private const int LocalSeedLimit = 256;
    private const int LocalVisitLimit = 65536;
    internal static long Edits, LocalEdits, GeneralEdits, VisitedNodes, OutsideExits;

    internal sealed class RefComparer : IEqualityComparer<ShipRoomsVoxelsTreeNode>
    {
        internal static readonly RefComparer Instance = new RefComparer();
        public bool Equals(ShipRoomsVoxelsTreeNode? a, ShipRoomsVoxelsTreeNode? b) => ReferenceEquals(a, b);
        public int GetHashCode(ShipRoomsVoxelsTreeNode node) => RuntimeHelpers.GetHashCode(node);
    }

    private static MethodInfo Method(Type type, string name) => AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name);
    internal static readonly MethodInfo UpdateMethod = Method(typeof(ShipRoomsVoxelUpdater), "UpdateRooms");
    internal static readonly MethodInfo AddNodeMethod = Method(typeof(ShipRoomsVoxelsTreeNode), "AddWall");
    internal static readonly MethodInfo RemoveNodeMethod = Method(typeof(ShipRoomsVoxelsTreeNode), "RemoveWall");
    internal static readonly MethodInfo AddTreeMethod = Method(typeof(ShipRoomsVoxelsTree), "AddWall");
    private static readonly MethodInfo ClearMethod = Method(typeof(ShipRoomsVoxelUpdater), "ClearRoomsVoxels");
    private static readonly MethodInfo CreateMethod = Method(typeof(ShipRoomsVoxelUpdater), "CreateRooms");
    private static readonly MethodInfo SplitMethod = Method(typeof(ShipRoomsVoxelsTreeNode), "CreateChildLeaves");
    private static readonly MethodInfo MergeMethod = Method(typeof(ShipRoomsVoxelsTreeNode), "RemoveChildLeaves");
    private static readonly MethodInfo ExpandLowMethod = Method(typeof(ShipRoomsVoxelsTree), "ExpandDownLeftBack");
    private static readonly MethodInfo ExpandHighMethod = Method(typeof(ShipRoomsVoxelsTree), "ExpandUpRightForward");
    internal static readonly MethodInfo AddWallsMethod = Method(typeof(ShipRoomsVoxelsTree), "AddWalls");
    internal static readonly MethodInfo RemoveWallsMethod = Method(typeof(ShipRoomsVoxelsTree), "RemoveWalls");

    internal static readonly MethodInfo[] Contracts =
    {
        UpdateMethod, ClearMethod, CreateMethod, Method(typeof(ShipRoomsVoxelUpdater), "Cleanup"),
        AddNodeMethod, RemoveNodeMethod, AddTreeMethod, SplitMethod, MergeMethod,
        ExpandLowMethod, ExpandHighMethod, AddWallsMethod, RemoveWallsMethod,
        Method(typeof(ShipRoomsVoxelsTree), "GetLeaf"), Method(typeof(ShipRoomsVoxelsTreeNode), "GetLeaf"),
        Method(typeof(ShipRoomsVoxelsTree), "RegisterNewRoomId"), Method(typeof(ShipRoomsVoxelsTree), "RemoveRoomId"),
        Method(typeof(ShipRoomsGases), "SetRoomGasContent"), Method(typeof(ShipRoomsGases), "TryGetRoomGasContent"),
        Method(typeof(ShipRoomsGases), "TryGetRoomGasState"), Method(typeof(ShipRoomsGases), "RemoveRoom")
    };

    private static Func<T, F> Field<T, F>(string name)
    {
        var field = AccessTools.Field(typeof(T), name) ?? throw new MissingFieldException(typeof(T).FullName, name);
        if (field.FieldType != typeof(F)) throw new InvalidOperationException("Room field contract changed: " + name);
        var owner = Expression.Parameter(typeof(T));
        return Expression.Lambda<Func<T, F>>(Expression.Field(owner, field), owner).Compile();
    }
    private static Action<T> Call<T>(MethodInfo method)
    {
        var owner = Expression.Parameter(typeof(T));
        return Expression.Lambda<Action<T>>(Expression.Call(owner, method), owner).Compile();
    }
    private static readonly Func<ShipRoomsVoxelUpdater, ShipRoomsVoxelsTree> Tree = Field<ShipRoomsVoxelUpdater, ShipRoomsVoxelsTree>("_tree");
    private static readonly Func<ShipRoomsVoxelUpdater, ShipRoomsGases> Gases = Field<ShipRoomsVoxelUpdater, ShipRoomsGases>("_gases");
    private static readonly Func<ShipRoomsVoxelUpdater, IShipStateRead> Ship = Field<ShipRoomsVoxelUpdater, IShipStateRead>("_ship");
    private static readonly Func<ShipRoomsVoxelUpdater, IWorldEnvironmentProvider> Environment = Field<ShipRoomsVoxelUpdater, IWorldEnvironmentProvider>("_envProvider");
    private static readonly Func<ShipRoomsVoxelUpdater, HashSet<Vector3Int>> ClearSeeds = Field<ShipRoomsVoxelUpdater, HashSet<Vector3Int>>("_clearRoomsVoxelsToScan");
    private static readonly Func<ShipRoomsVoxelUpdater, HashSet<Vector3Int>> CreateSeeds = Field<ShipRoomsVoxelUpdater, HashSet<Vector3Int>>("_createRoomsVoxelsToScan");
    private static readonly Func<ShipRoomsVoxelUpdater, List<Vector3Int>> Added = Field<ShipRoomsVoxelUpdater, List<Vector3Int>>("_addWallsVoxels");
    private static readonly Func<ShipRoomsVoxelUpdater, List<Vector3Int>> Removed = Field<ShipRoomsVoxelUpdater, List<Vector3Int>>("_removeWallsVoxels");
    private static readonly Func<ShipRoomsVoxelUpdater, List<ShipRoomsVoxelsTreeNode>> Cleared = Field<ShipRoomsVoxelUpdater, List<ShipRoomsVoxelsTreeNode>>("_clearedRoomVoxels");
    private static readonly Func<ShipRoomsVoxelUpdater, HashSet<ushort>> Previous = Field<ShipRoomsVoxelUpdater, HashSet<ushort>>("_previousRooms");
    private static readonly Func<ShipRoomsVoxelsTree, ShipRoomsVoxelsTreeNode> Root = Field<ShipRoomsVoxelsTree, ShipRoomsVoxelsTreeNode>("_root");
    private static readonly Action<ShipRoomsVoxelUpdater> NativeClear = Call<ShipRoomsVoxelUpdater>(ClearMethod);
    private static readonly Action<ShipRoomsVoxelUpdater> NativeCreate = Call<ShipRoomsVoxelUpdater>(CreateMethod);
    private static readonly Action<ShipRoomsVoxelsTreeNode> NativeSplit = Call<ShipRoomsVoxelsTreeNode>(SplitMethod);
    private static readonly Action<ShipRoomsVoxelsTreeNode> NativeMerge = Call<ShipRoomsVoxelsTreeNode>(MergeMethod);
    private static readonly Action<ShipRoomsVoxelsTree> NativeExpandLow = Call<ShipRoomsVoxelsTree>(ExpandLowMethod);
    private static readonly Action<ShipRoomsVoxelsTree> NativeExpandHigh = Call<ShipRoomsVoxelsTree>(ExpandHighMethod);

    [ThreadStatic] private static Edit? _current;
    [ThreadStatic] private static Edit? _mutation;

    internal sealed class Edit
    {
        internal readonly ShipRoomsVoxelUpdater Updater;
        internal readonly ShipRoomsVoxelsTree Tree;
        internal readonly ShipRoomsGases Gases;
        internal readonly GasState Atmosphere;
        internal readonly HashSet<ShipRoomsVoxelsTreeNode> Prepared = new HashSet<ShipRoomsVoxelsTreeNode>(RefComparer.Instance);
        internal readonly Dictionary<ShipRoomsVoxelsTreeNode, ushort> Labels = new Dictionary<ShipRoomsVoxelsTreeNode, ushort>(RefComparer.Instance);
        internal ushort LocalRoom;
        internal RoomGasState OldRoom;
        internal int Occupied, Freed;
        internal double DisplacedGas;
        internal bool InvalidLocal;
        internal long Visits;

        internal Edit(ShipRoomsVoxelUpdater updater, float atmosphere)
        {
            Updater = updater; Tree = IncrementalRooms.Tree(updater); Gases = IncrementalRooms.Gases(updater);
            Atmosphere = new GasState { GasItemId = ItemsConstants.OxygenItemId, GasDensity = atmosphere };
        }

        internal GasState Gas(ShipRoomsVoxelsTreeNode node)
        {
            if (Prepared.Add(node))
                node.TmpGasContent = node.WallCount > 0 ? default :
                    node.RoomId != 0 && Gases.TryGetRoomGasContent(node.RoomId, out var gas) ? gas : Atmosphere;
            return node.TmpGasContent;
        }
    }

    internal static bool Compatible() => Contracts.All(method => PatchSafety.HasOnlyOwners(method, new[] { Owner }));

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        if (!Compatible()) throw new InvalidOperationException("Another mod owns room reconstruction; duplicate room rewriting was not installed.");
        harmony.Patch(UpdateMethod, prefix: new HarmonyMethod(typeof(IncrementalRooms), nameof(BeforeUpdate)),
            transpiler: new HarmonyMethod(typeof(IncrementalRooms), nameof(Transpile)), finalizer: new HarmonyMethod(typeof(IncrementalRooms), nameof(AfterUpdate)));
        foreach (var method in new[] { AddNodeMethod, RemoveNodeMethod, AddTreeMethod })
            harmony.Patch(method, transpiler: new HarmonyMethod(typeof(IncrementalRooms), nameof(Transpile)));
    }

    internal static void BeforeUpdate(ShipRoomsVoxelUpdater __instance, out Edit? __state)
    {
        __state = _current;
        _current = null; // Nested/native updates must not borrow an outer ship's edit.
        if (Added(__instance).Count + Removed(__instance).Count + ClearSeeds(__instance).Count + CreateSeeds(__instance).Count == 0 || !Compatible()) return;
        _current = new Edit(__instance, Environment(__instance).GetAtmosphereDensityAtPosition(Ship(__instance).RelativeSpaceId, Vector3.zero));
    }

    // Also used by the offline native-orchestration runner; never installs runtime detours there.
    internal static Edit? Enter(ShipRoomsVoxelUpdater updater, float atmosphere)
    {
        var previous = _current;
        _current = new Edit(updater, atmosphere);
        return previous;
    }

    internal static Exception? AfterUpdate(Exception? __exception, Edit? __state)
    {
        var completed = _current;
        if (completed != null)
        {
            foreach (var node in completed.Prepared) node.TmpGasContent = default;
            if (DiagnosticCounters.Active) VisitedNodes += completed.Visits;
        }
        _current = __state;
        return __exception;
    }

    internal static Edit? EnterMutation(ShipRoomsVoxelsTree tree)
    {
        var previous = _mutation;
        _mutation = _current != null && ReferenceEquals(_current.Tree, tree) ? _current : null;
        return previous;
    }
    internal static void ExitMutation(Edit? previous) => _mutation = previous;

    internal static void AddWalls(ShipRoomsVoxelsTree tree, IEnumerable<Vector3Int> coords)
    {
        var previous = EnterMutation(tree);
        try { tree.AddWalls(coords); } finally { ExitMutation(previous); }
    }
    internal static void RemoveWalls(ShipRoomsVoxelsTree tree, IEnumerable<Vector3Int> coords)
    {
        var previous = EnterMutation(tree);
        try { tree.RemoveWalls(coords); } finally { ExitMutation(previous); }
    }

    internal static void Clear(ShipRoomsVoxelUpdater updater)
    {
        var edit = _current;
        if (edit == null || !ReferenceEquals(edit.Updater, updater)) { NativeClear(updater); return; }
        if (DiagnosticCounters.Active) Edits++;
        if (TryLocalRoom(edit)) return;
        ClearClosedRegions(edit, false);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool TryLocalRoom(Edit edit)
    {
        var updater = edit.Updater;
        if ((Added(updater).Count == 0) == (Removed(updater).Count == 0) || CreateSeeds(updater).Count > LocalSeedLimit) return false;
        ushort room = 0;
        foreach (var coord in ClearSeeds(updater).Concat(CreateSeeds(updater)))
        {
            var node = edit.Tree.GetLeaf(coord);
            if (node.WallCount != 0) continue;
            if (node.RoomId == 0 || (room != 0 && node.RoomId != room)) return false;
            room = node.RoomId;
        }
        if (room == 0 || !edit.Gases.TryGetRoomGasState(room, out var state) ||
            !Finite(state.RoomVolume) || state.RoomVolume <= 0 || !Finite(state.GasState.GasDensity) || state.GasState.GasDensity < 0 ||
            state.GasState.GasItemId != ItemsConstants.OxygenItemId) return false;
        // Aggregate +/- one-voxel accounting requires an exactly representable voxel count.
        // Larger native float volumes use the general search and its native accumulation.
        float voxels = state.RoomVolume / ShipRoomsConstants.GasVolumeCapacityPerVoxel;
        if (voxels > 16777216f || voxels != Math.Floor(voxels)) return false;
        edit.LocalRoom = room; edit.OldRoom = state;
        return true;
    }

    // Clear only enclosed old regions. Outside air already has the unassigned label and
    // gets its native atmosphere lazily, avoiding a walk around the whole exterior.
    private static void ClearClosedRegions(Edit edit, bool afterMutation)
    {
        var pending = new Stack<ShipRoomsVoxelsTreeNode>();
        foreach (var coord in ClearSeeds(edit.Updater).Concat(CreateSeeds(edit.Updater))) pending.Push(edit.Tree.GetLeaf(coord));
        if (afterMutation)
            foreach (var node in edit.Prepared.ToArray())
                foreach (var neighbors in Neighbors(node)) foreach (var neighbor in neighbors) pending.Push(neighbor);
        var seen = new HashSet<ShipRoomsVoxelsTreeNode>(RefComparer.Instance);
        var nodes = Cleared(edit.Updater);
        while (pending.Count != 0)
        {
            var node = pending.Pop();
            if (node.WallCount != 0 || node.RoomId == 0 || !seen.Add(node)) continue;
            if (DiagnosticCounters.Active) edit.Visits++;
            edit.Gas(node);
            Previous(edit.Updater).Add(node.RoomId);
            nodes.Add(node);
            foreach (var neighbors in Neighbors(node)) foreach (var neighbor in neighbors) pending.Push(neighbor);
        }
        foreach (var node in nodes) node.RoomId = 0;
    }

    internal static void PrepareAdd(ShipRoomsVoxelsTreeNode node)
    {
        var edit = MutationFor(node);
        if (edit == null || !node.IsLeaf) return;
        var gas = edit.Gas(node);
        if (edit.LocalRoom != 0 && node.WallCount == 0)
        {
            edit.Labels[node] = node.RoomId;
            if (node.RoomId != edit.LocalRoom) edit.InvalidLocal = true;
        }
        if (node.Bounds == 1 && node.WallCount == 0) { edit.Occupied++; edit.DisplacedGas += gas.GasDensity; }
    }

    internal static void PrepareRemove(ShipRoomsVoxelsTreeNode node)
    {
        var edit = MutationFor(node);
        if (edit == null || !node.IsLeaf) return;
        edit.Gas(node);
        if (node.Bounds == 1 && node.WallCount == 1)
        {
            edit.Freed++;
            if (edit.LocalRoom != 0) edit.Labels[node] = edit.LocalRoom;
        }
    }
    internal static void RemovedNode(ShipRoomsVoxelsTreeNode node)
    {
        var edit = MutationFor(node);
        if (edit != null && edit.LocalRoom != 0 && node.IsLeaf && node.WallCount == 0 && edit.Labels.TryGetValue(node, out var label)) node.RoomId = label;
    }

    internal static void Split(ShipRoomsVoxelsTreeNode node)
    {
        var edit = MutationFor(node);
        ushort label = 0;
        if (edit != null) edit.Labels.TryGetValue(node, out label);
        NativeSplit(node); // The installed implementation copies TmpGasContent to its children.
        if (edit == null) return;
        foreach (var child in Children(node))
        {
            edit.Prepared.Add(child);
            if (edit.LocalRoom != 0) child.RoomId = label;
        }
    }

    internal static void Merge(ShipRoomsVoxelsTreeNode node)
    {
        var edit = MutationFor(node);
        bool local = edit != null && edit.LocalRoom != 0;
        if (edit != null)
            foreach (var child in Children(node))
            {
                edit.Gas(child);
                if (local && child.RoomId != edit!.LocalRoom) { edit.InvalidLocal = true; local = false; }
            }
        NativeMerge(node); // Native equal-volume gas interpolation, including freed vacuum voxels.
        if (edit == null) return;
        edit.Prepared.Add(node);
        if (local) { node.RoomId = edit.LocalRoom; edit.Labels[node] = edit.LocalRoom; }
    }

    private static Edit? MutationFor(ShipRoomsVoxelsTreeNode node)
    {
        var edit = _mutation;
        if (edit == null) return null;
        var current = Root(edit.Tree);
        var coord = node.Coord;
        if (coord.x < current.Coord.x || coord.y < current.Coord.y || coord.z < current.Coord.z ||
            coord.x >= current.Coord.x + current.Bounds || coord.y >= current.Coord.y + current.Bounds || coord.z >= current.Coord.z + current.Bounds) return null;
        while (current.Bounds > node.Bounds && !current.IsLeaf)
        {
            int half = current.Bounds / 2;
            bool right = coord.x >= current.Coord.x + half, top = coord.y >= current.Coord.y + half, forward = coord.z >= current.Coord.z + half;
            current = right ? top ? forward ? current.RightTopForward : current.RightTopBack : forward ? current.RightBottomForward : current.RightBottomBack
                : top ? forward ? current.LeftTopForward : current.LeftTopBack : forward ? current.LeftBottomForward : current.LeftBottomBack;
        }
        return ReferenceEquals(current, node) ? edit : null;
    }

    internal static void ExpandLow(ShipRoomsVoxelsTree tree) => Expand(tree, NativeExpandLow);
    internal static void ExpandHigh(ShipRoomsVoxelsTree tree) => Expand(tree, NativeExpandHigh);
    private static void Expand(ShipRoomsVoxelsTree tree, Action<ShipRoomsVoxelsTree> native)
    {
        var old = Root(tree);
        native(tree);
        var edit = _mutation;
        if (edit == null || !ReferenceEquals(edit.Tree, tree)) return;
        // These nodes did not exist when native clear would have sampled the atmosphere.
        var root = Root(tree);
        edit.Prepared.Add(root);
        foreach (var child in Children(root)) if (!ReferenceEquals(child, old)) edit.Prepared.Add(child);
    }

    internal static void Create(ShipRoomsVoxelUpdater updater)
    {
        var edit = _current;
        if (edit == null || !ReferenceEquals(edit.Updater, updater)) { NativeCreate(updater); return; }
        if (edit.LocalRoom != 0)
        {
            if (!edit.InvalidLocal && ReconnectLocal(edit)) { if (DiagnosticCounters.Active) LocalEdits++; return; }
            // Walls were already changed. Rebuild affected regions without replaying mutations.
            ClearClosedRegions(edit, true);
            edit.LocalRoom = 0;
        }
        if (DiagnosticCounters.Active) GeneralEdits++;
        Rebuild(edit);
    }

    private sealed class Component
    {
        internal int Parent;
        internal readonly Stack<ShipRoomsVoxelsTreeNode> Pending = new Stack<ShipRoomsVoxelsTreeNode>();
        internal readonly List<ShipRoomsVoxelsTreeNode> Nodes = new List<ShipRoomsVoxelsTreeNode>();
        internal double Volume, Gas;
        internal Vector3Int Seed;
    }

    private static int Find(List<Component> components, int index)
    {
        while (components[index].Parent != index)
        {
            components[index].Parent = components[components[index].Parent].Parent;
            index = components[index].Parent;
        }
        return index;
    }
    private static double Volume(ShipRoomsVoxelsTreeNode node) => (double)node.Bounds * node.Bounds * node.Bounds;

    // Grow seeded components together. Once only one unfinished component remains, all
    // completed components are exact cut-off rooms; the large survivor needs no full walk.
    private static bool ReconnectLocal(Edit edit)
    {
        var components = new List<Component>();
        var owners = new Dictionary<ShipRoomsVoxelsTreeNode, int>(RefComparer.Instance);
        foreach (var seed in CreateSeeds(edit.Updater))
        {
            var node = edit.Tree.GetLeaf(seed);
            if (node.WallCount != 0 || owners.ContainsKey(node)) continue;
            if (node.RoomId != edit.LocalRoom || Boundary(node, edit.Tree.LowerBounds, edit.Tree.UpperBounds)) return false;
            var component = new Component { Parent = components.Count, Seed = seed };
            owners.Add(node, components.Count);
            Include(edit, component, node);
            components.Add(component);
        }
        double totalVolume = edit.OldRoom.RoomVolume / ShipRoomsConstants.GasVolumeCapacityPerVoxel + edit.Freed - edit.Occupied;
        double totalGas = edit.OldRoom.GasState.GasDensity * (double)(edit.OldRoom.RoomVolume / ShipRoomsConstants.GasVolumeCapacityPerVoxel) - edit.DisplacedGas;
        if (components.Count == 0)
        {
            if (totalVolume != 0) return false;
            edit.Tree.RemoveRoomId(edit.LocalRoom); edit.Gases.RemoveRoom(edit.LocalRoom); return true;
        }
        int roots = components.Count, localVisits = 0;
        while (roots > 1)
        {
            int open = 0;
            for (int i = 0; i < components.Count; i++)
                if (Find(components, i) == i && components[i].Pending.Count != 0) open++;
            if (open <= 1) break;
            for (int i = 0; i < components.Count && roots > 1; i++)
            {
                int root = Find(components, i);
                if (root != i) continue;
                // Bounded turns keep a small newly separated room from forcing a full large-room scan.
                for (int step = 0; step < 8 && components[root].Pending.Count != 0 && roots > 1; step++)
                {
                    var node = components[root].Pending.Pop(); if (DiagnosticCounters.Active) edit.Visits++;
                    if (++localVisits > LocalVisitLimit) return false;
                    foreach (var neighbors in Neighbors(node)) foreach (var neighbor in neighbors)
                        {
                            if (neighbor.WallCount != 0) continue;
                            if (neighbor.RoomId != edit.LocalRoom || Boundary(neighbor, edit.Tree.LowerBounds, edit.Tree.UpperBounds)) return false;
                            root = Find(components, root);
                            if (!owners.TryGetValue(neighbor, out var owner))
                            {
                                owners.Add(neighbor, root); Include(edit, components[root], neighbor); continue;
                            }
                            int other = Find(components, owner);
                            if (root == other) continue;
                            if (components[root].Nodes.Count < components[other].Nodes.Count) { int swap = root; root = other; other = swap; }
                            var keep = components[root]; var lose = components[other];
                            lose.Parent = root; roots--;
                            keep.Nodes.AddRange(lose.Nodes); lose.Nodes.Clear();
                            while (lose.Pending.Count != 0) keep.Pending.Push(lose.Pending.Pop());
                            keep.Volume += lose.Volume; keep.Gas += lose.Gas;
                        }
                }
            }
        }
        var survivors = Enumerable.Range(0, components.Count).Where(i => Find(components, i) == i).ToArray();
        int keeper = survivors.OrderByDescending(i => components[i].Pending.Count != 0).ThenByDescending(i => components[i].Volume).First();
        double cutVolume = 0, cutGas = 0;
        foreach (var index in survivors)
            if (index != keeper)
            {
                if (components[index].Pending.Count != 0) return false;
                cutVolume += components[index].Volume; cutGas += components[index].Gas;
            }
        double remainingVolume = totalVolume - cutVolume, remainingGas = totalGas - cutGas;
        if (remainingVolume <= 0 || remainingGas < -1e-8 || !Finite((float)remainingGas)) return false;
        foreach (var index in survivors)
        {
            if (index == keeper) continue;
            var component = components[index];
            ushort id = edit.Tree.RegisterNewRoomId();
            foreach (var node in component.Nodes) node.RoomId = id;
            edit.Gases.SetRoomGasContent(id, State(component.Volume, component.Gas, component.Seed));
        }
        var oldSeed = edit.Tree.GetLeaf(edit.OldRoom.RoomScanSeedCoord);
        var seedForKeeper = oldSeed.WallCount == 0 && oldSeed.RoomId == edit.LocalRoom ? edit.OldRoom.RoomScanSeedCoord : components[keeper].Seed;
        edit.Gases.SetRoomGasContent(edit.LocalRoom, State(remainingVolume, Math.Max(0, remainingGas), seedForKeeper));
        return true;
    }

    private static void Include(Edit edit, Component component, ShipRoomsVoxelsTreeNode node)
    {
        double volume = Volume(node);
        component.Volume += volume; component.Gas += edit.Gas(node).GasDensity * volume;
        component.Nodes.Add(node); component.Pending.Push(node);
    }
    private static RoomGasState State(double voxels, double gas, Vector3Int seed) => new RoomGasState
    {
        RoomVolume = (float)(voxels * ShipRoomsConstants.GasVolumeCapacityPerVoxel),
        RoomScanSeedCoord = seed,
        GasState = new GasState { GasItemId = ItemsConstants.OxygenItemId, GasDensity = (float)(gas / voxels) }
    };

    private static bool Boundary(ShipRoomsVoxelsTreeNode node, Vector3Int low, Vector3Int high)
    {
        var near = node.Coord; var far = near + Vector3Int.one * (node.Bounds - 1);
        return near.x <= low.x || near.y <= low.y || near.z <= low.z || far.x >= high.x || far.y >= high.y || far.z >= high.z;
    }

    private static void Rebuild(Edit edit)
    {
        var outside = new HashSet<ShipRoomsVoxelsTreeNode>(RefComparer.Instance);
        var pending = new Stack<ShipRoomsVoxelsTreeNode>();
        var seen = new HashSet<ShipRoomsVoxelsTreeNode>(RefComparer.Instance);
        var region = new List<ShipRoomsVoxelsTreeNode>();
        var low = edit.Tree.LowerBounds; var high = edit.Tree.UpperBounds;
        foreach (var seed in CreateSeeds(edit.Updater))
        {
            pending.Clear(); seen.Clear(); region.Clear();
            pending.Push(edit.Tree.GetLeaf(seed));
            bool exterior = false;
            float voxels = 0, gas = 0;
            while (pending.Count != 0)
            {
                var node = pending.Pop();
                if (node.WallCount != 0 || node.RoomId != 0) continue;
                if (outside.Contains(node)) { exterior = true; break; }
                if (!seen.Add(node)) continue;
                if (DiagnosticCounters.Active) edit.Visits++;
                region.Add(node);
                if (Boundary(node, low, high)) { exterior = true; break; }
                float v = (float)Volume(node); voxels += v; gas += edit.Gas(node).GasDensity * v;
                // Same order as native closed-room traversal and weighted float accumulation.
                foreach (var neighbor in node.NeighborsTop) pending.Push(neighbor);
                foreach (var neighbor in node.NeighborsLeft) pending.Push(neighbor);
                foreach (var neighbor in node.NeighborsRight) pending.Push(neighbor);
                foreach (var neighbor in node.NeighborsBack) pending.Push(neighbor);
                foreach (var neighbor in node.NeighborsForward) pending.Push(neighbor);
                foreach (var neighbor in node.NeighborsBottom) pending.Push(neighbor);
            }
            if (exterior)
            {
                if (DiagnosticCounters.Active) OutsideExits++;
                foreach (var node in region) outside.Add(node);
            }
            else if (region.Count != 0)
            {
                ushort id = edit.Tree.RegisterNewRoomId();
                foreach (var node in region) node.RoomId = id;
                edit.Gases.SetRoomGasContent(id, State(voxels, gas, seed));
            }
        }
    }

    private static IEnumerable<List<ShipRoomsVoxelsTreeNode>> Neighbors(ShipRoomsVoxelsTreeNode node)
    {
        yield return node.NeighborsLeft; yield return node.NeighborsRight; yield return node.NeighborsBottom;
        yield return node.NeighborsTop; yield return node.NeighborsBack; yield return node.NeighborsForward;
    }
    private static IEnumerable<ShipRoomsVoxelsTreeNode> Children(ShipRoomsVoxelsTreeNode node)
    {
        yield return node.LeftBottomBack; yield return node.LeftBottomForward; yield return node.LeftTopBack; yield return node.LeftTopForward;
        yield return node.RightBottomBack; yield return node.RightBottomForward; yield return node.RightTopBack; yield return node.RightTopForward;
    }

    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var codes = instructions.Select(code => new CodeInstruction(code)).ToList();
        var replacements = new Dictionary<MethodInfo, string>();
        string? before = null, after = null;
        if (__originalMethod == UpdateMethod)
        {
            replacements.Add(ClearMethod, nameof(Clear)); replacements.Add(CreateMethod, nameof(Create));
            replacements.Add(AddWallsMethod, nameof(AddWalls)); replacements.Add(RemoveWallsMethod, nameof(RemoveWalls));
        }
        else if (__originalMethod == AddNodeMethod) { before = nameof(PrepareAdd); replacements.Add(SplitMethod, nameof(Split)); }
        else if (__originalMethod == RemoveNodeMethod) { before = nameof(PrepareRemove); after = nameof(RemovedNode); replacements.Add(MergeMethod, nameof(Merge)); }
        else if (__originalMethod == AddTreeMethod) { replacements.Add(ExpandLowMethod, nameof(ExpandLow)); replacements.Add(ExpandHighMethod, nameof(ExpandHigh)); }
        else throw new InvalidOperationException("Unrecognized room rewrite target.");
        foreach (var pair in replacements)
        {
            var matches = codes.Where(code => code.Calls(pair.Key)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Room call contract changed: " + pair.Key.Name);
            matches[0].opcode = OpCodes.Call; matches[0].operand = Method(typeof(IncrementalRooms), pair.Value);
        }
        if (after != null)
            for (int i = codes.Count - 1; i >= 0; i--)
                if (codes[i].opcode == OpCodes.Ret)
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0); load.labels.AddRange(codes[i].labels); codes[i].labels.Clear();
                    codes.InsertRange(i, new[] { load, new CodeInstruction(OpCodes.Call, Method(typeof(IncrementalRooms), after)) });
                }
        if (before != null) codes.InsertRange(0, new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Call, Method(typeof(IncrementalRooms), before)) });
        return codes;
    }
}
