using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Managers.Client;
using Planets;
using Planets.EnvironmentGeneration;
using Planets.Objects.Common.Model;
using Planets.Terrain;
using Planets.Terrain.Meshes;
using Planets.Terrain.Model;
using UnityEngine;
using WorldTracking.Interface.Model;
using WorldTracking.Interface.Services;
using WorldTracking.Interface.Values;

namespace StellarPerformance;

internal static class WorldVisualUpdates
{
    static WorldVisualUpdates() { }

    private static readonly Func<PlanetHotspotsEnvironmentVisuals, Planet> PlanetOf = BindField<PlanetHotspotsEnvironmentVisuals, Planet>("_planet");
    private static readonly Func<PlanetHotspotsEnvironmentVisuals, IWorldTransposeClient> TransposeOf = BindField<PlanetHotspotsEnvironmentVisuals, IWorldTransposeClient>("_transpose");
    private static readonly Func<PlanetHotspotsEnvironmentVisuals, IDictionary> ChunksDictOf = BindChunksDictionary();
    private static readonly Action<object, TransformOperation, Quaternion> ApplyChunkOrigin = BindApplyChunkOrigin();
    private static readonly Func<object, bool> IsChunkVisited = BindChunkVisited();
    private static readonly Action<PlanetHotspotsEnvironmentVisuals, ulong> UnloadChunk = BindUnloadChunk();
    [ThreadStatic] private static List<ulong>? _unvisitedHashes;

    private static Func<TOwner, TField> BindField<TOwner, TField>(string name)
    {
        var owner = Expression.Parameter(typeof(TOwner), "owner");
        var field = AccessTools.Field(typeof(TOwner), name) ?? throw new MissingFieldException(typeof(TOwner).FullName, name);
        if (field.FieldType != typeof(TField)) throw new InvalidOperationException(typeof(TOwner).Name + "." + name + " type changed.");
        return Expression.Lambda<Func<TOwner, TField>>(Expression.Field(owner, field), owner).Compile();
    }

    private static Func<PlanetHotspotsEnvironmentVisuals, IDictionary> BindChunksDictionary()
    {
        var ownerType = typeof(PlanetHotspotsEnvironmentVisuals);
        var chunksField = AccessTools.Field(ownerType, "_instantiatedChunkObjects") ?? throw new MissingFieldException(ownerType.FullName, "_instantiatedChunkObjects");
        var dictField = AccessTools.Field(chunksField.FieldType, "_instantiatedChunkObjects") ?? throw new MissingFieldException(chunksField.FieldType.FullName, "_instantiatedChunkObjects");
        var owner = Expression.Parameter(ownerType, "owner");
        var dict = Expression.Field(Expression.Field(owner, chunksField), dictField);
        return Expression.Lambda<Func<PlanetHotspotsEnvironmentVisuals, IDictionary>>(Expression.Convert(dict, typeof(IDictionary)), owner).Compile();
    }

