using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Parts.SignalDisplay;
using Ships.Parts.GasMeter;
using Ships.Parts.Thruster;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StellarPerformance;

// Only the inspected passive text methods and cosmetic exhaust are intercepted.
// Native network state storage, interaction callbacks and simulation are untouched.
internal static class VisualTuning
{
    static VisualTuning() { }
    private const int Limit = 2048;
    private const string Owner = "stellarperformance.visual-presets";
    internal static bool Available;
    private static PresetPolicy Policy = PresetPolicy.Named("Balanced");
    private static Func<SignalDisplayVisuals, TMP_Text> SignalText = null!;
    private static Func<GasMeterVisuals, TMP_Text> GasText = null!;
    private static Func<ThrusterFlameVFX, ParticleSystem> Flame = null!;
    private static Func<ThrusterFlameVFX, float> LowRate = null!, HighRate = null!;
    private static Action<SignalDisplayVisuals, IStatefulPartState> SignalUpdate = null!;
    private static Action<GasMeterVisuals, Ships.Interface.Model.Parts.State.GasMeterState> GasUpdate = null!;
    private static bool Flushing;
    private sealed class Readout
    {
        internal readonly Component Owner;
        internal readonly TMP_Text Text;
        internal IStatefulPartState? Pending;
        internal float Due;
        internal Readout(Component owner, TMP_Text text) { Owner = owner; Text = text; }
    }
    private sealed class Exhaust
    {
        internal readonly ParticleSystem System;
        internal float Native;
        internal Exhaust(ParticleSystem system, float native) { System = system; Native = native; }
    }
    private static readonly Dictionary<Component, Readout> Readouts = new Dictionary<Component, Readout>();
    private static readonly List<Component> Dead = new List<Component>();
    private static readonly Dictionary<ThrusterFlameVFX, Exhaust> Exhausts = new Dictionary<ThrusterFlameVFX, Exhaust>();
    private static UniversalRenderPipelineAsset? Pipeline;
    private static float NativeDistance;
    private static int NativeCascades;
    private static int NativeMainShadowMap, NativeAdditionalShadowMap;
    private static bool NativeSoftShadows;
    private static Action<UniversalRenderPipelineAsset, bool> SetSoftShadows = null!;
    private static float DiscoverUntil, NextDiscovery;
    private static readonly Camera[] Cameras = new Camera[32];
    private sealed class FocusInfo
    {
        internal readonly Canvas Canvas;
        internal readonly TMP_InputField[] Inputs;
        internal FocusInfo(Canvas canvas) { Canvas = canvas; Inputs = canvas.GetComponentsInChildren<TMP_InputField>(true); }
    }
    private static readonly Dictionary<TMP_Text, FocusInfo> Focus = new Dictionary<TMP_Text, FocusInfo>();

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var signal = AccessTools.Method(typeof(SignalDisplayVisuals), "UpdateSignalVal");
        var gas = AccessTools.Method(typeof(GasMeterVisuals), "UpdateScreen");
        var flame = AccessTools.Method(typeof(ThrusterFlameVFX), nameof(ThrusterFlameVFX.SetIntensity));
        foreach (var method in new[] { signal, gas, flame }) PatchSafety.RequireNoForeignOwners(method, Owner);
        SignalText = NativeAccess.Read<SignalDisplayVisuals, TMP_Text>("displayedText");
        GasText = NativeAccess.Read<GasMeterVisuals, TMP_Text>("screen");
        Flame = NativeAccess.Read<ThrusterFlameVFX, ParticleSystem>("_particleSystem");
        LowRate = NativeAccess.Read<ThrusterFlameVFX, float>("lowRate"); HighRate = NativeAccess.Read<ThrusterFlameVFX, float>("highRate");
        SetSoftShadows = AccessTools.MethodDelegate<Action<UniversalRenderPipelineAsset, bool>>(AccessTools.PropertySetter(typeof(UniversalRenderPipelineAsset), nameof(UniversalRenderPipelineAsset.supportsSoftShadows)));
        SignalUpdate = AccessTools.MethodDelegate<Action<SignalDisplayVisuals, IStatefulPartState>>(signal);
        GasUpdate = AccessTools.MethodDelegate<Action<GasMeterVisuals, Ships.Interface.Model.Parts.State.GasMeterState>>(gas);
        harmony.Patch(signal, prefix: new HarmonyMethod(typeof(VisualTuning), nameof(BeforeSignal)));
        harmony.Patch(gas, prefix: new HarmonyMethod(typeof(VisualTuning), nameof(BeforeGas)));
        harmony.Patch(flame, postfix: new HarmonyMethod(typeof(VisualTuning), nameof(AfterFlame)));
        Available = true; SceneChanged();
        Canvas.preWillRenderCanvases += BeforeRender;
    }
    private static bool BeforeSignal(SignalDisplayVisuals __instance, IStatefulPartState state) => BeforeText(__instance, SignalText(__instance), state);
    private static bool BeforeGas(GasMeterVisuals __instance, Ships.Interface.Model.Parts.State.GasMeterState gasMeterState) => BeforeText(__instance, GasText(__instance), gasMeterState);
    private static bool BeforeText(Component owner, TMP_Text text, IStatefulPartState state)
    {
        if (Flushing || Policy.ReadoutHz == 0 || owner == null || text == null) return true;
        if (!Readouts.TryGetValue(owner, out var row))
        {
            if (Readouts.Count >= Limit) return true;
            Readouts.Add(owner, row = new Readout(owner, text));
        }
        var now = Time.unscaledTime;
        if (!Unfocused(text, false) || now >= row.Due)
        { row.Pending = null; row.Due = now + 1f / Policy.ReadoutHz; return true; }
        row.Pending = state; return false;
    }
    // Readout refresh caps apply to every unfocused passive readout; small-label
    // omission additionally requires every relevant camera to be beyond 20 m.
    internal static bool Unfocused(TMP_Text text, bool distant)
    {
        if (text == null) return false;
        var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
        if (!Focus.TryGetValue(text, out var focus))
        {
            var found = text.GetComponentInParent<Canvas>();
            if (found == null || Focus.Count >= Limit) return false;
            Focus.Add(text, focus = new FocusInfo(found));
        }
        var canvas = focus.Canvas;
        if (canvas == null || canvas.renderMode != RenderMode.WorldSpace) return false;
        if (selected != null && selected.transform.IsChildOf(canvas.transform)) return false;
        foreach (var input in focus.Inputs) if (input != null && input.isFocused) return false;
        var count = Camera.allCamerasCount;
        if (count == 0 || count > Cameras.Length) return false;
        count = Camera.GetAllCameras(Cameras); var relevant = false;
        for (var i = 0; i < count; i++)
        {
            var camera = Cameras[i];
            if (camera == null || !camera.isActiveAndEnabled || (camera.cullingMask & (1 << text.gameObject.layer)) == 0) continue;
            relevant = true;
            if (distant && (camera.transform.position - text.transform.position).sqrMagnitude <= 400f) return false;
        }
        return relevant;
    }
    internal static bool HideTinyLabel(TMP_Text text)
    {
        if (Policy.LabelPixels == 0 || !Unfocused(text, true)) return false;
        var count = Camera.GetAllCameras(Cameras);
        var bottom = text.transform.position; var top = bottom + text.transform.TransformVector(Vector3.up * text.fontSize);
        var relevant = false;
        for (var i = 0; i < count; i++)
        {
            var camera = Cameras[i];
            if (camera == null || !camera.isActiveAndEnabled || (camera.cullingMask & (1 << text.gameObject.layer)) == 0) continue;
            relevant = true;
            var a = camera.WorldToScreenPoint(bottom); var b = camera.WorldToScreenPoint(top);
            if (a.z <= 0 || b.z <= 0 || float.IsNaN(a.x) || float.IsNaN(b.x) ||
                Vector2.Distance(new Vector2(a.x, a.y), new Vector2(b.x, b.y)) >= Policy.LabelPixels) return false;
        }
        return relevant;
    }
    private static void BeforeRender() { if (Available) Flush(false); }
    private static void Flush(bool all)
    {
        Flushing = true;
        try
        {
            Dead.Clear(); var now = Time.unscaledTime;
            foreach (var pair in Readouts)
            {
                var row = pair.Value;
                if (row.Owner == null || row.Text == null) { Dead.Add(pair.Key); continue; }
                var pending = row.Pending;
                if (pending == null || (!all && Policy.ReadoutHz != 0 && now < row.Due && Unfocused(row.Text, false))) continue;
                row.Pending = null; row.Due = Policy.ReadoutHz == 0 ? now : now + 1f / Policy.ReadoutHz;
                if (row.Owner is SignalDisplayVisuals signal) SignalUpdate(signal, pending);
                else if (row.Owner is GasMeterVisuals gas) GasUpdate(gas, (Ships.Interface.Model.Parts.State.GasMeterState)pending);
            }
            foreach (var key in Dead) Readouts.Remove(key);
            if (all) Readouts.Clear();
        }
        finally { Flushing = false; }
    }
    private static void AfterFlame(ThrusterFlameVFX __instance, float intensity)
    {
        if (Policy.ExhaustDensity == 1 || __instance == null) return;
        var system = Flame(__instance); if (system == null) return;
        if (!Exhausts.TryGetValue(__instance, out var row))
        {
            if (Exhausts.Count >= Limit) return;
            Exhausts.Add(__instance, row = new Exhaust(system, 0));
        }
        var emission = system.emission; row.Native = emission.rateOverTimeMultiplier;
        emission.rateOverTimeMultiplier = row.Native * Policy.ExhaustDensity;
    }
    private static void RestoreGraphics()
    {
        if (Pipeline != null)
        {
            Pipeline.shadowDistance = NativeDistance; Pipeline.shadowCascadeCount = NativeCascades;
            Pipeline.mainLightShadowmapResolution = NativeMainShadowMap; Pipeline.additionalLightsShadowmapResolution = NativeAdditionalShadowMap;
            SetSoftShadows(Pipeline, NativeSoftShadows);
        }
        Pipeline = null;
        foreach (var pair in Exhausts) if (pair.Value.System != null) { var emission = pair.Value.System.emission; emission.rateOverTimeMultiplier = pair.Value.Native; }
        Exhausts.Clear();
    }
    internal static void Change(PresetPolicy policy)
    {
        Flush(true); RestoreGraphics(); Policy = policy;
        DiscoverGraphics(); SceneChanged();
    }
    internal static void SceneChanged() { Focus.Clear(); Array.Clear(Cameras, 0, Cameras.Length); DiscoverUntil = Time.unscaledTime + 8; NextDiscovery = 0; }
    internal static bool Discovering { get; private set; }
    internal static void Update()
    {
        Discovering = false;
        if (!Available || Time.unscaledTime > DiscoverUntil || Time.unscaledTime < NextDiscovery) return;
        NextDiscovery = Time.unscaledTime + 1; DiscoverGraphics(); Discovering = true;
    }
    private static void DiscoverGraphics()
    {
        if (Pipeline == null && (Policy.ShadowDistance > 0 || Policy.Cascades > 0 || Policy.ShadowResolution > 0 || Policy.ShadowQuality > 0) && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)
        {
            Pipeline = pipeline; NativeDistance = pipeline.shadowDistance; NativeCascades = pipeline.shadowCascadeCount;
            NativeMainShadowMap = pipeline.mainLightShadowmapResolution; NativeAdditionalShadowMap = pipeline.additionalLightsShadowmapResolution; NativeSoftShadows = pipeline.supportsSoftShadows;
            if (Policy.ShadowDistance > 0) pipeline.shadowDistance = Math.Min(NativeDistance, Policy.ShadowDistance);
            if (Policy.Cascades > 0) pipeline.shadowCascadeCount = Math.Min(NativeCascades, Policy.Cascades);
            if (Policy.ShadowResolution > 0)
            { pipeline.mainLightShadowmapResolution = Math.Min(NativeMainShadowMap, Policy.ShadowResolution); pipeline.additionalLightsShadowmapResolution = Math.Min(NativeAdditionalShadowMap, Policy.ShadowResolution); }
            if (Policy.ShadowQuality == 1) SetSoftShadows(pipeline, false);
            if (Policy.ShadowQuality == 2) pipeline.shadowDistance = 0;
        }
        if (Policy.ExhaustDensity < 1)
            foreach (var flame in UnityEngine.Object.FindObjectsByType<ThrusterFlameVFX>(FindObjectsSortMode.None))
            {
                if (Exhausts.Count >= Limit) break;
                var system = Flame(flame);
                if (system == null || Exhausts.ContainsKey(flame)) continue;
                var emission = system.emission; Exhausts.Add(flame, new Exhaust(system, emission.rateOverTimeMultiplier));
                emission.rateOverTimeMultiplier *= Policy.ExhaustDensity;
            }
    }
    internal static void Shutdown()
    {
        Canvas.preWillRenderCanvases -= BeforeRender;
        Flush(true); RestoreGraphics(); Available = false;
    }
}
