using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ships.Grid;
using Ships.Grid.Model;
using Ships.Interface.Model.Parts;
using UnityEngine;

namespace StellarPerformance;

internal static class ShipGridBounds
{
    static ShipGridBounds() { }

    private sealed class Entry
    {
        internal bool Valid;
        internal int ChunkCount;
        internal CoordBounds Bounds;
    }

    private static readonly Func<ShipGrid, Dictionary<Vector3Int, ShipVoxelChunk>> Chunks = BindChunks();
    private static readonly ConditionalWeakTable<ShipGrid, Entry> Cache = new ConditionalWeakTable<ShipGrid, Entry>();

    private static Func<ShipGrid, Dictionary<Vector3Int, ShipVoxelChunk>> BindChunks()
    {
        var grid = Expression.Parameter(typeof(ShipGrid), "grid");
        var field = AccessTools.Field(typeof(ShipGrid), "_chunks") ?? throw new MissingFieldException("ShipGrid._chunks");
        if (field.FieldType != typeof(Dictionary<Vector3Int, ShipVoxelChunk>)) throw new InvalidOperationException("ShipGrid._chunks type changed.");
        return Expression.Lambda<Func<ShipGrid, Dictionary<Vector3Int, ShipVoxelChunk>>>(Expression.Field(grid, field), grid).Compile();
    }

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        foreach (var target in new[] { AccessTools.Method(typeof(ShipGrid), nameof(ShipGrid.GetCoordBounds)) })
            PatchSafety.RequireOriginalBody(target, harmony.Id);
        var setPart = AccessTools.Method(typeof(ShipGrid), "SetPart", new[] { typeof(HullPart) }) ?? throw new MissingMethodException("ShipGrid.SetPart");
        var removeHull = AccessTools.Method(typeof(ShipGrid), nameof(ShipGrid.RemoveHullPart), new[] { typeof(HullPart) }) ?? throw new MissingMethodException("ShipGrid.RemoveHullPart");
        var removeStateful = AccessTools.Method(typeof(ShipGrid), nameof(ShipGrid.RemoveStatefulPart), new[] { typeof(StatefulPart) }) ?? throw new MissingMethodException("ShipGrid.RemoveStatefulPart");
        var getBounds = AccessTools.Method(typeof(ShipGrid), nameof(ShipGrid.GetCoordBounds), Type.EmptyTypes) ?? throw new MissingMethodException("ShipGrid.GetCoordBounds");
        foreach (var mutation in new[] { setPart, removeHull, removeStateful })
            harmony.Patch(mutation, prefix: new HarmonyMethod(typeof(ShipGridBounds), nameof(Invalidate)), finalizer: new HarmonyMethod(typeof(ShipGridBounds), nameof(OnMutationEnd)));
        harmony.Patch(getBounds, prefix: new HarmonyMethod(typeof(ShipGridBounds), nameof(BeforeGetCoordBounds)));
    }

    internal static void Invalidate(ShipGrid __instance)
    {
        if (__instance != null && Cache.TryGetValue(__instance, out var entry)) entry.Valid = false;
    }

    internal static Exception? OnMutationEnd(Exception? __exception, ShipGrid __instance)
    {
        Invalidate(__instance);
        return __exception;
    }

    internal static bool BeforeGetCoordBounds(ShipGrid __instance, ref CoordBounds __result)
    {
        __result = Read(__instance);
        return false;
    }

    internal static CoordBounds Read(ShipGrid grid)
    {
        var chunks = Chunks(grid);
        var entry = Cache.GetValue(grid, _ => new Entry());
        if (entry.Valid && entry.ChunkCount == chunks.Count) return entry.Bounds;
        var bounds = Compute(chunks);
        entry.Bounds = bounds;
        entry.ChunkCount = chunks.Count;
        entry.Valid = true;
        return bounds;
    }

    internal static CoordBounds Compute(Dictionary<Vector3Int, ShipVoxelChunk> chunks)
    {
        var result = new CoordBounds
        {
            UpperCoord = new Vector3Int(int.MinValue, int.MinValue, int.MinValue),
            LowerCoord = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue)
        };
        var gridSize = ShipVoxelConstants.VoxelsGridSize;
        foreach (var pair in chunks)
        {
            var coordBounds = pair.Value.GetExtremities();
            if (coordBounds.Right < coordBounds.Left) continue;
            var offset = pair.Key * gridSize;
            coordBounds.UpperCoord += offset;
            coordBounds.LowerCoord += offset;
            if (coordBounds.Forward > result.Forward) result.Forward = coordBounds.Forward;
            if (coordBounds.Backward < result.Backward) result.Backward = coordBounds.Backward;
            if (coordBounds.Left < result.Left) result.Left = coordBounds.Left;
            if (coordBounds.Right > result.Right) result.Right = coordBounds.Right;
            if (coordBounds.Up > result.Up) result.Up = coordBounds.Up;
            if (coordBounds.Down < result.Down) result.Down = coordBounds.Down;
        }
        return result;
    }
}
