using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace StellarPerformance;

internal enum Metric
{
    ServerTick, ShipParts, WorldSync, CableMesh, SignalCables, FluidCables, HullChunk, NativeSave, PlanetColliders, FluidGraphBuild,
    ClientVisuals, ShipVisuals, VisualLoading, WorldVisuals, ClientPhysics, ServerPhysics, RenderSubmission, ModUpdates, EngineLateUpdate, EngineFinishRendering,
    EngineMainThread, EngineRenderThread, EngineCameraRender, EngineRenderLoop, EngineBehaviourUpdate, EnginePhysics, EngineFrameWait, EnginePresentWait,
    FrameCpuMain, FrameCpuRender, FramePresentWait, FrameGpu, ScriptUpdateLoop, ScriptLateUpdateLoop,
    SignalEvaluation, FluidPreparation, ResourceTransfers, FluidBalance, HintsTips, ServerPostPhysics, ServerScenePrep,
    LoopTimeUpdate, LoopInitialization, LoopEarlyUpdate, LoopFixedUpdate, LoopPreUpdate, LoopUpdate, LoopPreLateUpdate, LoopPostLateUpdate,
    DefaultScenePhysics, PlanetVisuals, PlanetHotspots, PlanetStaticEnv, PlanetShaders,
    ClientPlanetPhysics, ClientShipPhysics, ClientPlayerPhysics, ServerShipLoading, ServerShipsPhysics,
    SignalGroups, SignalOperations, SignalInputClassification, TerrainTraversal, TerrainMeshUpdate, TerrainMeshDelta, TerrainNeighbors,
    DenseEnvironment, RingAsteroids, ClientSceneSimulate, ItemRouting,
    LoopFinishRendering, LoopSkinnedMeshes, LoopCanvasRect, LoopPlayerCanvases, LoopRectTransform, LoopAllRenderers, LoopPhysicsLateUpdate
}

internal enum TransformTarget
{
    TerrainCollider, ServerInteriorCollider, ClientInteriorCollider, ClientShipCollider, ClientShipVisualRoot
}

internal sealed class TimingSamples
{
    private readonly double[] _samples;
    private int _stored;
    private readonly Random _sampler = new Random(17);
    internal long Count { get; private set; }
    internal long Errors { get; private set; }
    internal double Total { get; private set; }
    internal double Maximum { get; private set; }

    internal TimingSamples(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _samples = new double[capacity];
    }

    internal void Add(double milliseconds, bool error = false)
    {
        if (milliseconds < 0 || double.IsNaN(milliseconds) || double.IsInfinity(milliseconds)) return;
        Count++;
        if (error) Errors++;
        Total += milliseconds;
        Maximum = Math.Max(Maximum, milliseconds);
        if (_stored < _samples.Length) _samples[_stored++] = milliseconds;
        else
        {
            var index = (long)(_sampler.NextDouble() * Count);
            if (index < _samples.Length) _samples[(int)index] = milliseconds;
        }
    }

    internal double[] Sorted() => _samples.Take(_stored).OrderBy(value => value).ToArray();
    internal int Stored => _stored;
    internal static double Percentile(double[] sorted, double fraction) => sorted.Length == 0 ? 0 : sorted[Math.Max(0, Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * fraction) - 1))];

    internal string Describe(string name)
    {
        if (Count == 0) return name + ": no samples";
        var sorted = Sorted();
        return string.Format(CultureInfo.InvariantCulture, "{0}: calls={1}, mean={2:F3} ms, max={3:F3} ms, p95={4:F3} ms, p99={5:F3} ms, errors={6}, stored={7}/{1}",
            name, Count, Total / Count, Maximum, Percentile(sorted, .95), Percentile(sorted, .99), Errors, Stored);
    }
}

internal sealed class CaptureData
{
    private struct SendTotals
    {
        internal long Count, Bytes, Failed, LimitExceeded, Thrown, Oversized;
        internal int MaxPayload, MaxBacking, MaxOffset;
    }

    private struct CounterPair
    {
        internal long Total, Filtered;
    }

