using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using MelonLoader;

[assembly: MelonInfo(typeof(StellarPerformance.PerformancePlugin), "Stellar Performance", StellarPerformance.PerformancePlugin.Version, "SilentHacks", null)]
[assembly: MelonGame("CuriousOwlGames", "StellarDrive")]
[assembly: HarmonyDontPatchAll]

namespace StellarPerformance;

public sealed class PerformancePlugin : MelonMod
{
    internal const string Version = "2.1.0";
    private readonly List<string> _installed = new List<string>();

    public override void OnLateInitializeMelon()
    {
        var preferences = MelonPreferences.CreateCategory("StellarPerformance", "Stellar Performance");
        var bounds = preferences.CreateEntry("ShipGridBounds", true, description: "Cache exact ship grid bounds; invalidate on native grid mutations. Restart to change.");
        var fluids = preferences.CreateEntry("FluidCableTopology", true, description: "Cache the fluid cable visual graph. Recompute live flow and native presentation every frame. Restart to change.");
        var rooms = preferences.CreateEntry("IncrementalRooms", true, description: "Rebuild edited air regions locally, retaining native wall/gas handling and valid saved coordinates. Gas rounding can differ slightly. Restart to change.");
        var singlePlayer = preferences.CreateEntry("SinglePlayerRooms", true, description: "Forward single-player host room queries to authority; rebuild the local mirror for peers/debug views. Restart to change.");
        var batching = preferences.CreateEntry("ShipPartBatching", true, description: "Batch inspected fixed opaque meshes; use native renderers during interaction, then recover unchanged geometry. Restart to change.");
        var continuous = preferences.CreateEntry("BatchingContinuousValidation", false, description: "Compatibility option: inspect every batch source each frame. Off retains inspected native event hooks and excludes unknown prefab scripts. Restart to change.");
        var distillers = preferences.CreateEntry("DistillerReplication", true, description: "Suppress consecutive distiller notifications with identical native payloads. Server production and callbacks retain native behavior. Restart to change.");
        var replication = preferences.CreateEntry("PartReplicationDeduplication", true, description: "Suppress identical native payloads for inspected machines, gyro/pilot bookkeeping, sensors, signals, storage and warp timers. Preserve native calculations, callbacks and genuine encoded changes. Restart to change.");
        var reliable = preferences.CreateEntry("ReliableTransport", false, description: "Experimental: retain and retry Steam reliable packets during temporary congestion, with bounded queues and disconnect on sustained overload. Restart to change.");
        var capture = preferences.CreateEntry("NetworkCaptureSeconds", 0, description: "Explicit diagnostic capture at startup, up to 600 seconds. Zero disables observers and counters. Writes a CSV in UserData; restart to change.");
        var signalScan = preferences.CreateEntry("SignalConnectionScan", true, description: "Reuse exact signal connection unions while their socket sets are unchanged. Native signal values and circuit callbacks remain live. Restart to change.");
        var signalReduction = preferences.CreateEntry("SignalReduction", true, description: "Fuse inspected native signal endpoint discovery and reduction, preserving exact values, order and native tick/callback rates. Unknown states retain native evaluation. Restart to change.");
        var powerScan = preferences.CreateEntry("PowerTransferScan", true, description: "Avoid duplicate power-cable visits accumulated by native topology rebuilds. Preserve native power values, first-occurrence order and inspected notifications. Restart to change.");
        var spatialClustering = preferences.CreateEntry("SpatialClustering", true, description: "Replace large surface-space adjacency graphs with an exact ordered spatial search. Preserve native distance tests, grouping, transfers and simulation cadence. Small or unsupported inputs retain native clustering. Restart to change.");
        var emptyWarp = preferences.CreateEntry("EmptyWarpUpdates", true, description: "Avoid no-op warp and grounding work for inspected ships with no warp-drive parts. Requires the inspected Foundations version and native providers; real warp drives retain their current updates. Restart to change.");
        var saveSerialization = preferences.CreateEntry("SaveSerialization", true, description: "Reuse exact unchanged hull JSON and native serializer contracts to reduce save stalls. Preserve native save flow and format; custom JSON policies fall back and replacement save writers retain their own path. Restart to change.");
        var canvases = preferences.CreateEntry("CanvasCulling", true, description: "Hide inspected offscreen ship-part canvases before rendering; retain native readout updates and focused controls. Restart to change.");
        Install("ship-grid-bounds", bounds.Value, h => ShipGridBounds.Install(h), "Ships", "Ships.Interface", "UnityEngine.CoreModule");
        Install("fluid-cable-topology", fluids.Value, h => FluidCableTopology.Install(h), "Ships", "Ships.Interface", "UnityEngine.CoreModule");
        Presets.Initialize(preferences, rooms, !rooms.Value || !bounds.Value || !fluids.Value || !singlePlayer.Value || !batching.Value || continuous.Value || !distillers.Value || !replication.Value || !signalScan.Value || !canvases.Value);
        Install("incremental-rooms", true, h => IncrementalRooms.Install(h), "Ships", "Ships.Interface", "UnityEngine.CoreModule");
        Presets.RoomsAvailable = _installed.Contains("incremental-rooms");
        if (Presets.RoomsAvailable) IncrementalRooms.Enabled = Presets.Current.Rooms;
        Install("single-player-rooms", singlePlayer.Value, h => { SinglePlayerRooms.Log = message => LoggerInstance.Msg(message); SinglePlayerRooms.Install(h); }, "Ships", "Ships.Interface", "Core", "FishNet.Runtime", "UnityEngine.CoreModule");
        Install("ship-part-batching", batching.Value, h =>
        {
            ShipPartBatching.ContinuousValidation = continuous.Value;
            ShipPartBatching.Log = message => LoggerInstance.Msg(message);
            ShipPartBatching.Install(h);
        }, "Ships", "Ships.Interface", "Core", "Players", "UI", "UnityEngine.CoreModule", "Unity.TextMeshPro");
        Install("replication-deduplication", distillers.Value || replication.Value, h =>
        {
            ReplicationDeduplication.Distillers = distillers.Value;
            ReplicationDeduplication.OtherParts = replication.Value;
            ReplicationDeduplication.Install(h);
        }, "Assembly-CSharp", "Ships.Interface", "Players", "Items.Model", "FishNet.Runtime", "UnityEngine.CoreModule");
        Install("signal-connection-scan", signalScan.Value, SignalConnectionScan.Install, "Ships", "Ships.Interface");
        Install("signal-reduction", signalReduction.Value, SignalReduction.Install, "Ships", "Ships.Interface");
        Install("power-transfer-scan", powerScan.Value, PowerTransferScan.Install, "Assembly-CSharp", "Ships", "Ships.Interface");
        Install("spatial-clustering", spatialClustering.Value, SpatialClustering.Install, "WorldTracking", "Core");
        // Foundations installs its wrapper later in the loader's initialization
        // sequence. Wait for the first update, after every mod has initialized.
        _installEmptyWarp = emptyWarp.Value;
        if (!emptyWarp.Value) LoggerInstance.Msg("empty-warp-updates disabled by configuration.");
        Install("save-serialization", saveSerialization.Value, SaveSerialization.Install, "Saving", "Saving.Serialization", "Newtonsoft.Json", "Core");
        Install("canvas-culling", canvases.Value, h => { CanvasCulling.Log = message => LoggerInstance.Warning(message); CanvasCulling.Install(h); }, "Ships", "Ships.Interface", "Players", "Core", "UI", "UnityEngine.CoreModule", "UnityEngine.UI", "UnityEngine.UIModule", "Unity.TextMeshPro");
        Install("reliable-transport", reliable.Value, h => { ReliableTransport.Log = message => LoggerInstance.Warning(message); ReliableTransport.Install(h); }, "Assembly-CSharp", "FishNet.Runtime", "com.rlabrecque.steamworks.net");
        Install("scene-outlines", true, SceneOutlines.Install, "Rendering", "MKEdgeDetection", "MKEdgeDetectionUniversalRendererFeature", "Unity.RenderPipelines.Universal.Runtime", "Unity.RenderPipelines.Core.Runtime", "UnityEngine.CoreModule");
        Install("visual-presets", true, VisualTuning.Install, "Ships", "Ships.Interface", "UnityEngine.CoreModule", "UnityEngine.UI", "Unity.TextMeshPro", "UnityEngine.ParticleSystemModule", "Unity.RenderPipelines.Core.Runtime", "Unity.RenderPipelines.Universal.Runtime");
        Install("visual-detail", true, VisualDetail.Install, "Planets", "UnityEngine.CoreModule");
        Install("visual-cadence", true, VisualCadence.Install, "Planets", "Ships", "Ships.Interface", "UnityEngine.CoreModule");
        Presets.Log = message => LoggerInstance.Msg(message);
        Presets.ApplyPresentation(Presets.Current);
        PresetSelector.Initialize();
        if (capture.Value > 0 && Compatibility.Validate(new[] { "Assembly-CSharp", "FishNet.Runtime", "com.rlabrecque.steamworks.net" }, out _))
        {
            NetworkCapture.Log = message => LoggerInstance.Msg(message);
            try { NetworkCapture.Start(capture.Value, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData", "StellarPerformance-network-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".csv")); }
            catch (Exception error) { LoggerInstance.Warning("Network capture unavailable: " + error.GetBaseException().Message); }
        }
        LoggerInstance.Msg("Stellar Performance " + Version + " ready. Native simulation and physics; no save-schema or network-format changes. Preset: " + Presets.Current.Name + ". Presentation trade-offs are local; identical inspected part payloads can be suppressed; incremental-room gas rounding can differ slightly. Enabled modules: " + string.Join(", ", _installed));
    }

    private void Install(string name, bool enabled, Action<HarmonyLib.Harmony> install, params string[] modules)
    {
        if (!enabled) { LoggerInstance.Msg(name + " disabled by configuration."); return; }
        var harmony = new HarmonyLib.Harmony("stellarperformance." + name);
        try
        {
            if (!Compatibility.Validate(modules, out var reason)) { LoggerInstance.Warning(name + " skipped: " + reason); return; }
            install(harmony);
            _installed.Add(name);
            LoggerInstance.Msg(name + " installed.");
        }
        catch (Exception error)
        {
            harmony.UnpatchSelf();
            var cause = error.GetBaseException();
            LoggerInstance.Warning(name + " skipped; native/previous mod behavior retained. " + cause.GetType().Name + ": " + cause.Message);
        }
    }

    public override void OnLateUpdate()
    {
        Presets.ApplyPending();
        VisualTuning.Update();
        if (VisualTuning.Discovering) SceneOutlines.Discover();
        if (_installed.Contains("single-player-rooms")) SinglePlayerRooms.Update();
        if (_installed.Contains("ship-part-batching")) ShipPartBatching.Update();
        if (_installed.Contains("reliable-transport")) ReliableTransport.Update();
        if (NetworkCapture.Active) NetworkCapture.Update();
    }
    public override void OnApplicationQuit()
    {
        PresetSelector.Close();
        if (VisualTuning.Available) VisualTuning.Shutdown();
        SceneOutlines.Restore();
        if (_installed.Contains("ship-part-batching")) ShipPartBatching.Shutdown();
        if (_installed.Contains("reliable-transport")) ReliableTransport.Shutdown();
        NetworkCapture.Stop();
        if (_installed.Contains("signal-connection-scan")) SignalConnectionScan.Clear();
        if (_installed.Contains("signal-reduction")) SignalEndpointPlan.Clear();
        if (_installed.Contains("power-transfer-scan")) PowerTransferScan.Clear();
        if (_installed.Contains("save-serialization")) SaveSerialization.Clear();
        if (_installed.Contains("canvas-culling")) CanvasCulling.Shutdown();
    }
    public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
    {
        PresetSelector.Close();
        if (VisualTuning.Available) VisualTuning.Change(Presets.Current);
        if (_installed.Contains("signal-connection-scan")) SignalConnectionScan.Clear();
        if (_installed.Contains("signal-reduction")) SignalEndpointPlan.Clear();
        if (_installed.Contains("power-transfer-scan")) PowerTransferScan.Clear();
        if (_installed.Contains("save-serialization")) SaveSerialization.Clear();
        if (_installed.Contains("canvas-culling")) CanvasCulling.Clear();
    }
    private bool _installEmptyWarp;
    public override void OnUpdate()
    {
        if (_installEmptyWarp)
        {
            _installEmptyWarp = false;
            Install("empty-warp-updates", true, EmptyWarpUpdates.Install, "Ships", "Ships.Interface", "WorldTracking", "Core", "UnityEngine.CoreModule");
        }
        PresetSelector.Update();
    }
    public override void OnGUI() => PresetSelector.Draw();
}
