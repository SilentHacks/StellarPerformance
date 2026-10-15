using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Items.Model;
using Managers.Client;
using Managers.Server;
using Planets.EnvironmentGeneration;
using Planets.Terrain;
using Planets.Terrain.Meshes;
using Planets.Terrain.Model;
using Players.Services;
using Ships;
using Ships.Cables;
using Ships.Cables.Fluids;
using Ships.Grid;
using Ships.Grid.Model;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Model.Placement;
using Ships.Interface.Model.State;
using Ships.Interface.Settings;
using Ships.Interface.Values;
using Ships.Parts.Common.Model;
using Ships.Parts.EthanolDistiller;
using Ships.Parts.FluidPump;
using Ships.Parts.WoodPowerGenerator;
using Ships.Physics;
using Tools.Build;
using Tools.Recycler;
using UnityEngine;
using UnityEngine.LowLevel;
using WorldTracking.Interface.Model.Spaces;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class QueryCacheTests
{
    [Fact]
    public void ShipGridBoundsMatchesNativeComputationAndInvalidatesOnMutation()
    {
        var grid = new ShipGrid();
        Assert.Equal(grid.GetCoordBounds().UpperCoord, ShipGridBounds.Read(grid).UpperCoord);
        Assert.Equal(grid.GetCoordBounds().LowerCoord, ShipGridBounds.Read(grid).LowerCoord);

        var hullSettings = (HullPartSettings)RuntimeHelpers.GetUninitializedObject(typeof(HullPartSettings));
        hullSettings.size = Vector3Int.one;
        var partA = new HullPart(hullSettings, new HullPartPlacement(new HullPartPosition(new Vector3Int(2, 3, 4), VoxelPartType.Floor), PartOrientation.Forward, PartOrientation.Up));
        var partSameChunk = new HullPart(hullSettings, new HullPartPlacement(new HullPartPosition(new Vector3Int(5, 6, 7), VoxelPartType.Floor), PartOrientation.Forward, PartOrientation.Up));
        var partB = new HullPart(hullSettings, new HullPartPlacement(new HullPartPosition(new Vector3Int(-10, 12, 19), VoxelPartType.Floor), PartOrientation.Forward, PartOrientation.Up));

        grid.AddHullPart(partA);
        ShipGridBounds.Invalidate(grid);
        var boundsA = ShipGridBounds.Read(grid);
        var nativeA = grid.GetCoordBounds();
        Assert.Equal(nativeA.UpperCoord, boundsA.UpperCoord);
        Assert.Equal(nativeA.LowerCoord, boundsA.LowerCoord);

        grid.AddHullPart(partSameChunk);
        var staleWhileUninvalidated = ShipGridBounds.Read(grid);
        Assert.Equal(boundsA.UpperCoord, staleWhileUninvalidated.UpperCoord);

        ShipGridBounds.Invalidate(grid);
        var boundsSameChunk = ShipGridBounds.Read(grid);
        Assert.Equal(new Vector3Int(5, 6, 7), boundsSameChunk.UpperCoord);

        grid.AddHullPart(partB);
        var boundsAB = ShipGridBounds.Read(grid);
        var nativeAB = grid.GetCoordBounds();
        Assert.Equal(nativeAB.UpperCoord, boundsAB.UpperCoord);
        Assert.Equal(nativeAB.LowerCoord, boundsAB.LowerCoord);

        grid.RemoveHullPart(partB);
        grid.RemoveHullPart(partSameChunk);
        ShipGridBounds.OnMutationEnd(null, grid);
        var afterRemove = ShipGridBounds.Read(grid);
        Assert.Equal(new Vector3Int(2, 3, 4), afterRemove.UpperCoord);
        Assert.Equal(new Vector3Int(2, 3, 4), afterRemove.LowerCoord);
    }

    [Fact]
    public void AllTwentyFiveStatefulPartQueryTargetsTranspileCleanly()
    {
        Assert.Equal(25, StatefulPartQueries.Targets.Length);
        foreach (var target in StatefulPartQueries.Targets)
        {
            Assert.NotNull(target);
            var original = ManagedMethod.Instructions(target);
            var nativeCalls = original.Count(code => code.operand is MethodInfo method && StatefulPartQueries.IsGetAllStatefulParts(method));
            Assert.True(nativeCalls >= 1, $"Expected GetAllStatefulParts call in {target.DeclaringType?.Name}.{target.Name}");
            var transformed = StatefulPartQueries.Transpile(original).ToArray();
            Assert.Equal(original.Count, transformed.Length);
            Assert.DoesNotContain(transformed, code => code.operand is MethodInfo method && StatefulPartQueries.IsGetAllStatefulParts(method));
            Assert.Equal(nativeCalls, transformed.Count(code => code.operand is MethodInfo method && method.DeclaringType == typeof(StatefulPartQueries) && method.Name == nameof(StatefulPartQueries.GetParts)));
        }
    }

    [Fact]
    public void StatefulPartQueriesReusesResolvedCollectionAndInvalidatesOnAddOrRemove()
    {
        var shipState = new ShipState(1u, 2u, null!);
        var cache = new ShipStatefulPartsCache(shipState);
        var tracked = (TrackedShipServer)RuntimeHelpers.GetUninitializedObject(typeof(TrackedShipServer));
        AccessTools.Field(typeof(TrackedShip), "<State>k__BackingField").SetValue(tracked, shipState);
        AccessTools.Field(typeof(TrackedShip), "<StatefulPartsCache>k__BackingField").SetValue(tracked, cache);

        Assert.Same(StatefulPartQueries.Empty, StatefulPartQueries.GetParts<ThrusterState>(tracked));

        var settings = (PartSettings)RuntimeHelpers.GetUninitializedObject(typeof(PartSettings));
        var part1 = (StatefulPart)RuntimeHelpers.GetUninitializedObject(typeof(StatefulPart));
        part1.Id = 10;
        part1.State = new ThrusterState { ThrusterStrength = 0.75f };
        AccessTools.Field(typeof(StatefulPart), "<Settings>k__BackingField").SetValue(part1, settings);

        var partsDict = (Dictionary<ushort, StatefulPart>)AccessTools.Field(typeof(ShipState), "_statefulParts").GetValue(shipState)!;
        partsDict[part1.Id] = part1;
        cache.AddStatefulPart(part1);
        StatefulPartQueries.Invalidate(cache);

        var firstRead = StatefulPartQueries.GetParts<ThrusterState>(tracked);
        var secondRead = StatefulPartQueries.GetParts<ThrusterState>(tracked);
        Assert.Same(firstRead, secondRead);
        Assert.Single(firstRead);
        Assert.Same(part1, firstRead.Single());

        part1.State = new ThrusterState { ThrusterStrength = 1.0f };
        Assert.Equal(1.0f, ((ThrusterState)StatefulPartQueries.GetParts<ThrusterState>(tracked).Single().State).ThrusterStrength);

        cache.RemoveStatefulPart(part1);
        partsDict.Remove(part1.Id);
        StatefulPartQueries.OnMutationEnd(null, cache);
        Assert.Same(StatefulPartQueries.Empty, StatefulPartQueries.GetParts<ThrusterState>(tracked));
    }
}