    private readonly object _sync = new object();
    private readonly TimingSamples _frames = new TimingSamples(32768);
    private readonly TimingSamples _gcFrames = new TimingSamples(4096);
    private readonly TimingSamples _nonGcFrames = new TimingSamples(32768);
    private readonly TimingSamples[] _metrics = Enum.GetValues(typeof(Metric)).Cast<Metric>().Select(_ => new TimingSamples(8192)).ToArray();
    private readonly Dictionary<string, TimingSamples> _cameras = new Dictionary<string, TimingSamples>(StringComparer.Ordinal);
    private readonly Dictionary<string, TimingSamples> _mods = new Dictionary<string, TimingSamples>(StringComparer.Ordinal);
    private readonly Dictionary<string, TimingSamples> _renderCounts = new Dictionary<string, TimingSamples>(StringComparer.Ordinal);
    private readonly DiagnosticMode _mode;
    private readonly Dictionary<string, CounterPair> _stateUpdates = new Dictionary<string, CounterPair>(StringComparer.Ordinal);
    private readonly Dictionary<string, CounterPair> _networkQueued = new Dictionary<string, CounterPair>(StringComparer.Ordinal);
    private readonly CounterPair[] _transformWrites = new CounterPair[Enum.GetValues(typeof(TransformTarget)).Length];
    private readonly SendTotals[] _sends = new SendTotals[3];
    private readonly long[,] _sizes = new long[3, 8];
    private readonly long[,,] _results = new long[3, 8, 256];
    private bool _closed;
    private long _snapshots, _snapshotEntries, _warnings, _errors, _spaceErrors, _sendErrors, _boundsErrors;
    private long _heapGrowthBytes, _heapDrops, _buildToolCalls, _buildToolScans, _recyclerToolCalls, _recyclerToolScans;
    private long _hotspotSamples, _hotspotTotalObjects, _staticEnvSamples, _staticTotalObjects, _atmoSamples, _atmoPlanetSamples, _atmoRingSamples;
    private int _maxShips, _maxParts, _maxWorldObjects, _maxLoadedCables, _maxHotspotObjects, _maxStaticObjects;
    internal readonly string Label;

    internal CaptureData(string label, DiagnosticMode mode = DiagnosticMode.Full) { Label = label; _mode = mode; }
    internal void RenderCount(string name, long count)
    {
        if (string.IsNullOrEmpty(name) || count < 0) return;
        lock (_sync)
        {
            if (_closed) return;
            if (!_renderCounts.TryGetValue(name, out var samples))
            {
                if (_renderCounts.Count >= 8) return;
                _renderCounts[name] = samples = new TimingSamples(1024);
            }
            samples.Add(count);
        }
    }
    internal static double CaptureSeconds(double seconds) => double.IsNaN(seconds) || double.IsInfinity(seconds) ? 30 : Math.Max(5, Math.Min(120, seconds));
    internal void Frame(double milliseconds) => Frame(milliseconds, 0, false);
    internal void Frame(double milliseconds, long positiveHeapDelta, bool gcEvent)
    {
        lock (_sync)
        {
            if (_closed || milliseconds <= 0) return;
            _frames.Add(milliseconds);
            if (positiveHeapDelta > 0) _heapGrowthBytes += positiveHeapDelta;
            if (gcEvent) { _gcFrames.Add(milliseconds); _heapDrops++; }
            else _nonGcFrames.Add(milliseconds);
        }
    }

    internal void Population(int ships, int parts) { lock (_sync) if (!_closed) { _maxShips = Math.Max(_maxShips, ships); _maxParts = Math.Max(_maxParts, parts); } }
    internal void Cables(int count) { lock (_sync) if (!_closed) _maxLoadedCables = Math.Max(_maxLoadedCables, count); }
    internal void Timing(Metric metric, double milliseconds, bool error) { lock (_sync) if (!_closed) _metrics[(int)metric].Add(milliseconds, error); }
    internal void Snapshot(int count) { lock (_sync) if (!_closed) { _snapshots++; _snapshotEntries += count; _maxWorldObjects = Math.Max(_maxWorldObjects, count); } }

    internal void CameraTiming(string cameraKey, double milliseconds)
    {
        if (string.IsNullOrEmpty(cameraKey)) return;
        lock (_sync)
        {
            if (_closed) return;
            if (!_cameras.TryGetValue(cameraKey, out var samples))
            {
                if (_cameras.Count >= 16) return;
                _cameras[cameraKey] = samples = new TimingSamples(4096);
            }
            samples.Add(milliseconds);
        }
    }