    private static Action<object, TransformOperation, Quaternion> BindApplyChunkOrigin()
    {
        var ownerType = typeof(PlanetHotspotsEnvironmentVisuals);
        var chunksField = AccessTools.Field(ownerType, "_instantiatedChunkObjects") ?? throw new MissingFieldException(ownerType.FullName, "_instantiatedChunkObjects");
        var dictField = AccessTools.Field(chunksField.FieldType, "_instantiatedChunkObjects") ?? throw new MissingFieldException(chunksField.FieldType.FullName, "_instantiatedChunkObjects");
        var nodeType = dictField.FieldType.GetGenericArguments()[1];
        var refsField = AccessTools.Field(nodeType, "ObjectRefs") ?? throw new MissingFieldException(nodeType.FullName, "ObjectRefs");
        var listType = refsField.FieldType;
        var objectRefType = listType.GetGenericArguments()[0];
        var getCount = AccessTools.PropertyGetter(listType, "Count") ?? throw new MissingMethodException(listType.FullName, "get_Count");
        var getItem = AccessTools.Method(listType, "get_Item", new[] { typeof(int) }) ?? throw new MissingMethodException(listType.FullName, "get_Item");
        var getTransform = AccessTools.PropertyGetter(objectRefType, "Transform") ?? throw new MissingMethodException(objectRefType.FullName, "get_Transform");
        var getVisualObject = AccessTools.PropertyGetter(objectRefType, "VisualObject") ?? throw new MissingMethodException(objectRefType.FullName, "get_VisualObject");
        var applyMethod = AccessTools.Method(typeof(WorldVisualUpdates), nameof(ApplyHotspotObjectTransform)) ?? throw new MissingMethodException("WorldVisualUpdates.ApplyHotspotObjectTransform");

        var nodeObj = Expression.Parameter(typeof(object), "nodeObj");
        var opParam = Expression.Parameter(typeof(TransformOperation), "op");
        var rotParam = Expression.Parameter(typeof(Quaternion), "baseRotation");
        var listVar = Expression.Variable(listType, "refs");
        var countVar = Expression.Variable(typeof(int), "count");
        var indexVar = Expression.Variable(typeof(int), "i");
        var refVar = Expression.Variable(objectRefType, "objRef");
        var breakLabel = Expression.Label("break");

        var body = Expression.Block(
            new[] { listVar, countVar, indexVar, refVar },
            Expression.Assign(listVar, Expression.Field(Expression.Convert(nodeObj, nodeType), refsField)),
            Expression.Assign(countVar, Expression.Call(listVar, getCount)),
            Expression.Assign(indexVar, Expression.Constant(0)),
            Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(indexVar, countVar),
                    Expression.Block(
                        Expression.Assign(refVar, Expression.Call(listVar, getItem, indexVar)),
                        Expression.Call(applyMethod, Expression.Call(refVar, getTransform), Expression.Call(refVar, getVisualObject), opParam, rotParam),
                        Expression.PreIncrementAssign(indexVar)),
                    Expression.Break(breakLabel)),
                breakLabel));

        return Expression.Lambda<Action<object, TransformOperation, Quaternion>>(body, nodeObj, opParam, rotParam).Compile();
    }

    private static Func<object, bool> BindChunkVisited()
    {
        var ownerType = typeof(PlanetHotspotsEnvironmentVisuals);
        var chunksField = AccessTools.Field(ownerType, "_instantiatedChunkObjects") ?? throw new MissingFieldException(ownerType.FullName, "_instantiatedChunkObjects");
        var dictField = AccessTools.Field(chunksField.FieldType, "_instantiatedChunkObjects") ?? throw new MissingFieldException(chunksField.FieldType.FullName, "_instantiatedChunkObjects");
        var nodeType = dictField.FieldType.GetGenericArguments()[1];
        var visitedField = AccessTools.Field(nodeType, "Visited") ?? throw new MissingFieldException(nodeType.FullName, "Visited");
        var nodeObj = Expression.Parameter(typeof(object), "nodeObj");
        return Expression.Lambda<Func<object, bool>>(Expression.Field(Expression.Convert(nodeObj, nodeType), visitedField), nodeObj).Compile();
    }

    private static Action<PlanetHotspotsEnvironmentVisuals, ulong> BindUnloadChunk()
    {
        var ownerType = typeof(PlanetHotspotsEnvironmentVisuals);
        var method = AccessTools.Method(ownerType, "UnloadChunk", new[] { typeof(ulong) }) ?? throw new MissingMethodException(ownerType.FullName, "UnloadChunk");
        var owner = Expression.Parameter(ownerType, "owner");
        var hash = Expression.Parameter(typeof(ulong), "hash");
        return Expression.Lambda<Action<PlanetHotspotsEnvironmentVisuals, ulong>>(Expression.Call(owner, method, hash), owner, hash).Compile();
    }

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        foreach (var target in new[] { AccessTools.Method(typeof(PlanetHotspotsEnvironmentVisuals), "UpdateOrigin"), AccessTools.Method(typeof(PlanetHotspotsEnvironmentVisuals), "Cleanup") })
            PatchSafety.RequireOriginalBody(target, harmony.Id);
        var updateOrigin = AccessTools.Method(typeof(PlanetHotspotsEnvironmentVisuals), "UpdateOrigin", Type.EmptyTypes) ?? throw new MissingMethodException("PlanetHotspotsEnvironmentVisuals.UpdateOrigin");
        var cleanup = AccessTools.Method(typeof(PlanetHotspotsEnvironmentVisuals), "Cleanup", Type.EmptyTypes) ?? throw new MissingMethodException("PlanetHotspotsEnvironmentVisuals.Cleanup");
        harmony.Patch(updateOrigin, prefix: new HarmonyMethod(typeof(WorldVisualUpdates), nameof(BeforeUpdateOrigin)));
        harmony.Patch(cleanup, prefix: new HarmonyMethod(typeof(WorldVisualUpdates), nameof(BeforeCleanup)));
    }

    internal static void ApplyHotspotObjectTransform(Transform transform, PlanetObjectVisuals visualObject, TransformOperation transformOperation, Quaternion baseRotation)
    {
        transform.position = transformOperation.MultiplyPoint(visualObject.planetRelativePos);
        transform.rotation = baseRotation * visualObject.planetRelativeRot;
    }

    internal static bool BeforeUpdateOrigin(PlanetHotspotsEnvironmentVisuals __instance)
    {
        var chunks = ChunksDictOf(__instance);
        if (chunks.Count == 0) return false;
        var planet = PlanetOf(__instance);
        var transpose = TransposeOf(__instance);
        var transformOperation = transpose.ToTransformOperation(planet.SurfaceSpace, ClientOrigin.LocalVisualSpace);
        var baseRotation = (Quaternion)transformOperation.Rotation;
        foreach (var node in chunks.Values)
            if (node != null) ApplyChunkOrigin(node, transformOperation, baseRotation);
        return false;
    }

    internal static bool BeforeCleanup(PlanetHotspotsEnvironmentVisuals __instance)
    {
        var chunks = ChunksDictOf(__instance);
        if (chunks.Count == 0) return false;
        List<ulong>? toUnload = null;
        foreach (DictionaryEntry entry in chunks)
        {
            if (entry.Value != null && !IsChunkVisited(entry.Value))
            {
                if (toUnload == null)
                {
                    toUnload = _unvisitedHashes ?? (_unvisitedHashes = new List<ulong>(8));
                    toUnload.Clear();
                }
                toUnload.Add((ulong)entry.Key);
            }
        }
        if (toUnload != null)
        {
            for (var i = 0; i < toUnload.Count; i++)
                UnloadChunk(__instance, toUnload[i]);
            toUnload.Clear();
        }
        return false;
    }
}
