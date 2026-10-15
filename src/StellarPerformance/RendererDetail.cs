using System;
using System.Collections.Generic;
using HarmonyLib;
using Ships.Interface.Model.Parts;
using Ships.Parts.Common;
using Ships.Visuals;
using UnityEngine;
using UnityEngine.Rendering;

namespace StellarPerformance;

// Presentation only. Keep close geometry and all gameplay/ID objects at native detail.
// Sample at 10 Hz; hysteresis avoids toggling meshes along a detail boundary.
internal static class RendererDetail
{
    static RendererDetail() { }

    internal readonly struct View
    {
        internal readonly Vector3 Position, Forward;
        internal readonly int Mask;
        internal readonly float FocalPixels, Near, Far;
        internal readonly bool Orthographic;
        internal View(Vector3 position, Vector3 forward, int mask, float focalPixels, float near, float far, bool orthographic)
        { Position = position; Forward = forward; Mask = mask; FocalPixels = focalPixels; Near = near; Far = far; Orthographic = orthographic; }
    }

    private sealed class Entry
    {
        internal readonly MeshRenderer Renderer;
        internal bool Hidden, ShadowsSuppressed;
        private ShadowCastingMode _shadows;
        internal Entry(MeshRenderer renderer) { Renderer = renderer; }
        internal void Apply(bool hidden, bool distant)
        {
            if (hidden != Hidden)
            {
                if (!hidden || !Renderer.forceRenderingOff) { Renderer.forceRenderingOff = hidden; Hidden = hidden; }
            }
            if (distant && !ShadowsSuppressed && Renderer.shadowCastingMode != ShadowCastingMode.Off)
            { _shadows = Renderer.shadowCastingMode; Renderer.shadowCastingMode = ShadowCastingMode.Off; ShadowsSuppressed = true; }
            else if (!distant && ShadowsSuppressed)
            {
                if (Renderer.shadowCastingMode == ShadowCastingMode.Off) Renderer.shadowCastingMode = _shadows;
                ShadowsSuppressed = false;
            }
        }
        internal void Restore() { if (Renderer != null) Apply(false, false); }
    }

