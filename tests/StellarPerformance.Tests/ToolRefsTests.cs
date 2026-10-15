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

public sealed class ToolRefsTests
{
    [Fact]
    public void ToolRefsCacheResolvesSelectedToolInConstantTimeAndClearsOnInvalidate()
    {
        var tracker = (LocalSelectedToolTracker)RuntimeHelpers.GetUninitializedObject(typeof(LocalSelectedToolTracker));
        var buildTool = (BuildToolLocalClient)RuntimeHelpers.GetUninitializedObject(typeof(BuildToolLocalClient));
        var recyclerTool = (RecyclerTool)RuntimeHelpers.GetUninitializedObject(typeof(RecyclerTool));
        var currentToolField = AccessTools.Field(typeof(LocalSelectedToolTracker), "_currentTool")!;

        currentToolField.SetValue(tracker, null);
        ToolRefsCache.AfterToolStateChanged(tracker);
        BuildToolLocalClient? resolvedBuild = null;
        RecyclerTool? resolvedRecycler = null;
        Assert.False(ToolRefsCache.BeforeGetBuild(ref resolvedBuild));
        Assert.False(ToolRefsCache.BeforeGetRecycler(ref resolvedRecycler));
        Assert.Null(resolvedBuild);
        Assert.Null(resolvedRecycler);

        currentToolField.SetValue(tracker, buildTool);
        ToolRefsCache.AfterToolStateChanged(tracker);
        Assert.False(ToolRefsCache.BeforeGetBuild(ref resolvedBuild));
        Assert.False(ToolRefsCache.BeforeGetRecycler(ref resolvedRecycler));
        Assert.Same(buildTool, resolvedBuild);
        Assert.Null(resolvedRecycler);

        currentToolField.SetValue(tracker, recyclerTool);
        ToolRefsCache.AfterToolStateChanged(tracker);
        Assert.False(ToolRefsCache.BeforeGetBuild(ref resolvedBuild));
        Assert.False(ToolRefsCache.BeforeGetRecycler(ref resolvedRecycler));
        Assert.Null(resolvedBuild);
        Assert.Same(recyclerTool, resolvedRecycler);

        ToolRefsCache.OnWorldInvalidate();
        Assert.False(ToolRefsCache.BeforeGetBuild(ref resolvedBuild));
        Assert.False(ToolRefsCache.BeforeGetRecycler(ref resolvedRecycler));
        Assert.Null(resolvedBuild);
        Assert.Null(resolvedRecycler);
    }
}
