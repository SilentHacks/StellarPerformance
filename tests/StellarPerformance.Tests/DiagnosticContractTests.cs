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

public sealed class DiagnosticContractTests
{
    [Fact]
    public void PhaseZeroTopLevelPlayerLoopPhasesAndDiagnosticsCountersAreRecordedAndRestored()
    {
        var capture = new CaptureData("phase0");
        var starts = new long[EngineTimings.TopLevelPhases.Length];
        var order = new List<int>();
        var loop = new PlayerLoopSystem
        {
            subSystemList = new[]
            {
                new PlayerLoopSystem
                {
                    type = typeof(UnityEngine.PlayerLoop.EarlyUpdate),
                    updateFunction = new IntPtr(42),
                    subSystemList = new[]
                    {
                        new PlayerLoopSystem { type = typeof(DiagnosticContractTests), updateDelegate = () => order.Add(1) }
                    }
                },
                new PlayerLoopSystem
                {
                    type = typeof(UnityEngine.PlayerLoop.PostLateUpdate),
                    subSystemList = new[]
                    {
                        new PlayerLoopSystem { type = typeof(DiagnosticContractTests), updateDelegate = () => order.Add(2) }
                    }
                }
            }
        };

        var wrapped = EngineTimings.RewriteTopLevelPhases(loop, true, capture, starts, out var wrappedCount);
        Assert.Equal(2, wrappedCount);
        Assert.Equal(new IntPtr(42), wrapped.subSystemList[0].updateFunction);
        Assert.Equal(3, wrapped.subSystemList[0].subSystemList.Length);
        foreach (var phase in wrapped.subSystemList)
            foreach (var sub in phase.subSystemList)
                sub.updateDelegate();
        Assert.Equal(new[] { 1, 2 }, order);

        var idempotent = EngineTimings.RewriteTopLevelPhases(wrapped, true, capture, starts, out var secondCount);
        Assert.Equal(2, secondCount);
        Assert.Equal(3, idempotent.subSystemList[0].subSystemList.Length);

        var restored = EngineTimings.RewriteTopLevelPhases(idempotent, false, null, null, out _);
        Assert.Single(restored.subSystemList[0].subSystemList);
        Assert.Single(restored.subSystemList[1].subSystemList);

        capture.Frame(16.0, 8192, false);
        capture.Frame(25.0, 0, true);
        capture.CameraTiming("Main Camera (far=10000.0)", 3.5);
        capture.CameraTiming("idCamera (far=1000.0)", 0.9);
        capture.ModTiming("buildplus", 0.08, false);
        capture.RecordStateUpdate("ThrusterState", unchanged: true);
        capture.RecordStateUpdate("ThrusterState", unchanged: false);
        capture.RecordNetworkQueue("EthanolDistillerState", identicalBytes: true);
        capture.RecordTransformWrite(TransformTarget.TerrainCollider, noop: true);
        capture.RecordToolRefs(isRecycler: false, scanned: true);
        capture.RecordHotspotObjects(120);
        capture.RecordStaticObjects(45);
        capture.RecordAtmosphericPlanet(hasPlanet: true, hasRing: true);

        var report = capture.Close();
        Assert.Contains("Stellar Performance " + PerformancePlugin.Version + " diagnostic capture: phase0", report);
        Assert.Contains("LoopEarlyUpdate: calls=1", report);
        Assert.Contains("LoopPostLateUpdate: calls=1", report);
        Assert.Contains("Camera[Main Camera (far=10000.0)]: calls=1", report);
        Assert.Contains("Camera[idCamera (far=1000.0)]: calls=1", report);
        Assert.Contains("Mod[buildplus]: calls=1", report);
        Assert.Contains("Part state updates: total=2, unchanged=1; by type: ThrusterState=2/1", report);
        Assert.Contains("Queued network part updates: total=1, identical-bytes=1; by type: EthanolDistillerState=1/1", report);
        Assert.Contains("TerrainCollider=1/1", report);
        Assert.Contains("ToolRefs Build calls/scans=1/1", report);
        Assert.Contains("hotspot objects max=120", report);
        Assert.Contains("static objects max=45", report);
        Assert.Contains("nearest atmo planet calls=1/1 (ringed=1)", report);
        Assert.Contains("GC frame correlation: gc-event frames=1/2", report);
    }

    [OptionalModFact]
    public void SerializedStateEqualityMatchesGameSerializerAndResolvesSdModDispatcher()
    {
        var sdModKit = Assembly.Load("SDModKit");
        var sdModType = sdModKit.GetType("SDModKit.Api.SDMod", true)!;
        var dispatcher = Diagnostics.ResolveSdModUpdateDispatcher(sdModKit, sdModType);
        Assert.NotNull(dispatcher);

        Assert.True(Diagnostics.AreSerializedStatesEqual(
            new ReactionWheelState { RotationStrength = 0.25f },
            new ReactionWheelState { RotationStrength = 0.75f }));

        Assert.True(Diagnostics.AreSerializedStatesEqual(
            new SignalTransmitterState { Channel = "alpha", SignalValue = 1.0 },
            new SignalTransmitterState { Channel = "alpha", SignalValue = 99.0 }));
        Assert.False(Diagnostics.AreSerializedStatesEqual(
            new SignalTransmitterState { Channel = "alpha", SignalValue = 1.0 },
            new SignalTransmitterState { Channel = "beta", SignalValue = 1.0 }));

        Assert.True(Diagnostics.AreSerializedStatesEqual(
            new GyroscopeState { TargetGyroscopeAnimation = Vector3.up, RotationStrength = Vector3.left },
            new GyroscopeState { TargetGyroscopeAnimation = Vector3.up, RotationStrength = Vector3.right }));
        Assert.False(Diagnostics.AreSerializedStatesEqual(
            new GyroscopeState { TargetGyroscopeAnimation = Vector3.up },
            new GyroscopeState { TargetGyroscopeAnimation = Vector3.down }));

        Assert.True(Diagnostics.AreSerializedStatesEqual(
            new PilotSeatState { IsInUse = true, MoveInputY = 1, MoveInputZ = -1, RotationInput = Vector3.one, TargetGyroscopeAnimation = Vector3.up, InternalGyroStrength = Vector3.left, LastInputReceivedTick = 10u },
            new PilotSeatState { IsInUse = true, MoveInputY = 1, MoveInputZ = -1, RotationInput = Vector3.one, TargetGyroscopeAnimation = Vector3.up, InternalGyroStrength = Vector3.right, LastInputReceivedTick = 20u }));
        Assert.False(Diagnostics.AreSerializedStatesEqual(
            new PilotSeatState { IsInUse = true, MoveInputY = 1, MoveInputZ = 0 },
            new PilotSeatState { IsInUse = true, MoveInputY = 0, MoveInputZ = 0 }));
    }
}