    private const int MaximumEntries = 32768;
    private static readonly Dictionary<MeshRenderer, Entry> Entries = new Dictionary<MeshRenderer, Entry>();
    private static readonly List<MeshRenderer> Removed = new List<MeshRenderer>();
    private static readonly Camera[] Cameras = new Camera[32];
    private static readonly View[] Views = new View[32];
    private static float _nextUpdate;
    internal static bool Enabled;
    internal static float MinimumPixels = 3f, ShadowDistance = 40f;
    internal static Action<string>? Log;

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ShipPartVisuals), nameof(ShipPartVisuals.AddStatefulPart)),
            postfix: new HarmonyMethod(typeof(RendererDetail), nameof(AfterAdd)));
        Enabled = true;
    }

    internal static void AfterAdd(ShipPartVisuals __instance, StatefulPart part)
    {
        if (!__instance.TryGetFloatingPartVisual(part.Id, out var visual) || !(visual is DefaultShipPartVisuals native) || !ShipPartBatching.FixedBody(native)) return;
        foreach (var renderer in native.GetComponentsInChildren<MeshRenderer>())
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !ShipPartBatching.FixedHierarchy(renderer.transform, native.transform) ||
                renderer.GetComponentInParent<LODGroup>() != null || renderer.GetComponentInParent<Animator>() != null || renderer.GetComponentInParent<Animation>() != null) continue;
            Register(renderer);
        }
    }

    internal static void Register(MeshRenderer renderer)
    {
        if (!Enabled || renderer == null || Entries.Count >= MaximumEntries || Entries.ContainsKey(renderer) || renderer.forceRenderingOff) return;
        var material = renderer.sharedMaterial;
        if (material == null || material.shader == null || material.renderQueue < 0 || material.renderQueue > 2500 ||
            !ShipPartBatching.SupportedShader(material.shader.name, true)) return;
        Entries.Add(renderer, new Entry(renderer));
    }

    // Batching takes ownership of source renderers; restore our flags before it inspects them.
    internal static void Release(MeshRenderer renderer)
    {
        if (renderer == null) return;
        if (!Entries.TryGetValue(renderer, out var entry)) return;
        entry.Restore(); Entries.Remove(renderer);
    }

    internal static void Restore(MeshRenderer renderer)
    {
        if (renderer == null) return;
        if (Entries.TryGetValue(renderer, out var entry)) entry.Restore();
    }

    internal static float ProjectedDiameter(float radius, float depth, float focalPixels, bool orthographic)
    {
        if (!Finite(radius) || !Finite(depth) || !Finite(focalPixels) || radius < 0 || focalPixels <= 0) return float.PositiveInfinity;
        if (orthographic) return 2f * radius * focalPixels;
        return depth <= radius ? float.PositiveInfinity : 2f * radius * focalPixels / (depth - radius);
    }

    internal static bool HideSmall(bool hidden, float pixels, float minimumPixels) => minimumPixels > 0 && pixels < minimumPixels * (hidden ? 1.5f : 1f);
    internal static bool SuppressShadows(bool suppressed, float distance, float maximumDistance) => maximumDistance > 0 && distance > maximumDistance * (suppressed ? 0.9f : 1f);
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    internal static void ChooseDetail(Bounds bounds, int layer, View[] views, int count, bool wasHidden, bool shadowsSuppressed, out bool hidden, out bool distant)
    {
        hidden = distant = false;
        var extents = bounds.extents;
        var radius = (float)Math.Sqrt(extents.x * extents.x + extents.y * extents.y + extents.z * extents.z);
        if (!Finite(radius) || !Finite(bounds.center.x) || !Finite(bounds.center.y) || !Finite(bounds.center.z)) return;
        var pixels = 0f; var distance = float.PositiveInfinity; var relevant = false;
        for (var i = 0; i < count; i++)
        {
            var view = views[i];
            if ((view.Mask & layer) == 0) continue;
            var delta = bounds.center - view.Position;
            var depth = delta.x * view.Forward.x + delta.y * view.Forward.y + delta.z * view.Forward.z;
            if (depth + radius < view.Near || depth - radius > view.Far) continue;
            relevant = true;
            var surfaceDistance = Math.Max(0f, (float)Math.Sqrt(delta.x * delta.x + delta.y * delta.y + delta.z * delta.z) - radius);
            distance = Math.Min(distance, surfaceDistance);
            pixels = Math.Max(pixels, ProjectedDiameter(radius, depth, view.FocalPixels, view.Orthographic));
        }
        if (!relevant || distance <= 20f) return;
        hidden = HideSmall(wasHidden, pixels, MinimumPixels);
        distant = SuppressShadows(shadowsSuppressed, distance, ShadowDistance);
    }

    internal static void Update()
    {
        if (!Enabled || Time.unscaledTime < _nextUpdate) return;
        _nextUpdate = Time.unscaledTime + 0.1f;
        try { UpdateCore(); }
        catch (Exception error)
        {
            Shutdown();
            Log?.Invoke("Renderer detail restored native presentation after " + error.GetType().Name + ": " + error.Message);
        }
    }

    private static void UpdateCore()
    {
        if (Camera.allCamerasCount > Cameras.Length) { Restore(); return; }
        var count = Camera.GetAllCameras(Cameras);
        var usable = 0;
        for (var i = 0; i < count; i++)
        {
            var camera = Cameras[i];
            if (camera == null || !camera.isActiveAndEnabled) continue;
            var focal = camera.orthographic ? camera.pixelHeight / (2f * camera.orthographicSize) : camera.pixelHeight / (2f * (float)Math.Tan(camera.fieldOfView * Math.PI / 360));
            // A custom projection can magnify beyond the fieldOfView property: retain native detail.
            var projection = camera.projectionMatrix;
            var matrixFocal = Math.Max(Math.Abs(projection.m11) * camera.pixelHeight, Math.Abs(projection.m00) * camera.pixelWidth) * 0.5f;
            focal = Math.Max(focal, matrixFocal);
            Views[usable++] = new View(camera.transform.position, camera.transform.forward, camera.cullingMask, focal, camera.nearClipPlane, camera.farClipPlane, camera.orthographic);
        }
        if (usable == 0) { Restore(); Array.Clear(Cameras, 0, Cameras.Length); return; }
        Removed.Clear();
        foreach (var pair in Entries)
        {
            var renderer = pair.Key; var entry = pair.Value;
            if (renderer == null) { Removed.Add(pair.Key); continue; }
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) { entry.Restore(); continue; }
            ChooseDetail(renderer.bounds, 1 << renderer.gameObject.layer, Views, usable, entry.Hidden, entry.ShadowsSuppressed, out var hidden, out var distant);
            entry.Apply(hidden, distant);
        }
        foreach (var renderer in Removed) Entries.Remove(renderer);
        Removed.Clear();
        Array.Clear(Cameras, 0, Cameras.Length);
    }

    internal static void Restore() { foreach (var entry in Entries.Values) entry.Restore(); }
    internal static void Shutdown()
    {
        Restore(); Entries.Clear(); Removed.Clear(); Array.Clear(Cameras, 0, Cameras.Length); Enabled = false; _nextUpdate = 0;
    }
}
