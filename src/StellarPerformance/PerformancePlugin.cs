using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MelonLoader;
using UnityEngine.InputSystem;

[assembly: MelonInfo(typeof(StellarPerformance.PerformancePlugin), "Stellar Performance", StellarPerformance.PerformancePlugin.Version, "SilentHacks", null)]
[assembly: MelonGame("CuriousOwlGames", "StellarDrive")]
[assembly: HarmonyDontPatchAll]

namespace StellarPerformance;

public sealed class PerformancePlugin : MelonMod
{
    internal const string Version = "0.9.0";
    private readonly List<string> _installed = new List<string>();
    private bool _diagnostics;
    private bool _loaderCache;
    private MelonPreferences_Entry<float> _captureSeconds = null!;

    public override void OnLateInitializeMelon()
    {
        var preferences = MelonPreferences.CreateCategory("StellarPerformance", "Stellar Performance");
        var snapshots = preferences.CreateEntry("WorldObjectSnapshots", true, description: "Host-side: capture one native world-object snapshot per sync pass. Restart to change.");
        var cableBuffers = preferences.CreateEntry("CableMeshBuffers", true, description: "Client-side: reuse exact-length cable arrays; retain at most 6 MiB per calling thread and clear on return. Native calculations stay unchanged. Restart to change.");
        var fluidSocketReads = preferences.CreateEntry("FluidGraphSocketReads", false, description: "Experimental client-side: reuse prefab socket-component arrays within one synchronous fluid update only. Connections, socket properties and fluid values stay live; assumes prefab hierarchy is stable during that call. Restart to change.");
        var signalReads = preferences.CreateEntry("SignalEvaluationReads", true, description: "Experimental host-side: reuse native signal-connection unions while public socket/set membership versions are unchanged; classify live inputs and omit unused native output roles. Custom callbacks and patched classification paths stay native. Restart to change.");
        var membership = preferences.CreateEntry("ContentMembership", true, description: "Optional SDModKit adapter: cache complete part membership, not definitions or live state. Restart to change.");
        var hullReads = preferences.CreateEntry("ScopedHullMeshReads", true, description: "Client-side: reuse source mesh reads within one native rebuild. Restart to change.");
        var gridBounds = preferences.CreateEntry("ShipGridBounds", true, description: "Host/client: cache ShipGrid.GetCoordBounds per ship and invalidate on hull or stateful part grid mutations. Restart to change.");
        var partQueries = preferences.CreateEntry("StatefulPartQueries", true, description: "Host/client: cache resolved GetAllStatefulParts<T> arrays per ship and invalidate on part additions or removals. Restart to change.");
        var fluidTopology = preferences.CreateEntry("FluidCableTopology", true, description: "Client-side: cache fluid cable visual graph topology across frames until cables, connections, or parts change, and skip unchanged cable shader property writes. Restart to change.");
        var partVisuals = preferences.CreateEntry("PartVisualUpdates", true, description: "Client-side: skip redundant transform/material writes on idle part visuals and stationary LookAtPlayer billboards. Restart to change.");
        var modKitTools = preferences.CreateEntry("ModKitToolRefs", true, description: "Optional SDModKit 1.3.3 adapter: resolve ToolRefs.Build and ToolRefs.Recycler in O(1) from LocalSelectedToolTracker without 1 Hz FindFirstObjectByType scene scans. Restart to change.");
        var distillerSlots = preferences.CreateEntry("DistillerSlotReuse", true, description: "Host-side: reuse EthanolDistillerState.InventorySlots array when slot contents are unchanged so record equality filters redundant network updates. Restart to change.");
        var physicsTransforms = preferences.CreateEntry("PhysicsSceneTransforms", true, description: "Host/client: skip identity terrain collider transposes, exact-equal interior/client ship collider transform writes, and redundant physics-scene lookups/renames. Restart to change.");
        var worldVisuals = preferences.CreateEntry("WorldVisualUpdates", true, description: "Client-side: remove duplicate hotspot object transform writes, hoist planet-to-visual transform outside the loop, and avoid per-frame LINQ allocations. Restart to change.");
        var clientSignals = preferences.CreateEntry("ClientSignalCables", true, description: "Client-side: reuse validated signal-connection input projection during SignalCablesVisualUpdater and cache per-prefab signal sockets at that call site. Restart to change.");
        var stateReuse = preferences.CreateEntry("NativeStateReuse", true, description: "Host-side: avoid identical native tank/thruster/valve/signal record allocations. Preserve calculations, tick rates and native state-update events. Restart to change.");
        var stableSignals = preferences.CreateEntry("StableSignals", true, description: "Host: skip unchanged native signal processors and cable groups in native operation order. Changing inputs/configuration wake them; derivatives remain native. Identical state callbacks are coalesced. Restart to change.");
        var signalRecheck = preferences.CreateEntry("StableSignalRecheckSeconds", 0.25f, description: "Maximum stable-value refresh interval, clamped to 0.02..1 second. Initial deadlines are staggered; wiring follows native topology invalidation. Input changes still propagate in native order each tick. Restart to change.").Value;
        var incrementalRooms = preferences.CreateEntry("IncrementalRooms", true, description: "Host/client: rebuild dirty air regions locally, stop exterior searches at the octree boundary, and keep native wall/gas interpolation and valid saved room coordinates. Other room-rewriting mods retain their own behavior. Restart to change.");
        var singlePlayerRooms = preferences.CreateEntry("SinglePlayerRooms", true, description: "Single-player host: query authoritative room geometry instead of building a duplicate client room tree. Reconstruct native client rooms when another player joins or a room debug visualizer needs them. Restart to change.");
        var worldUi = preferences.CreateEntry("WorldUiVisibility", true, description: "Client-side: defer hidden part display updates and avoid unchanged presentation callbacks; refresh before rendering on re-entry. Restart to change.");
        var cullCanvases = preferences.CreateEntry("CullOffscreenWorldUi", true, description: "Client-side: suppress registered offscreen world-space part Canvases with conservative bounds and all-camera visibility. Focused input fields remain active. Requires WorldUiVisibility; restart to change.").Value;
        var uiDistance = preferences.CreateEntry("WorldUiMaximumDistance", 100f, description: "Optional visual tradeoff: maximum world-space part UI distance in metres. 0 preserves native distance. Requires WorldUiVisibility; restart to change.").Value;
        var maximumDistance = float.IsNaN(uiDistance) || float.IsInfinity(uiDistance) ? 0 : Math.Max(0, Math.Min(9000, uiDistance));
        var uiRefresh = preferences.CreateEntry("WorldUiRefreshHz", 10f, description: "Numeric part readouts refresh at this rate; focused fields and control changes stay immediate. 0 uses the native rate. Restart to change.").Value;
        var rendererDetail = preferences.CreateEntry("RendererDetail", true, description: "Reduce fixed part mesh detail and shadows at distance. Nearby geometry, ID rendering and colliders remain native. Restart to change.");
        var minimumPixels = preferences.CreateEntry("MinimumDetailPixels", 3f, description: "Omit fixed opaque meshes smaller than this projected diameter beyond 20 m. 0 keeps all meshes. Restart to change.").Value;
        var shadowDistance = preferences.CreateEntry("PartShadowDistance", 40f, description: "Stop fixed part meshes casting shadows beyond this distance in metres. 0 keeps native shadows. Restart to change.").Value;
        var partBatching = preferences.CreateEntry("ShipPartBatching", true, description: "Experimental fixed part mesh batching: readable opaque stock URP or asset-hash-verified Custom/PlanetObject, matching materials/vertex layouts and uniform positive transforms. Native fallback on interaction/topology/material/transform changes. Restart to change.");
        var continuousValidation = preferences.CreateEntry("BatchingContinuousValidation", false, description: "Compatibility option for ShipPartBatching: inspect every source renderer/material/transform each frame. Adds CPU overhead. Off uses inspected native topology, highlight, hover and valve-state invalidation, and excludes unknown prefab scripts. Restart to change.").Value;
        var terrainCadence = preferences.CreateEntry("TerrainTraversalCadence", true, description: "Client: reuse the previous terrain LOD traversal while the observer has moved less than max(0.25 m, 0.2% of altitude); refresh at least every 15 frames. Restart to change.");
        var cableHz = preferences.CreateEntry("CableVisualHz", 20f, description: "Client: recompute cosmetic signal glow targets and fluid flow balancing at this rate; per-frame cable animation stays smooth. 0 uses every frame; maximum 60. Restart to change.").Value;
        var pickingDivisor = preferences.CreateEntry("PickingCameraDivisor", 3, description: "Client: render the 128x128 object-id picking camera on every Nth frame (hover/interaction lookups update a few frames later). 1 renders every frame; maximum 6. Restart to change.").Value;
        var simulationDivisor = preferences.CreateEntry("PartSimulationDivisor", 2, description: "Host: advance factory simulation (signals, fluids, machines, power, rooms) every Nth network tick with an N-times step. Physics/network ticks stay native; piloting and lever use run every tick. 1 is native; maximum 4. Restart to change.").Value;
        var loaderCache = preferences.CreateEntry("LoaderReflectionCache", true, description: "Reuse MelonLoader's resolved SetAsLastSibling delegate instead of repeating reflection every frame. Restart to change.");
        var trailer = preferences.CreateEntry("PacketTrailerHardening", true, description: "Correct channel-trailer boundary handling only; no MTU, queue, retry, or wire-format changes. Restart to change.");
        _diagnostics = preferences.CreateEntry("Diagnostics", false, description: "Install targeted timing probes. Ctrl+Shift+F8 starts/stops a bounded capture to the MelonLoader log. Restart to change.").Value;
        _captureSeconds = preferences.CreateEntry("CaptureSeconds", 30f, description: "Diagnostic capture duration, clamped to 5..120 seconds.");
        var diagnosticMode = preferences.CreateEntry("DiagnosticMode", "Lean", description: "Frame: frame/GC timing without Harmony probes or PlayerLoop brackets. Lean: aggregate and focused timings without detailed per-state/transform counters. Full: include expensive state/serialized-byte/transform counters. Restart to change.").Value;
        if (_diagnostics && !Compatibility.Validate(new[] { "Unity.InputSystem", "UnityEngine.CoreModule" }, out var inputReason))
        {
            _diagnostics = false;
            LoggerInstance.Warning("Diagnostics disabled: " + inputReason);
        }
        if (_diagnostics) Diagnostics.Configure(diagnosticMode);
        var hasSdModKit = AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "SDModKit");
        var hz = FiniteRange(cableHz, 20, 0, 60);
        CableVisualCadence.Interval = hz <= 0 ? 0 : 1f / hz;
        Install("world-snapshot", snapshots.Value, h => WorldSnapshotPatch.Install(h), "Assembly-CSharp", "WorldObjects", "WorldObjects.Interface", "UnityEngine.CoreModule", "FishNet.Runtime");
        Install("cable-buffers", cableBuffers.Value, h => CableBuffers.Install(h), "CablesMesh", "UnityEngine.CoreModule");
        Install("fluid-sockets", fluidSocketReads.Value, h => FluidSocketReads.Install(h), "Ships", "Ships.Interface", "UnityEngine.CoreModule");
        Install("fluid-socket-content", fluidSocketReads.Value && _installed.Contains("fluid-sockets") && hasSdModKit, h => FluidSocketReads.InstallContent(h), "SDModKit", "Ships", "Ships.Interface", "UnityEngine.CoreModule");
        Install("signal-evaluation", signalReads.Value, h => SignalInputProjection.Install(h), "Ships", "Ships.Interface", "System.Core");
        Install("content-membership", membership.Value && hasSdModKit, InstallContent, "SDModKit", "Ships", "Ships.Interface", "UnityEngine.CoreModule");
        Install("hull-reads", hullReads.Value, h => HullMeshReads.Install(h), "Ships", "Ships.Interface", "UnityEngine.CoreModule");
        Install("ship-grid-bounds", gridBounds.Value, h => ShipGridBounds.Install(h), "Ships", "Ships.Interface", "UnityEngine.CoreModule");
        Install("stateful-part-queries", partQueries.Value, h => StatefulPartQueries.Install(h), "Ships", "Ships.Interface");
        Install("fluid-cable-topology", fluidTopology.Value, h => FluidCableTopology.Install(h), "Ships", "Ships.Interface", "UnityEngine.CoreModule");
        Install("part-visual-updates", partVisuals.Value, h => PartVisualUpdates.Install(h), "Ships", "Ships.Interface", "Players", "Core", "UnityEngine.CoreModule");
        Install("modkit-tool-refs", modKitTools.Value && hasSdModKit, h => ToolRefsCache.Install(h), "SDModKit", "Players", "Players.Interface", "Tools", "Tools.Interface", "UnityEngine.CoreModule");
        Install("distiller-slot-reuse", distillerSlots.Value, h => DistillerSlotReuse.Install(h), "Ships", "Ships.Interface");
        Install("physics-scene-transforms", physicsTransforms.Value, h => PhysicsSceneTransforms.Install(h), "Assembly-CSharp", "Planets", "Players", "Ships", "Ships.Interface", "WorldObjects", "WorldTracking", "WorldTracking.Interface", "UnityEngine.CoreModule");
        Install("world-visual-updates", worldVisuals.Value, h => WorldVisualUpdates.Install(h), "Assembly-CSharp", "Planets", "WorldTracking.Interface", "UnityEngine.CoreModule");
        Install("client-signal-cables", clientSignals.Value, h => SignalInputProjection.InstallClient(h), "Ships", "Ships.Interface", "System.Core");
        Install("native-state-reuse", stateReuse.Value, h => NativeStateReuse.Install(h), "Ships", "Ships.Interface");
        Install("stable-signals", stableSignals.Value, h =>
        {
            StableSignals.RecheckSeconds = FiniteRange(signalRecheck, 0.25f, 0.02f, 1f);
            StableSignals.Install(h);
        }, "Ships", "Ships.Interface");
        Install("incremental-rooms", incrementalRooms.Value, h => IncrementalRooms.Install(h), "Ships", "Ships.Interface", "UnityEngine.CoreModule");
        Install("single-player-rooms", singlePlayerRooms.Value, h => { SinglePlayerRooms.Log = message => LoggerInstance.Msg(message); SinglePlayerRooms.Install(h); }, "Ships", "Ships.Interface", "Core", "FishNet.Runtime", "UnityEngine.CoreModule");
        Install("world-ui-visibility", worldUi.Value, h =>
        {
            WorldUiVisibility.CullCanvases = cullCanvases;
            WorldUiVisibility.MaximumDistance = maximumDistance;
            var hz = FiniteRange(uiRefresh, 10, 0, 60);
            WorldUiVisibility.RefreshInterval = hz <= 0 ? 0 : 1f / hz;
            WorldUiVisibility.Log = message => LoggerInstance.Warning(message);
            WorldUiVisibility.Install(h);
        }, "Ships", "Ships.Interface", "UI", "UnityEngine.CoreModule", "UnityEngine.UIModule", "UnityEngine.UI", "Unity.TextMeshPro");
        Install("ship-part-batching", partBatching.Value, h =>
        {
            ShipPartBatching.ContinuousValidation = continuousValidation;
            ShipPartBatching.Log = message => LoggerInstance.Msg(message);
            ShipPartBatching.Install(h);
        }, "Ships", "Ships.Interface", "Core", "Players", "UI", "UnityEngine.CoreModule", "Unity.TextMeshPro");
        Install("renderer-detail", rendererDetail.Value, h =>
        {
            RendererDetail.MinimumPixels = FiniteRange(minimumPixels, 3, 0, 16);
            RendererDetail.ShadowDistance = FiniteRange(shadowDistance, 40, 0, 9000);
            RendererDetail.Log = message => LoggerInstance.Warning(message);
            RendererDetail.Install(h);
        }, "Ships", "Ships.Interface", "Core", "Players", "UI", "UnityEngine.CoreModule", "Unity.TextMeshPro");
        Install("packet-trailer", trailer.Value, h => PacketTrailerPatch.Install(h), "Assembly-CSharp", "FishNet.Runtime", "com.rlabrecque.steamworks.net");
        Install("terrain-traversal-cadence", terrainCadence.Value, h => PlanetTraversalCadence.Install(h), "Planets", "UnityEngine.CoreModule");
        Install("cable-visual-cadence", CableVisualCadence.Interval > 0, h => CableVisualCadence.Install(h), "Ships", "Ships.Interface", "UnityEngine.CoreModule");
        Install("picking-camera-cadence", pickingDivisor > 1, h => PickingCameraCadence.Divisor = Math.Min(6, pickingDivisor), "UnityEngine.CoreModule");
        Install("simulation-cadence", simulationDivisor > 1, h =>
        {
            SimulationCadence.Divisor = Math.Min(4, simulationDivisor);
            SimulationCadence.Install(h);
        }, "Assembly-CSharp", "Players", "Players.Interface", "Ships");
        Install("loader-sibling-cache", loaderCache.Value, h => LoaderOverhead.Install(h));
        _loaderCache = _installed.Contains("loader-sibling-cache");
        if (_diagnostics && Diagnostics.Mode != DiagnosticMode.Frame)
        {
            Install("profile-server", true, h => Diagnostics.InstallServer(h), "Assembly-CSharp", "WorldObjects", "WorldObjects.Interface", "Ships", "Ships.Interface", "FishNet.Runtime", "UnityEngine.CoreModule");
            Install("profile-cables", true, h => Diagnostics.InstallCables(h), "CablesMesh", "Ships", "Ships.Interface", "UnityEngine.CoreModule");
            Install("profile-client", true, h => Diagnostics.InstallClient(h), "Assembly-CSharp", "Planets", "Ships", "Ships.Interface", "UnityEngine.CoreModule", "UnityEngine.PhysicsModule", "FishNet.Runtime");
            Install("profile-item-routing", AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "ItemLines"), h => Diagnostics.InstallItemRouting(h), "ItemLines");
            Install("profile-render", true, h => Diagnostics.InstallRendering(h), "UnityEngine.CoreModule");
            Install("profile-mods", hasSdModKit, h => Diagnostics.InstallModUpdates(h), "SDModKit");
            Install("profile-transport", true, h => Diagnostics.InstallTransport(h), "Assembly-CSharp", "FishNet.Runtime", "com.rlabrecque.steamworks.net");
            Install("profile-save", true, h => Diagnostics.InstallSaves(h), "Saving");
        }
        LoggerInstance.Msg("Stellar Performance " + Version + " ready. No save-schema, physics-rate or network-tick changes; factory simulation divisor=" + (_installed.Contains("simulation-cadence") ? SimulationCadence.Divisor : 1) + ". Experimental room arithmetic can differ by rounding. World UI distance=" + maximumDistance + " m (0=native). Enabled modules: " + string.Join(", ", _installed));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InstallContent(HarmonyLib.Harmony harmony) => ContentMembership.Install(harmony);

    internal static float FiniteRange(float value, float fallback, float minimum, float maximum) =>
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(minimum, Math.Min(maximum, value));

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
            LoggerInstance.Warning(name + " skipped; its native/previous mod behavior is retained. " + cause.GetType().Name + ": " + cause.Message);
        }
    }

    public override void OnUpdate()
    {
        if (_loaderCache) LoaderOverhead.Update();
        if (!_diagnostics) return;
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f8Key.wasPressedThisFrame && keyboard.ctrlKey.isPressed && keyboard.shiftKey.isPressed)
        {
            if (Diagnostics.Active) Report(Diagnostics.Stop());
            else
            {
                Diagnostics.Start(_captureSeconds.Value, "modules=" + string.Join(",", _installed));
                LoggerInstance.Msg("Diagnostic capture started. Keep the scene and workload controlled; Ctrl+Shift+F8 stops early.");
                return;
            }
        }
        Report(Diagnostics.Update());
    }

    public override void OnLateUpdate()
    {
        if (_installed.Contains("single-player-rooms")) SinglePlayerRooms.Update();
        if (_installed.Contains("ship-part-batching")) ShipPartBatching.Update();
        if (_installed.Contains("renderer-detail")) RendererDetail.Update();
        if (_installed.Contains("picking-camera-cadence")) PickingCameraCadence.Update();
    }
    public override void OnApplicationQuit()
    {
        if (_diagnostics) Report(Diagnostics.Stop());
        if (_installed.Contains("picking-camera-cadence")) PickingCameraCadence.Shutdown();
        if (_installed.Contains("world-ui-visibility")) WorldUiVisibility.Shutdown();
        if (_installed.Contains("renderer-detail")) RendererDetail.Shutdown();
        if (_installed.Contains("ship-part-batching")) ShipPartBatching.Shutdown();
    }
    private void Report(string? report) { if (report != null) LoggerInstance.Msg(report); }
}
