using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using UnityEngine;

namespace StellarPerformance;

internal sealed class EngineTimings : IDisposable
{
    private readonly struct Counter
    {
        internal readonly Metric Metric;
        internal readonly ProfilerRecorder Recorder;
        internal Counter(Metric metric, ProfilerRecorder recorder) { Metric = metric; Recorder = recorder; }
    }

    private readonly struct RenderCounter
    {
        internal readonly string Name;
        internal readonly ProfilerRecorder Recorder;
        internal RenderCounter(string name, ProfilerRecorder recorder) { Name = name; Recorder = recorder; }
    }

    internal static readonly (Type PhaseType, Metric Metric)[] TopLevelPhases = new[]
    {
        (typeof(UnityEngine.PlayerLoop.TimeUpdate), Metric.LoopTimeUpdate),
        (typeof(UnityEngine.PlayerLoop.Initialization), Metric.LoopInitialization),
        (typeof(UnityEngine.PlayerLoop.EarlyUpdate), Metric.LoopEarlyUpdate),
        (typeof(UnityEngine.PlayerLoop.FixedUpdate), Metric.LoopFixedUpdate),
        (typeof(UnityEngine.PlayerLoop.PreUpdate), Metric.LoopPreUpdate),
        (typeof(UnityEngine.PlayerLoop.Update), Metric.LoopUpdate),
        (typeof(UnityEngine.PlayerLoop.PreLateUpdate), Metric.LoopPreLateUpdate),
        (typeof(UnityEngine.PlayerLoop.PostLateUpdate), Metric.LoopPostLateUpdate)
    };

    internal static readonly (string PhaseName, Metric Metric)[] FocusedPhases = new[]
    {
        ("UnityEngine.PlayerLoop.PostLateUpdate+FinishFrameRendering", Metric.LoopFinishRendering),
        ("UnityEngine.PlayerLoop.PostLateUpdate+UpdateAllSkinnedMeshes", Metric.LoopSkinnedMeshes),
        ("UnityEngine.PlayerLoop.PostLateUpdate+UpdateCanvasRectTransform", Metric.LoopCanvasRect),
        ("UnityEngine.PlayerLoop.PostLateUpdate+PlayerUpdateCanvases", Metric.LoopPlayerCanvases),
        ("UnityEngine.PlayerLoop.PostLateUpdate+UpdateRectTransform", Metric.LoopRectTransform),
        ("UnityEngine.PlayerLoop.PostLateUpdate+UpdateAllRenderers", Metric.LoopAllRenderers),
        ("UnityEngine.PlayerLoop.PreLateUpdate+PhysicsLateUpdate", Metric.LoopPhysicsLateUpdate)
    };

    private static readonly ConditionalWeakTable<Camera, string> CameraLabels = new ConditionalWeakTable<Camera, string>();
    private readonly List<Counter> _counters = new List<Counter>(8);
    private readonly List<RenderCounter> _renderCounters = new List<RenderCounter>(4);
    private readonly FrameTiming[] _frame = new FrameTiming[1];
    private readonly long[] _phaseStarts = new long[TopLevelPhases.Length];
    private readonly long[] _focusedStarts = new long[FocusedPhases.Length];
    private readonly Action<ScriptableRenderContext, Camera> _beginCamera;
    private readonly Action<ScriptableRenderContext, Camera> _endCamera;
    private readonly bool _frameTiming;
    private ulong _lastTimestamp;
    private long _cameraStarted;
    private readonly bool _loopInstalled;
    private struct UpdateStart { }
    private struct UpdateEnd { }
    private struct LateStart { }
    private struct LateEnd { }
    private struct PhaseStart { }
    private struct PhaseEnd { }
    private struct FocusedStart { }
    private struct FocusedEnd { }

