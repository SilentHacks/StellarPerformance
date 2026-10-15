using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using FishNet.Managing.Timing;
using FishNet.Serializing;
using HarmonyLib;
using Imports.CablesMesh;
using Managers.Client;
using Managers.Client.Services;
using Managers.Server;
using Managers.Server.Updaters;
using Managers.Server.Network;
using Managers.Server.Network.Serialization;
using Planets;
using Planets.EnvironmentGeneration;
using Planets.Lighting;
using Planets.Network;
using Planets.Rings;
using Planets.Services;
using Planets.Terrain;
using Planets.Terrain.Meshes;
using Saving.Services.IO;
using Ships;
using Ships.Cables;
using Ships.Cables.VisualObjects;
using Ships.Cables.Fluids;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Model.State;
using Ships.Physics;
using Ships.Physics.Model;
using Ships.Services;
using Steamworks;
using UnityEngine;
using WorldObjects.Interface.Model;
using WorldObjects.Services;
using WorldTracking.Spaces;

namespace StellarPerformance;

internal enum DiagnosticMode { Frame, Lean, Full }

// Ordinary feature paths can check this without initializing the diagnostic hooks.
internal static class DiagnosticCounters
{
    internal static bool Active;
}

internal static partial class Diagnostics
{
    internal static DiagnosticMode Mode { get; private set; } = DiagnosticMode.Lean;
    internal static DiagnosticMode ParseMode(string? value) => Enum.TryParse(value, true, out DiagnosticMode mode) && Enum.IsDefined(typeof(DiagnosticMode), mode) ? mode : DiagnosticMode.Lean;
    internal static void Configure(string? mode) => Mode = ParseMode(mode);
    internal readonly struct Stamp
    {
        internal readonly CaptureData? Capture;
        internal readonly long Started;
        internal readonly Metric Metric;
        internal Stamp(CaptureData capture, Metric metric) { Capture = capture; Metric = metric; Started = Stopwatch.GetTimestamp(); }
    }

    internal readonly struct ModStamp
    {
        internal readonly CaptureData? Capture;
        internal readonly long Started;
        internal readonly string? ModId;
        internal ModStamp(CaptureData capture, string modId) { Capture = capture; ModId = modId; Started = Stopwatch.GetTimestamp(); }
    }

    internal readonly struct PoseStamp
    {
        internal readonly CaptureData? Capture;
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;
        internal PoseStamp(CaptureData capture, Vector3 position, Quaternion rotation) { Capture = capture; Position = position; Rotation = rotation; }
    }

    internal readonly struct ToolScanStamp
    {
        internal readonly CaptureData? Capture;
        internal readonly float NextSearchBefore;
        internal ToolScanStamp(CaptureData capture, float nextSearchBefore) { Capture = capture; NextSearchBefore = nextSearchBefore; }
    }

    internal readonly struct SendStamp
    {
        internal readonly CaptureData? Capture;
        internal readonly byte Channel;
        internal readonly int Count, Offset, Backing;
        internal SendStamp(CaptureData capture, ArraySegment<byte> segment, byte channel)
        {
            Capture = capture; Channel = channel; Count = segment.Count; Offset = segment.Offset; Backing = segment.Array?.Length ?? 0;
        }
    }

    private static readonly Dictionary<MethodBase, Metric> Targets = new Dictionary<MethodBase, Metric>();
    private static Func<PlanetStaticEnvironmentVisuals, ICollection> StaticSpawnedOf = null!;
    private static Func<PlanetHotspotsEnvironmentVisuals, int> HotspotCountOf = null!;
    private static Func<object, string>? _sdModId;
    private static Func<float>? _nextBuildSearch;
    private static Func<float>? _nextRecyclerSearch;
    [ThreadStatic] private static Writer? _writerA;
    [ThreadStatic] private static Writer? _writerB;
    private static bool _writersInitialized;
    private static Func<long>? _allocatedBytes;
    private static bool _allocationResolved;
    private static volatile CaptureData? _capture;
    private static long _started, _lastFrame, _allocationStart, _lastPopulation, _lastHeap;
    private static long _reusedStart, _createdStart, _uiDeferredStart, _uiUnchangedStart;
    private static long _roomEditsStart, _roomLocalStart, _roomGeneralStart, _roomVisitsStart, _roomOutsideStart;
    private static string _rendererInventory = string.Empty;
    private static int _lastGc0;
    private static readonly int[] CollectionsStart = new int[3];
    private static double _duration;
    private static EngineTimings? _engine;
    internal static bool Active => _capture != null;