    internal void ModTiming(string modId, double milliseconds, bool error)
    {
        if (string.IsNullOrEmpty(modId)) return;
        lock (_sync)
        {
            if (_closed) return;
            if (!_mods.TryGetValue(modId, out var samples))
            {
                if (_mods.Count >= 32) return;
                _mods[modId] = samples = new TimingSamples(4096);
            }
            samples.Add(milliseconds, error);
        }
    }

    internal void RecordStateUpdate(string typeName, bool unchanged)
    {
        if (string.IsNullOrEmpty(typeName)) return;
        lock (_sync)
        {
            if (_closed) return;
            if (!_stateUpdates.TryGetValue(typeName, out var pair) && _stateUpdates.Count >= 64) return;
            pair.Total++;
            if (unchanged) pair.Filtered++;
            _stateUpdates[typeName] = pair;
        }
    }

    internal void RecordNetworkQueue(string typeName, bool identicalBytes)
    {
        if (string.IsNullOrEmpty(typeName)) return;
        lock (_sync)
        {
            if (_closed) return;
            if (!_networkQueued.TryGetValue(typeName, out var pair) && _networkQueued.Count >= 64) return;
            pair.Total++;
            if (identicalBytes) pair.Filtered++;
            _networkQueued[typeName] = pair;
        }
    }

    internal void RecordTransformWrite(TransformTarget target, bool noop)
    {
        lock (_sync)
        {
            if (_closed) return;
            ref var pair = ref _transformWrites[(int)target];
            pair.Total++;
            if (noop) pair.Filtered++;
        }
    }

    internal void RecordToolRefs(bool isRecycler, bool scanned)
    {
        lock (_sync)
        {
            if (_closed) return;
            if (isRecycler) { _recyclerToolCalls++; if (scanned) _recyclerToolScans++; }
            else { _buildToolCalls++; if (scanned) _buildToolScans++; }
        }
    }

    internal void RecordHotspotObjects(int count)
    {
        if (count < 0) return;
        lock (_sync)
        {
            if (_closed) return;
            _hotspotSamples++;
            _hotspotTotalObjects += count;
            _maxHotspotObjects = Math.Max(_maxHotspotObjects, count);
        }
    }

    internal void RecordStaticObjects(int count)
    {
        if (count < 0) return;
        lock (_sync)
        {
            if (_closed) return;
            _staticEnvSamples++;
            _staticTotalObjects += count;
            _maxStaticObjects = Math.Max(_maxStaticObjects, count);
        }
    }

    internal void RecordAtmosphericPlanet(bool hasPlanet, bool hasRing)
    {
        lock (_sync)
        {
            if (_closed) return;
            _atmoSamples++;
            if (hasPlanet) _atmoPlanetSamples++;
            if (hasRing) _atmoRingSamples++;
        }
    }

    internal void Send(byte channel, int count, int offset, int backing, int result, bool thrown)
    {
        lock (_sync)
        {
            if (_closed) return;
            var index = channel < 2 ? channel : 2;
            ref var totals = ref _sends[index];
            totals.Count++;
            totals.Bytes += (long)count + 1;
            totals.MaxPayload = Math.Max(totals.MaxPayload, count);
            totals.MaxBacking = Math.Max(totals.MaxBacking, backing);
            totals.MaxOffset = Math.Max(totals.MaxOffset, offset);
            if (thrown || result != 1) totals.Failed++;
            if (result == 25 && !thrown) totals.LimitExceeded++;
            if (thrown) totals.Thrown++;
            if ((long)count + 1 > 524288) totals.Oversized++;
            var bucket = count < 1200 ? 0 : count < 4096 ? 1 : count < 16384 ? 2 : count < 65536 ? 3 : count < 262144 ? 4 : count < 524288 ? 5 : count == 524288 ? 6 : 7;
            _sizes[index, bucket]++;
            _results[index, bucket, result >= 0 && result < 255 ? result : 255]++;
        }
    }

    internal void Log(string message, bool error)
    {
        lock (_sync)
        {
            if (_closed) return;
            if (error) _errors++; else _warnings++;
            if (message.IndexOf("Space id ", StringComparison.Ordinal) >= 0) _spaceErrors++;
            if (message.IndexOf("k_EResultLimitExceeded", StringComparison.Ordinal) >= 0) _sendErrors++;
            if (message.IndexOf("IndexOutOfRangeException", StringComparison.Ordinal) >= 0 || message.IndexOf("out of bounds", StringComparison.OrdinalIgnoreCase) >= 0) _boundsErrors++;
        }
    }