    internal EngineTimings(CaptureData capture)
    {
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        long update = 0, late = 0;
        var wrapped = RewriteLoop(loop, true, () => update = Stopwatch.GetTimestamp(), () => RecordLoop(capture, Metric.ScriptUpdateLoop, update), () => late = Stopwatch.GetTimestamp(), () => RecordLoop(capture, Metric.ScriptLateUpdateLoop, late), out var updates, out var lates);
        wrapped = RewriteTopLevelPhases(wrapped, true, capture, _phaseStarts, out var phases);
        wrapped = RewriteFocusedPhases(wrapped, true, capture, _focusedStarts, out var focused);
        if ((updates == 1 && lates == 1) || phases > 0 || focused > 0) { PlayerLoop.SetPlayerLoop(wrapped); _loopInstalled = true; }
        _beginCamera = (_, camera) => _cameraStarted = Stopwatch.GetTimestamp();
        _endCamera = (_, camera) =>
        {
            if (_cameraStarted == 0) return;
            var elapsed = (Stopwatch.GetTimestamp() - _cameraStarted) * (1000.0 / Stopwatch.Frequency);
            _cameraStarted = 0;
            capture.CameraTiming(DescribeCamera(camera), elapsed);
        };
        RenderPipelineManager.beginCameraRendering += _beginCamera;
        RenderPipelineManager.endCameraRendering += _endCamera;
        Add(ProfilerCategory.Internal, "Main Thread", Metric.EngineMainThread);
        Add(ProfilerCategory.Internal, "Render Thread", Metric.EngineRenderThread);
        Add(ProfilerCategory.Render, "Camera.Render", Metric.EngineCameraRender);
        Add(ProfilerCategory.Render, "RenderLoop.Draw", Metric.EngineRenderLoop);
        Add(ProfilerCategory.Internal, "Update.ScriptRunBehaviourUpdate", Metric.EngineBehaviourUpdate);
        Add(ProfilerCategory.Internal, "PreLateUpdate.ScriptRunBehaviourLateUpdate", Metric.EngineLateUpdate);
        Add(ProfilerCategory.Internal, "PostLateUpdate.FinishFrameRendering", Metric.EngineFinishRendering);
        Add(ProfilerCategory.Physics, "Physics.Simulate", Metric.EnginePhysics);
        Add(ProfilerCategory.Internal, "WaitForTargetFPS", Metric.EngineFrameWait);
        Add(ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread", Metric.EnginePresentWait);
        foreach (var name in new[] { "Draw Calls Count", "Batches Count", "SetPass Calls Count", "Triangles Count" }) AddRenderCounter(name);
        try { _frameTiming = FrameTimingManager.IsFeatureEnabled(); }
        catch (NotSupportedException) { }
    }

    internal static string DescribeCamera(Camera? camera)
    {
        if (ReferenceEquals(camera, null)) return "null";
        return CameraLabels.GetValue(camera, static cam =>
        {
            try
            {
                var name = string.IsNullOrEmpty(cam.name) ? "Unnamed" : cam.name;
                return string.Format(CultureInfo.InvariantCulture, "{0} (far={1:F1})", name, cam.farClipPlane);
            }
            catch
            {
                return cam.GetType().Name;
            }
        });
    }

    private void Add(ProfilerCategory category, string name, Metric metric)
    {
        try
        {
            var recorder = ProfilerRecorder.StartNew(category, name, 1, ProfilerRecorderOptions.Default);
            if (recorder.Valid && recorder.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds) _counters.Add(new Counter(metric, recorder));
            else recorder.Dispose();
        }
        catch (ArgumentException) { }
        catch (NotSupportedException) { }
        catch (InvalidOperationException) { }
    }

    private void AddRenderCounter(string name)
    {
        try
        {
            var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, name, 1, ProfilerRecorderOptions.Default);
            if (recorder.Valid && recorder.UnitType == ProfilerMarkerDataUnit.Count) _renderCounters.Add(new RenderCounter(name, recorder));
            else recorder.Dispose();
        }
        catch (ArgumentException) { }
        catch (NotSupportedException) { }
        catch (InvalidOperationException) { }
    }

