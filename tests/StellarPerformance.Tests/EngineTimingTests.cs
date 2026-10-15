using System;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Managers.Client;
using Managers.Client.Services;
using Managers.Server;
using Managers.Server.Updaters;
using UnityEngine;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class EngineTimingTests
{
    [Fact]
    public void NativeFrameTimingsAreDeduplicatedAndUnavailableGpuIsNotZero()
    {
        var capture = new CaptureData("engine");
        ulong timestamp = 0;
        var frame = new FrameTiming { frameStartTimestamp = 7, cpuMainThreadFrameTime = 12, cpuRenderThreadFrameTime = 3, cpuMainThreadPresentWaitTime = 0, gpuFrameTime = 0 };
        EngineTimings.RecordFrame(capture, frame, ref timestamp);
        EngineTimings.RecordFrame(capture, frame, ref timestamp);
        frame.frameStartTimestamp = 0;
        EngineTimings.RecordFrame(capture, frame, ref timestamp);
        var report = capture.Close();
        Assert.Contains("FrameCpuMain: calls=1, mean=12.000 ms", report);
        Assert.Contains("FrameCpuRender: calls=1, mean=3.000 ms", report);
        Assert.Contains("FrameGpu: no samples", report);
        Assert.Contains("FramePresentWait: no samples", report);
    }

    [Fact]
    public void InvalidEngineTimingsNeverPoisonTheReport()
    {
        var capture = new CaptureData("engine");
        ulong timestamp = 0;
        EngineTimings.RecordFrame(capture, new FrameTiming { frameStartTimestamp = 1, gpuFrameTime = double.NaN, cpuMainThreadFrameTime = double.PositiveInfinity, cpuRenderThreadFrameTime = -1 }, ref timestamp);
        EngineTimings.RecordNanoseconds(capture, Metric.EngineMainThread, -1);
        EngineTimings.RecordNanoseconds(capture, Metric.EngineMainThread, 2500000);
        var report = capture.Close();
        Assert.Contains("EngineMainThread: calls=1, mean=2.500 ms", report);
        Assert.Contains("FrameGpu: no samples", report);
    }

    [Fact]
    public void AggregateLoopTimingPreservesNativeDelegatesPointersAndOtherMods()
    {
        var calls = new System.Collections.Generic.List<int>();
        UnityEngine.LowLevel.PlayerLoopSystem.UpdateFunction update = () => calls.Add(2), late = () => calls.Add(5), other = () => calls.Add(8);
        var loop = new UnityEngine.LowLevel.PlayerLoopSystem
        {
            type = typeof(UnityEngine.PlayerLoop.Update),
            subSystemList = new[]
            {
                new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate), updateDelegate = update, updateFunction = new IntPtr(17), loopConditionFunction = new IntPtr(29) },
                new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate), updateDelegate = late }
            }
        };
        var wrapped = EngineTimings.RewriteLoop(loop, true, () => calls.Add(1), () => calls.Add(3), () => calls.Add(4), () => calls.Add(6), out var updates, out var lates);
        Assert.Equal(1, updates);
        Assert.Equal(1, lates);
        foreach (var child in wrapped.subSystemList) child.updateDelegate();
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, calls);
        Assert.Equal(new IntPtr(17), wrapped.subSystemList[1].updateFunction);
        Assert.Equal(new IntPtr(29), wrapped.subSystemList[1].loopConditionFunction);
        Assert.Equal(2, loop.subSystemList.Length);
        wrapped.subSystemList = wrapped.subSystemList.Append(new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(EngineTimingTests), updateDelegate = other }).ToArray();
        var restored = EngineTimings.RewriteLoop(wrapped, false, null, null, null, null, out _, out _);
        Assert.Equal(3, restored.subSystemList.Length);
        Assert.Same(update, restored.subSystemList[0].updateDelegate);
        Assert.Same(late, restored.subSystemList[1].updateDelegate);
        Assert.Same(other, restored.subSystemList[2].updateDelegate);
        Assert.Equal(new IntPtr(17), restored.subSystemList[0].updateFunction);
    }

    [Fact]
    public void AggregateLoopTimingDoesNotAccumulateAndReportsMissingBoundaries()
    {
        var loop = new UnityEngine.LowLevel.PlayerLoopSystem { subSystemList = new[] { new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate) } } };
        var wrapped = EngineTimings.RewriteLoop(loop, true, () => { }, () => { }, () => { }, () => { }, out var updates, out var lates);
        Assert.Equal(1, updates);
        Assert.Equal(0, lates);
        var repeated = EngineTimings.RewriteLoop(wrapped, true, () => { }, () => { }, () => { }, () => { }, out _, out _);
        Assert.Equal(wrapped.subSystemList.Length, repeated.subSystemList.Length);
        var restored = EngineTimings.RewriteLoop(repeated, false, null, null, null, null, out _, out _);
        Assert.Single(restored.subSystemList);
    }

    [Fact]
    public void FocusedLoopBracketsPreservePointersAndOtherModDelegatesWithoutAccumulating()
    {
        var capture = new CaptureData("focused");
        var starts = new long[EngineTimings.FocusedPhases.Length];
        var calls = 0;
        UnityEngine.LowLevel.PlayerLoopSystem.UpdateFunction native = () => calls++;
        var loop = new UnityEngine.LowLevel.PlayerLoopSystem
        {
            type = typeof(UnityEngine.PlayerLoop.PostLateUpdate),
            subSystemList = new[]
            {
                new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(UnityEngine.PlayerLoop.PostLateUpdate.FinishFrameRendering), updateDelegate = native, updateFunction = new IntPtr(73), loopConditionFunction = new IntPtr(91) },
                new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(EngineTimingTests), updateDelegate = native }
            }
        };
        var wrapped = EngineTimings.RewriteFocusedPhases(loop, true, capture, starts, out var count);
        Assert.Equal(1, count);
        foreach (var child in wrapped.subSystemList) child.updateDelegate();
        Assert.Equal(2, calls);
        Assert.Contains("LoopFinishRendering: calls=1", capture.Close());
        var repeated = EngineTimings.RewriteFocusedPhases(wrapped, true, capture, starts, out count);
        Assert.Equal(1, count);
        Assert.Equal(wrapped.subSystemList.Length, repeated.subSystemList.Length);
        var restored = EngineTimings.RewriteFocusedPhases(repeated, false, null, null, out _);
        Assert.Equal(2, restored.subSystemList.Length);
        Assert.Same(native, restored.subSystemList[0].updateDelegate);
        Assert.Equal(new IntPtr(73), restored.subSystemList[0].updateFunction);
        Assert.Equal(new IntPtr(91), restored.subSystemList[0].loopConditionFunction);
        Assert.Same(native, restored.subSystemList[1].updateDelegate);
    }

    private static float _simulationStep;
    private static Exception? _simulationError;
    private static void StubSimulate(ref PhysicsScene scene, float deltaTime)
    {
        _simulationStep = deltaTime;
        if (_simulationError != null) throw _simulationError;
    }

    [Fact]
    public void ClientPhysicsProbePreservesTheStepAndNativeExceptions()
    {
        var run = ManagedMethod.Clone(AccessTools.Method(typeof(Diagnostics), nameof(Diagnostics.SimulateClientScene)), (codes, _) => codes.Select(source =>
        {
            var code = new CodeInstruction(source);
            if (code.Calls(AccessTools.Method(typeof(PhysicsScene), nameof(PhysicsScene.Simulate), new[] { typeof(float) })))
            {
                code.opcode = OpCodes.Call;
                code.operand = AccessTools.Method(typeof(EngineTimingTests), nameof(StubSimulate));
            }
            return code;
        }));
        var capture = new CaptureData("simulate");
        var field = AccessTools.Field(typeof(Diagnostics), "_capture");
        field.SetValue(null, capture);
        try
        {
            _simulationError = null;
            run.DynamicInvoke(new object[] { default(PhysicsScene), 0.02f });
            Assert.Equal(0.02f, _simulationStep);
            _simulationError = new InvalidOperationException("native");
            var thrown = Assert.Throws<System.Reflection.TargetInvocationException>(() => run.DynamicInvoke(new object[] { default(PhysicsScene), 0.04f }));
            Assert.Same(_simulationError, thrown.InnerException);
            Assert.Equal(0.04f, _simulationStep);
        }
        finally { field.SetValue(null, null); _simulationError = null; }
        var report = capture.Close();
        Assert.Contains("ClientSceneSimulate: calls=2", report);
        Assert.Contains("errors=1, stored=2/2", report);
    }

    [Fact]
    public void ClientPhysicsProbeReplacesExactlyOneNativeSimulateCall()
    {
        var method = AccessTools.Method(typeof(WorldPhysicsClientUpdater), "PrePhysics");
        var original = ManagedMethod.Instructions(method);
        var rewritten = Diagnostics.TranspileClientSimulate(original).ToArray();
        Assert.Equal(original.Count, rewritten.Length);
        Assert.Single(rewritten, code => code.Calls(AccessTools.Method(typeof(Diagnostics), nameof(Diagnostics.SimulateClientScene))));
        Assert.Equal(typeof(PhysicsScene).MakeByRefType(), AccessTools.Method(typeof(Diagnostics), nameof(Diagnostics.SimulateClientScene)).GetParameters()[0].ParameterType);
        Assert.Throws<InvalidOperationException>(() => Diagnostics.TranspileClientSimulate(Array.Empty<CodeInstruction>()).ToArray());
    }

    [Fact]
    public void FocusedTerrainProbesAreAggregateAndMissingLoopSystemsStayUnavailable()
    {
        foreach (var target in new[]
        {
            AccessTools.Method(typeof(Planets.Terrain.PlanetTreeTraversor), "TraverseQuadTree"),
            AccessTools.Method(typeof(Planets.Terrain.Meshes.PlanetMeshUpdater), "FindMeshesDelta"),
            AccessTools.Method(typeof(Planets.Terrain.Meshes.PlanetMeshUpdater), "NeighboursCorrectionPass")
        }) Assert.NotNull(target);
        var capture = new CaptureData("missing");
        var loop = new UnityEngine.LowLevel.PlayerLoopSystem { subSystemList = new[] { new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(EngineTimingTests) } } };
        EngineTimings.RewriteFocusedPhases(loop, true, capture, new long[EngineTimings.FocusedPhases.Length], out var count);
        Assert.Equal(0, count);
        Assert.Contains("LoopFinishRendering: no samples", capture.Close());
    }

    [Fact]
    public void RemainingFrameProbesUseAggregateManagedBoundaries()
    {
        var render = AccessTools.Method(typeof(UnityEngine.Rendering.RenderPipelineManager), "DoRenderLoop_Internal");
        Assert.NotNull(render);
        Assert.NotNull(render.GetMethodBody());
        if (OptionalModTests.Enabled)
            Assert.NotNull(AccessTools.Method(System.Reflection.Assembly.Load("SDModKit").GetType("SDModKit.Plugin", true), "OnUpdate"));
        Assert.NotNull(AccessTools.Method(typeof(Diagnostics), "InstallRendering"));
        Assert.NotNull(AccessTools.Method(typeof(Diagnostics), "InstallModUpdates"));
    }

    [Fact]
    public void ClientProbeNamesExistAndDetailedPerNodeHooksAreNotInstalled()
    {
        Assert.NotNull(AccessTools.Method(typeof(ClientVisualsUpdater), "Update"));
        Assert.NotNull(AccessTools.Method(typeof(ClientVisualsUpdater), "UpdateShipsVisuals"));
        Assert.NotNull(AccessTools.Method(typeof(ClientVisualsUpdater), "UpdateLoadedVisualShips"));
        Assert.NotNull(AccessTools.Method(typeof(ClientWorldVisualsUpdater), "UpdateWorldVisuals"));
        Assert.NotNull(AccessTools.Method(typeof(WorldPhysicsClientUpdater), "PrePhysics"));
        Assert.NotNull(AccessTools.Method(typeof(PhysicsScenesServerUpdater), "SimulatePhysics"));
        Assert.DoesNotContain(ManagedMethod.Instructions(AccessTools.Method(typeof(Diagnostics), nameof(Diagnostics.InstallCables))), code => code.opcode == OpCodes.Ldstr && Equals(code.operand, "RecurseBuildNode"));
    }
}
