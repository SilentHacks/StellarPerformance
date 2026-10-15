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

public sealed class PartVisualTests
{
    [Fact]
    public void PartVisualUpdatesDormancyAndBillboardOrientationMatchNativeBehavior()
    {
        var generator = (PowerGeneratorVisuals)RuntimeHelpers.GetUninitializedObject(typeof(PowerGeneratorVisuals));
        AccessTools.Field(typeof(PowerGeneratorVisuals), "_rotationSpeed").SetValue(generator, 0f);
        Assert.False(PartVisualUpdates.BeforePowerGeneratorUpdate(generator));
        AccessTools.Field(typeof(PowerGeneratorVisuals), "_rotationSpeed").SetValue(generator, 50f);
        Assert.True(PartVisualUpdates.BeforePowerGeneratorUpdate(generator));

        var pump = (FluidPumpVisuals)RuntimeHelpers.GetUninitializedObject(typeof(FluidPumpVisuals));
        AccessTools.Field(typeof(FluidPumpVisuals), "_currentRate").SetValue(pump, 0f);
        Assert.True(PartVisualUpdates.BeforeFluidPumpUpdate(pump));
        Assert.False(PartVisualUpdates.BeforeFluidPumpUpdate(pump));
        AccessTools.Field(typeof(FluidPumpVisuals), "_currentRate").SetValue(pump, 0.5f);
        Assert.True(PartVisualUpdates.BeforeFluidPumpUpdate(pump));

        LiquidVisualUpdates.StepFluidTank(0f, 0f, 0f, 1.5f, 0.02f, out var nextDensity, out _, out _, out var shaderDensity);
        Assert.Equal(0f, nextDensity);
        Assert.Equal(0f, shaderDensity);

        LiquidVisualUpdates.StepMachineLiquid(5E-07f, 0f, 5E-07f, 0f, 1f, 0.02f, subtractPos: true, out var nextVel, out var nextMachineDensity, out var nextPos);
        Assert.Equal(0f, nextVel);
        Assert.Equal(0f, nextMachineDensity);
        Assert.Equal(1f, nextPos);

        var surfaceRot = Quaternion.identity;
        var initUp = Vector3.forward;
        var camForwardUp = new Quaternion(0.7071068f, 0f, 0f, 0.7071068f);
        var camBackUp = new Quaternion(-0.7071068f, 0f, 0f, 0.7071068f);
        var camRightUp = new Quaternion(0f, 0f, -0.7071068f, 0.7071068f);
        Assert.Equal(0, PartVisualUpdates.ChooseFlat2Side(surfaceRot, camForwardUp, initUp));
        Assert.Equal(1, PartVisualUpdates.ChooseFlat2Side(surfaceRot, camBackUp, initUp));

        Assert.Equal(0, PartVisualUpdates.ChooseFlat4Side(surfaceRot, camForwardUp, out var up0));
        Assert.Equal(Vector3.forward, up0);
        Assert.Equal(2, PartVisualUpdates.ChooseFlat4Side(surfaceRot, camRightUp, out var up2));
        Assert.Equal(Vector3.right, up2);
    }
}