    internal void Collect(CaptureData capture)
    {
        foreach (var counter in _counters)
            if (counter.Recorder.Valid && counter.Recorder.Count > 0) RecordNanoseconds(capture, counter.Metric, counter.Recorder.LastValue);
        foreach (var counter in _renderCounters)
            if (counter.Recorder.Valid && counter.Recorder.Count > 0) capture.RenderCount(counter.Name, counter.Recorder.LastValue);
        if (!_frameTiming) return;
        if (FrameTimingManager.GetLatestTimings(1, _frame) > 0) RecordFrame(capture, _frame[0], ref _lastTimestamp);
        FrameTimingManager.CaptureFrameTimings();
    }

    internal static void RecordNanoseconds(CaptureData capture, Metric metric, long nanoseconds)
    {
        if (nanoseconds >= 0) capture.Timing(metric, nanoseconds / 1000000.0, false);
    }

    internal static void RecordFrame(CaptureData capture, FrameTiming frame, ref ulong lastTimestamp)
    {
        if (frame.frameStartTimestamp == 0 || frame.frameStartTimestamp <= lastTimestamp) return;
        lastTimestamp = frame.frameStartTimestamp;
        Record(capture, Metric.FrameCpuMain, frame.cpuMainThreadFrameTime);
        Record(capture, Metric.FrameCpuRender, frame.cpuRenderThreadFrameTime);
        Record(capture, Metric.FramePresentWait, frame.cpuMainThreadPresentWaitTime);
        Record(capture, Metric.FrameGpu, frame.gpuFrameTime);
    }

    private static void Record(CaptureData capture, Metric metric, double value)
    {
        if (value > 0 && !double.IsInfinity(value) && !double.IsNaN(value)) capture.Timing(metric, value, false);
    }

    private static void RecordLoop(CaptureData capture, Metric metric, long started)
    {
        if (started != 0) capture.Timing(metric, (Stopwatch.GetTimestamp() - started) * (1000.0 / Stopwatch.Frequency), false);
    }

    internal static PlayerLoopSystem RewriteTopLevelPhases(PlayerLoopSystem loop, bool add, CaptureData? capture, long[]? phaseStarts, out int wrappedPhases)
    {
        wrappedPhases = 0;
        if (loop.subSystemList == null) return loop;
        var topChildren = new PlayerLoopSystem[loop.subSystemList.Length];
        for (var i = 0; i < loop.subSystemList.Length; i++)
        {
            var phase = loop.subSystemList[i];
            if (phase.subSystemList == null)
            {
                topChildren[i] = phase;
                continue;
            }
            var inner = new List<PlayerLoopSystem>(phase.subSystemList.Length + 2);
            var phaseSlot = -1;
            for (var p = 0; p < TopLevelPhases.Length; p++)
            {
                if (phase.type == TopLevelPhases[p].PhaseType) { phaseSlot = p; break; }
            }
            if (add && phaseSlot >= 0 && capture != null && phaseStarts != null)
            {
                var slot = phaseSlot;
                inner.Add(new PlayerLoopSystem { type = typeof(PhaseStart), updateDelegate = () => phaseStarts[slot] = Stopwatch.GetTimestamp() });
            }
            foreach (var child in phase.subSystemList)
            {
                if (child.type == typeof(PhaseStart) || child.type == typeof(PhaseEnd)) continue;
                inner.Add(child);
            }
            if (add && phaseSlot >= 0 && capture != null && phaseStarts != null && inner.Count > 1)
            {
                var slot = phaseSlot;
                var metric = TopLevelPhases[slot].Metric;
                inner.Add(new PlayerLoopSystem { type = typeof(PhaseEnd), updateDelegate = () => RecordLoop(capture, metric, phaseStarts[slot]) });
                wrappedPhases++;
            }
            else if (add && phaseSlot >= 0 && inner.Count == 1 && inner[0].type == typeof(PhaseStart))
            {
                inner.Clear();
            }
            phase.subSystemList = inner.ToArray();
            topChildren[i] = phase;
        }
        loop.subSystemList = topChildren;
        return loop;
    }