    internal static void Start(double seconds, string label)
    {
        if (_capture != null) return;
        _rendererInventory = string.Empty;
        if (Mode != DiagnosticMode.Frame)
        {
            try { _rendererInventory = RendererInventory.Capture(); }
            catch (Exception error) { _rendererInventory = "Renderer inventory unavailable: " + error.GetType().Name; }
        }
        if (!_allocationResolved) { _allocatedBytes = ResolveAllocationCounter(); _allocationResolved = true; }
        var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        var capture = new CaptureData(label + "; diagnostic-mode=" + Mode + "; render pipeline=" + (pipeline == null ? "built-in" : pipeline.GetType().FullName) + "; SRP batching=" + UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching, Mode);
        _engine = Mode == DiagnosticMode.Frame ? null : new EngineTimings(capture);
        _duration = CaptureData.CaptureSeconds(seconds);
        Application.logMessageReceived += OnLog;
        _started = _lastFrame = Stopwatch.GetTimestamp();
        _lastPopulation = _started - Stopwatch.Frequency;
        for (var generation = 0; generation < 3; generation++) CollectionsStart[generation] = GC.CollectionCount(generation);
        _lastGc0 = CollectionsStart[0];
        _lastHeap = GC.GetTotalMemory(false);
        _allocationStart = ReadAllocationCounter() ?? 0;
        _reusedStart = NativeStateReuse.Reused; _createdStart = NativeStateReuse.Created;
        _uiDeferredStart = WorldUiVisibility.Deferred; _uiUnchangedStart = WorldUiVisibility.Unchanged;
        _roomEditsStart = IncrementalRooms.Edits; _roomLocalStart = IncrementalRooms.LocalEdits;
        _roomGeneralStart = IncrementalRooms.GeneralEdits; _roomVisitsStart = IncrementalRooms.VisitedNodes; _roomOutsideStart = IncrementalRooms.OutsideExits;
        _capture = capture;
        DiagnosticCounters.Active = true;
    }

    internal static string? Update()
    {
        var capture = _capture;
        if (capture == null) return null;
        var now = Stopwatch.GetTimestamp();
        var heap = Mode == DiagnosticMode.Frame ? _lastHeap : GC.GetTotalMemory(false);
        var gc0 = GC.CollectionCount(0);
        var gcEvent = gc0 > _lastGc0 || heap < _lastHeap;
        var positiveDelta = !gcEvent && heap > _lastHeap ? heap - _lastHeap : 0L;
        _lastHeap = heap;
        _lastGc0 = gc0;
        capture.Frame(Milliseconds(now - _lastFrame), positiveDelta, gcEvent);
        _lastFrame = now;
        _engine?.Collect(capture);
        return Milliseconds(now - _started) >= _duration * 1000 ? Stop() : null;
    }

    internal static string? Stop()
    {
        var capture = _capture;
        if (capture == null) return null;
        _capture = null;
        DiagnosticCounters.Active = false;
        Application.logMessageReceived -= OnLog;
        var allocationEnd = ReadAllocationCounter();
        var allocation = allocationEnd.HasValue && allocationEnd.Value >= _allocationStart ? "Main-thread allocated bytes=" + (allocationEnd.Value - _allocationStart) + " (validated with 4096-byte positive control)" : "Main-thread allocation counter unavailable; no allocation estimate reported.";
        var gc0 = GC.CollectionCount(0) - CollectionsStart[0];
        var gc1 = GC.CollectionCount(1) - CollectionsStart[1];
        var gc2 = GC.CollectionCount(2) - CollectionsStart[2];
        _engine?.Dispose();
        _engine = null;
        return capture.Close() + allocation + "; GC collections delta=" + gc0 + "/" + gc1 + "/" + gc2 +
            "\nNative state factories: reused=" + (NativeStateReuse.Reused - _reusedStart) + ", created=" + (NativeStateReuse.Created - _createdStart) +
            "; part UI: deferred=" + (WorldUiVisibility.Deferred - _uiDeferredStart) + ", unchanged=" + (WorldUiVisibility.Unchanged - _uiUnchangedStart) +
            ", visible/hidden canvases at stop=" + WorldUiVisibility.VisibleCanvases + "/" + WorldUiVisibility.HiddenCanvases +
            "; ship batches: source/replacement renderers=" + ShipPartBatching.SourceRenderers + "/" + ShipPartBatching.ReplacementRenderers +
            "\nRoom edits: total=" + (IncrementalRooms.Edits - _roomEditsStart) + ", local=" + (IncrementalRooms.LocalEdits - _roomLocalStart) +
            ", general=" + (IncrementalRooms.GeneralEdits - _roomGeneralStart) + ", visited nodes=" + (IncrementalRooms.VisitedNodes - _roomVisitsStart) +
            ", exterior exits=" + (IncrementalRooms.OutsideExits - _roomOutsideStart) + "\n" + _rendererInventory;
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            _capture?.Log(message, type != LogType.Warning);
    }

    private static double Milliseconds(long ticks) => ticks * (1000.0 / Stopwatch.Frequency);
    private static Func<long>? ResolveAllocationCounter()
    {
        var method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static);
        if (method == null) return null;
        return ValidateAllocationCounter((Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method));
    }

    internal static Func<long>? ValidateAllocationCounter(Func<long> counter)
    {
        try
        {
            var before = counter();
            var probe = new byte[4096];
            var after = counter();
            GC.KeepAlive(probe);
            return before >= 0 && after >= before && after - before >= probe.Length ? counter : null;
        }
        catch (NotSupportedException) { return null; }
        catch (NotImplementedException) { return null; }
    }

    private static long? ReadAllocationCounter()
    {
        try
        {
            var value = _allocatedBytes?.Invoke();
            if (value >= 0) return value;
        }
        catch (NotSupportedException) { }
        catch (NotImplementedException) { }
        _allocatedBytes = null;
        return null;
    }
}
