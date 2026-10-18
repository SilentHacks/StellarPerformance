using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;

[assembly: MelonInfo(typeof(StellarPerformance.PerformancePlugin), "Stellar Performance", StellarPerformance.PerformancePlugin.Version, "SilentHacks", null)]
[assembly: MelonGame("CuriousOwlGames", "StellarDrive")]
[assembly: HarmonyDontPatchAll]

namespace StellarPerformance;

public sealed class PerformancePlugin : MelonMod
{
    internal const string Version = "1.0.0";
    private readonly List<string> _installed = new List<string>();

    public override void OnLateInitializeMelon()
    {
        var preferences = MelonPreferences.CreateCategory("StellarPerformance", "Stellar Performance");
        var bounds = preferences.CreateEntry("ShipGridBounds", true, description: "Cache exact ship grid bounds; invalidate on native grid mutations. Restart to change.");
        var fluids = preferences.CreateEntry("FluidCableTopology", true, description: "Cache the fluid cable visual graph. Recompute live flow and native presentation every frame. Restart to change.");
        var rooms = preferences.CreateEntry("IncrementalRooms", true, description: "Rebuild edited air regions locally, retaining native wall/gas handling and valid saved coordinates. Gas rounding can differ slightly. Restart to change.");
        var singlePlayer = preferences.CreateEntry("SinglePlayerRooms", true, description: "Forward single-player host room queries to authority; rebuild the local mirror for peers/debug views. Restart to change.");
        var batching = preferences.CreateEntry("ShipPartBatching", true, description: "Batch inspected fixed opaque meshes; restore native renderers on interaction and topology changes. Restart to change.");
        var continuous = preferences.CreateEntry("BatchingContinuousValidation", false, description: "Compatibility option: inspect every batch source each frame. Off retains inspected native event hooks and excludes unknown prefab scripts. Restart to change.");
        Install("ship-grid-bounds", bounds.Value, h => ShipGridBounds.Install(h), "Ships", "Ships.Interface", "UnityEngine.CoreModule");
        Install("fluid-cable-topology", fluids.Value, h => FluidCableTopology.Install(h), "Ships", "Ships.Interface", "UnityEngine.CoreModule");
        Install("incremental-rooms", rooms.Value, h => IncrementalRooms.Install(h), "Ships", "Ships.Interface", "UnityEngine.CoreModule");
        Install("single-player-rooms", singlePlayer.Value, h => { SinglePlayerRooms.Log = message => LoggerInstance.Msg(message); SinglePlayerRooms.Install(h); }, "Ships", "Ships.Interface", "Core", "FishNet.Runtime", "UnityEngine.CoreModule");
        Install("ship-part-batching", batching.Value, h =>
        {
            ShipPartBatching.ContinuousValidation = continuous.Value;
            ShipPartBatching.Log = message => LoggerInstance.Msg(message);
            ShipPartBatching.Install(h);
        }, "Ships", "Ships.Interface", "Core", "Players", "UI", "UnityEngine.CoreModule", "Unity.TextMeshPro");
        LoggerInstance.Msg("Stellar Performance " + Version + " ready. Native simulation, physics, network and presentation update rates; no save-schema changes. Room gas rounding can differ slightly. Enabled modules: " + string.Join(", ", _installed));
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
        if (_installed.Contains("single-player-rooms")) SinglePlayerRooms.Update();
        if (_installed.Contains("ship-part-batching")) ShipPartBatching.Update();
    }
    public override void OnApplicationQuit()
    {
        if (_installed.Contains("ship-part-batching")) ShipPartBatching.Shutdown();
    }
}