    internal static PlayerLoopSystem RewriteFocusedPhases(PlayerLoopSystem loop, bool add, CaptureData? capture, long[]? starts, out int wrappedPhases)
    {
        wrappedPhases = 0;
        if (loop.subSystemList == null) return loop;
        var children = new List<PlayerLoopSystem>(loop.subSystemList.Length + FocusedPhases.Length * 2);
        foreach (var child in loop.subSystemList)
        {
            if (child.type == typeof(FocusedStart) || child.type == typeof(FocusedEnd)) continue;
            var updated = RewriteFocusedPhases(child, add, capture, starts, out var nested);
            wrappedPhases += nested;
            var slot = -1;
            for (var i = 0; i < FocusedPhases.Length; i++)
                if (child.type?.FullName == FocusedPhases[i].PhaseName) { slot = i; break; }
            if (add && slot >= 0 && capture != null && starts != null)
            {
                var index = slot;
                children.Add(new PlayerLoopSystem { type = typeof(FocusedStart), updateDelegate = () => starts[index] = Stopwatch.GetTimestamp() });
                children.Add(updated);
                children.Add(new PlayerLoopSystem { type = typeof(FocusedEnd), updateDelegate = () => RecordLoop(capture, FocusedPhases[index].Metric, starts[index]) });
                wrappedPhases++;
            }
            else children.Add(updated);
        }
        loop.subSystemList = children.ToArray();
        return loop;
    }

    internal static PlayerLoopSystem RewriteLoop(PlayerLoopSystem loop, bool add, PlayerLoopSystem.UpdateFunction? updateStart, PlayerLoopSystem.UpdateFunction? updateEnd, PlayerLoopSystem.UpdateFunction? lateStart, PlayerLoopSystem.UpdateFunction? lateEnd, out int updates, out int lates)
    {
        updates = lates = 0;
        if (loop.subSystemList == null) return loop;
        var children = new List<PlayerLoopSystem>(loop.subSystemList.Length + 4);
        foreach (var child in loop.subSystemList)
        {
            if (child.type == typeof(UpdateStart) || child.type == typeof(UpdateEnd) || child.type == typeof(LateStart) || child.type == typeof(LateEnd)) continue;
            var updated = RewriteLoop(child, add, updateStart, updateEnd, lateStart, lateEnd, out var nestedUpdates, out var nestedLates);
            updates += nestedUpdates;
            lates += nestedLates;
            var update = child.type == typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate);
            var late = child.type == typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate);
            if (update) updates++;
            if (late) lates++;
            if (add && (update || late)) children.Add(new PlayerLoopSystem { type = update ? typeof(UpdateStart) : typeof(LateStart), updateDelegate = update ? updateStart : lateStart });
            children.Add(updated);
            if (add && (update || late)) children.Add(new PlayerLoopSystem { type = update ? typeof(UpdateEnd) : typeof(LateEnd), updateDelegate = update ? updateEnd : lateEnd });
        }
        loop.subSystemList = children.ToArray();
        return loop;
    }

    public void Dispose()
    {
        RenderPipelineManager.beginCameraRendering -= _beginCamera;
        RenderPipelineManager.endCameraRendering -= _endCamera;
        if (_loopInstalled)
        {
            var current = PlayerLoop.GetCurrentPlayerLoop();
            current = RewriteFocusedPhases(current, false, null, null, out _);
            current = RewriteTopLevelPhases(current, false, null, null, out _);
            current = RewriteLoop(current, false, null, null, null, null, out _, out _);
            PlayerLoop.SetPlayerLoop(current);
        }
        foreach (var counter in _counters) counter.Recorder.Dispose();
        foreach (var counter in _renderCounters) counter.Recorder.Dispose();
        _counters.Clear();
        _renderCounters.Clear();
    }
}
