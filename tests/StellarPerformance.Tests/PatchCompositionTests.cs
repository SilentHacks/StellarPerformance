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

public sealed class PatchCompositionTests
{
    [Fact]
    public void WorldVisualUpdatesAndClientSignalCablesComposeCleanly()
    {
        var hotspots = (PlanetHotspotsEnvironmentVisuals)RuntimeHelpers.GetUninitializedObject(typeof(PlanetHotspotsEnvironmentVisuals));
        var chunksField = AccessTools.Field(typeof(PlanetHotspotsEnvironmentVisuals), "_instantiatedChunkObjects")!;
        var chunksInstance = Activator.CreateInstance(chunksField.FieldType, nonPublic: true)!;
        chunksField.SetValue(hotspots, chunksInstance);
        Assert.False(WorldVisualUpdates.BeforeUpdateOrigin(hotspots));
        Assert.False(WorldVisualUpdates.BeforeCleanup(hotspots));

        var updateSignalCables = AccessTools.Method(typeof(SignalCablesVisualUpdater), nameof(SignalCablesVisualUpdater.UpdateSignalCables));
        var originalSignalCables = ManagedMethod.Instructions(updateSignalCables);
        var composed = SignalInputProjection.TranspileClientSignalCables(
            StatefulPartQueries.Transpile(
                originalSignalCables)).ToArray();
        Assert.Equal(originalSignalCables.Count, composed.Length);
        Assert.Single(composed, code => code.Calls(AccessTools.Method(typeof(SignalInputProjection), nameof(SignalInputProjection.GetSignalSockets))));

        var updateGroupValue = AccessTools.Method(typeof(SignalConnectionGroupValues), nameof(SignalConnectionGroupValues.UpdateGroupSignalValue));
        var groupInstructions = ManagedMethod.Instructions(updateGroupValue);
        var once = SignalInputProjection.Transpile(groupInstructions).ToArray();
        var twice = SignalInputProjection.Transpile(once).ToArray();
        Assert.Equal(once.Length, twice.Length);
        Assert.Single(twice, code => code.Calls(AccessTools.Method(typeof(SignalInputProjection), nameof(SignalInputProjection.ReadInputs))));
    }
}