    internal string Close()
    {
        lock (_sync)
        {
            _closed = true;
            var text = new StringBuilder("Stellar Performance " + PerformancePlugin.Version + " diagnostic capture: ").AppendLine(Label);
            text.AppendLine("Patched-method timings include hooks, stalls and overlapping paths. Engine* metrics are Unity recorder durations; Frame* metrics are native frame timings when available. They overlap, may include waits, and must not be summed. Missing engine/GPU data is unavailable, not zero. Quantiles use a bounded reservoir; stored/total exposes sampling.");
            text.AppendLine(_frames.Describe("Frame intervals"));
            if (_frames.Count > 0)
            {
                var sorted = _frames.Sorted();
                var slowCount = Math.Max(1, (int)Math.Ceiling(sorted.Length * .01));
                var slowMean = sorted.Skip(sorted.Length - slowCount).Average();
                text.AppendFormat(CultureInfo.InvariantCulture, "Average FPS={0:F2}, median FPS={1:F2}, 1% low={2:F2} (reciprocal mean of slowest 1% stored frame times)\n", 1000 * _frames.Count / _frames.Total, 1000 / TimingSamples.Percentile(sorted, .5), 1000 / slowMean);
            }
            foreach (Metric metric in Enum.GetValues(typeof(Metric))) text.AppendLine(_metrics[(int)metric].Describe(metric.ToString()));
            foreach (var camera in _cameras.OrderBy(pair => pair.Key, StringComparer.Ordinal)) text.AppendLine(camera.Value.Describe("Camera[" + camera.Key + "]"));
            foreach (var mod in _mods.OrderBy(pair => pair.Key, StringComparer.Ordinal)) text.AppendLine(mod.Value.Describe("Mod[" + mod.Key + "]"));
            foreach (var counter in _renderCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                text.AppendFormat(CultureInfo.InvariantCulture, "Render counter[{0}]: samples={1}, mean={2:F2}, max={3:F0}\n", counter.Key, counter.Value.Count, counter.Value.Total / counter.Value.Count, counter.Value.Maximum);
            if (_mode == DiagnosticMode.Full)
            {
                AppendCounterSummary(text, "Part state updates", "unchanged", _stateUpdates);
                AppendCounterSummary(text, "Queued network part updates", "identical-bytes", _networkQueued);
                text.Append("Transform writes (calls/no-op): ");
                var targets = (TransformTarget[])Enum.GetValues(typeof(TransformTarget));
                for (var i = 0; i < targets.Length; i++)
                {
                    var pair = _transformWrites[(int)targets[i]];
                    if (i > 0) text.Append(", ");
                    text.Append(targets[i]).Append('=').Append(pair.Total).Append('/').Append(pair.Filtered);
                }
                text.AppendLine();
                var hotspotMean = _hotspotSamples > 0 ? (double)_hotspotTotalObjects / _hotspotSamples : 0.0;
                var staticMean = _staticEnvSamples > 0 ? (double)_staticTotalObjects / _staticEnvSamples : 0.0;
                text.AppendFormat(CultureInfo.InvariantCulture, "Environment & tool counters: ToolRefs Build calls/scans={0}/{1}, Recycler calls/scans={2}/{3}; hotspot objects max={4} (mean={5:F1}, calls={6}), static objects max={7} (mean={8:F1}, calls={9}); nearest atmo planet calls={10}/{11} (ringed={12})\n",
                    _buildToolCalls, _buildToolScans, _recyclerToolCalls, _recyclerToolScans, _maxHotspotObjects, hotspotMean, _hotspotSamples, _maxStaticObjects, staticMean, _staticEnvSamples, _atmoPlanetSamples, _atmoSamples, _atmoRingSamples);
            }
            else text.AppendLine("Detailed state, network-equality, transform, environment and tool counters: unavailable (not installed in this diagnostic mode).");
            if (_mode == DiagnosticMode.Frame) text.AppendLine("Frame mode: subsystem, population, transport, save, per-frame heap and engine probes are unavailable; frame timing still includes capture bookkeeping.");
            var durationSeconds = _frames.Total > 0 ? _frames.Total / 1000.0 : 0.0;
            var kibPerFrame = _frames.Count > 0 ? _heapGrowthBytes / 1024.0 / _frames.Count : 0.0;
            var mibPerSec = durationSeconds > 0 ? _heapGrowthBytes / (1024.0 * 1024.0) / durationSeconds : 0.0;
            var gcMean = _gcFrames.Count > 0 ? _gcFrames.Total / _gcFrames.Count : 0.0;
            var nonGcMean = _nonGcFrames.Count > 0 ? _nonGcFrames.Total / _nonGcFrames.Count : 0.0;
            if (_mode == DiagnosticMode.Frame)
                text.AppendFormat(CultureInfo.InvariantCulture, "GC frame correlation: gc-event frames={0}/{1}, gc-frame mean={2:F3} ms (max={3:F3} ms), non-gc mean={4:F3} ms (max={5:F3} ms); positive heap growth=unavailable (not sampled in Frame mode)\n",
                    _gcFrames.Count, _frames.Count, gcMean, _gcFrames.Maximum, nonGcMean, _nonGcFrames.Maximum);
            else text.AppendFormat(CultureInfo.InvariantCulture, "GC frame correlation: gc-event frames={0}/{1}, gc-frame mean={2:F3} ms (max={3:F3} ms), non-gc mean={4:F3} ms (max={5:F3} ms); positive heap growth={6} bytes ({7:F2} KiB/frame, {8:F2} MiB/s across {9} heap-drop/gc frames; heap proxy, not allocated bytes)\n",
                _gcFrames.Count, _frames.Count, gcMean, _gcFrames.Maximum, nonGcMean, _nonGcFrames.Maximum, _heapGrowthBytes, kibPerFrame, mibPerSec, _heapDrops);
            text.AppendFormat(CultureInfo.InvariantCulture, "World-object snapshots={0}, entries copied={1}; warnings={2}, errors={3}, space messages={4}, send-limit messages={5}, bounds messages={6}\n", _snapshots, _snapshotEntries, _warnings, _errors, _spaceErrors, _sendErrors, _boundsErrors);
            text.AppendFormat(CultureInfo.InvariantCulture, "Observed maxima: ships={0}, parts={1} (host, sampled at most once/second), world objects={2}, loaded cables={3}\n", _maxShips, _maxParts, _maxWorldObjects, _maxLoadedCables);
            for (var i = 0; i < _sends.Length; i++)
            {
                var send = _sends[i];
                text.AppendFormat(CultureInfo.InvariantCulture, "Send channel {0}: calls={1}, attempted framed bytes={2}, failures={3}, limit-exceeded={4}, thrown={5}, max payload={6}, max backing={7}, max offset={8}; payload buckets <1200/<4KiB/<16KiB/<64KiB/<256KiB/<512KiB/=512KiB/>512KiB: ", i, send.Count, send.Bytes, send.Failed, send.LimitExceeded, send.Thrown, send.MaxPayload, send.MaxBacking, send.MaxOffset);
                for (var bucket = 0; bucket < 8; bucket++) text.Append(_sizes[i, bucket]).Append(bucket == 7 ? '\n' : '/');
                text.AppendFormat(CultureInfo.InvariantCulture, "Max framed size={0}, attempted sends exceeding Steam's 512KiB single-message limit={1}. Results by payload bucket (Steam code/count; 255=other): ", send.Count == 0 ? 0L : (long)send.MaxPayload + 1, send.Oversized);
                for (var bucket = 0; bucket < 8; bucket++)
                {
                    if (_sizes[i, bucket] == 0) continue;
                    text.Append('b').Append(bucket).Append("={");
                    for (var result = 0; result < 256; result++) if (_results[i, bucket, result] > 0) text.Append(result).Append(':').Append(_results[i, bucket, result]).Append(' ');
                    text.Append("} ");
                }
                text.AppendLine();
            }
            return text.ToString();
        }
    }

    private static void AppendCounterSummary(StringBuilder text, string title, string filteredLabel, Dictionary<string, CounterPair> map)
    {
        var total = 0L;
        var filtered = 0L;
        foreach (var pair in map.Values) { total += pair.Total; filtered += pair.Filtered; }
        text.AppendFormat(CultureInfo.InvariantCulture, "{0}: total={1}, {2}={3}", title, total, filteredLabel, filtered);
        if (map.Count > 0)
        {
            text.Append("; by type: ");
            var first = true;
            foreach (var entry in map.OrderByDescending(pair => pair.Value.Total).ThenBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!first) text.Append(", ");
                first = false;
                text.Append(entry.Key).Append('=').Append(entry.Value.Total).Append('/').Append(entry.Value.Filtered);
            }
        }
        text.AppendLine();
    }
}
