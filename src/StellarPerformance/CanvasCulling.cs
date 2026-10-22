using System;
using System.Collections.Generic;
using HarmonyLib;
using Ships.Interface.Model.Parts;
using Ships.Parts.Common.Model.Reactions;
using Ships.Parts.Common;
using Ships.Visuals;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StellarPerformance;

// Presentation only. Native scripts, raycasters, ID meshes and authoritative
// callbacks continue running. An uncertain visibility verdict always draws.
internal static class CanvasCulling
{
    static CanvasCulling() { }
    internal const int MaxCanvases = 4096, MaxCameras = 32, MaxGraphics = 64;
    private sealed class Entry
    {
        internal readonly ShipPartVisuals Owner;
        internal readonly ushort PartId;
        internal readonly Canvas Canvas;
        internal readonly RectTransform Rect;
        internal readonly Graphic[] Graphics;
        internal readonly TMP_InputField[] Inputs;
        internal readonly Bounds LocalBounds;
        internal readonly Rect Layout;
        internal readonly int Layers;
        internal readonly bool Passive;
        internal bool Hidden;
        internal Entry(ShipPartVisuals owner, ushort id, Canvas canvas, Graphic[] graphics)
        {
            Owner = owner; PartId = id; Canvas = canvas; Rect = (RectTransform)canvas.transform;
            Graphics = graphics; Inputs = canvas.GetComponentsInChildren<TMP_InputField>(true);
            Passive = canvas.GetComponentInParent<Ships.Parts.SignalDisplay.SignalDisplayVisuals>() != null || canvas.GetComponentInParent<Ships.Parts.GasMeter.GasMeterVisuals>() != null;
            Layout = Rect.rect;
            var bounds = new Bounds(new Vector3(Layout.center.x, Layout.center.y, 0), new Vector3(Layout.width, Layout.height, 0));
            var inverse = Rect.worldToLocalMatrix;
            foreach (var graphic in graphics)
            {
                Layers |= 1 << graphic.gameObject.layer;
                graphic.rectTransform.GetWorldCorners(Corners);
                for (var i = 0; i < 4; i++) bounds.Encapsulate(inverse.MultiplyPoint3x4(Corners[i]));
            }
            LocalBounds = bounds;
        }
        internal void Restore() { if (!Hidden) return; Hidden = false; if (Canvas != null) Canvas.enabled = true; }
    }
    private sealed class View
    {
        internal readonly Plane[] Planes = new Plane[6];
        internal Camera Camera = null!;
        internal int Layers;
    }
    private static readonly List<Entry> Entries = new List<Entry>();
    private static readonly HashSet<Canvas> Owned = new HashSet<Canvas>();
    private static readonly Camera[] Cameras = new Camera[MaxCameras];
    private static readonly View[] Views = new View[MaxCameras];
    private static readonly Vector3[] Corners = new Vector3[4];
    private static Action<Camera, Plane[]> _planes = null!;
    private static AccessTools.FieldRef<ShipPartVisuals, Dictionary<ushort, IShipPartVisual>> _parts = null!;
    private static bool _scan, _inside;
    internal static bool Enabled;
    internal static float LabelDistance = 0;
    internal static Action<string>? Log;

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var add = AccessTools.Method(typeof(ShipPartVisuals), nameof(ShipPartVisuals.AddStatefulPart));
        var remove = AccessTools.Method(typeof(ShipPartVisuals), nameof(ShipPartVisuals.RemoveStatefulPart));
        PatchSafety.RequireOriginalBody(add, "canvas registration", "stellarperformance.ship-part-batching");
        PatchSafety.RequireOriginalBody(remove, "canvas removal", "stellarperformance.ship-part-batching");
        _parts = AccessTools.FieldRefAccess<ShipPartVisuals, Dictionary<ushort, IShipPartVisual>>("_floatingParts");
        // Bind the array overload explicitly: Unity 6 also exposes Span overloads.
        _planes = (Action<Camera, Plane[]>)Delegate.CreateDelegate(typeof(Action<Camera, Plane[]>),
            typeof(GeometryUtility).GetMethod("CalculateFrustumPlanes", new[] { typeof(Camera), typeof(Plane[]) }) ?? throw new MissingMethodException("GeometryUtility array frustum overload"));
        for (var i = 0; i < Views.Length; i++) Views[i] = new View();
        harmony.Patch(add, postfix: new HarmonyMethod(typeof(CanvasCulling), nameof(AfterAdd)));
        harmony.Patch(remove, prefix: new HarmonyMethod(typeof(CanvasCulling), nameof(BeforeRemove)));
        _scan = true; Enabled = true;
        // Before uGUI's layout/clipping/graphic rebuild, after normal LateUpdate
        // cameras and billboards. Re-enabling can rebuild in this same draw.
        Canvas.preWillRenderCanvases += BeforeRender;
    }

    private static bool KnownPart(GameObject part)
    {
        var visual = part.GetComponent<DefaultShipPartVisuals>();
        if (visual == null || !FixedPart(visual.GetType())) return false;
        foreach (var script in part.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (script == null) return false;
            var assembly = script.GetType().Assembly.GetName().Name;
            if (assembly != "Ships" && assembly != "Core" && assembly != "Players" && assembly != "UI" &&
                assembly != "Unity.TextMeshPro" && assembly != "UnityEngine.UI") return false;
        }
        // A patched native visual may control Canvas.enabled itself.
        foreach (var name in new[] { "SetNetworkState", "ReactToNetworkStateChange", "Update", "LateUpdate" })
        {
            var method = visual.GetType().GetMethod(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method != null && !PatchSafety.HasOnlyOwners(method, Array.Empty<string>())) return false;
        }
        return true;
    }
    internal static bool FixedPart(Type type) => type == typeof(DefaultShipPartVisuals) || type == typeof(Ships.Parts.MathGate.MathGateVisuals) ||
        type == typeof(Ships.Parts.Constant.ConstantVisuals) || type == typeof(Ships.Parts.SignalTransmitter.SignalTransmitterVisuals) ||
        type == typeof(Ships.Parts.UnaryMathGate.SignalProcessorVisuals) || type == typeof(Ships.Parts.SignalDisplay.SignalDisplayVisuals) ||
        type == typeof(Ships.Parts.GasMeter.GasMeterVisuals) || type == typeof(Ships.Parts.SignalSplitter.SignalSplitterVisuals) || type == typeof(Ships.Parts.Beacon.BeaconVisual);
    private static void Register(ShipPartVisuals owner, ushort id, GameObject part)
    {
        if (!Enabled || Entries.Count >= MaxCanvases) return;
        var canvases = part.GetComponentsInChildren<Canvas>(true);
        if (canvases.Length == 0 || !KnownPart(part)) return;
        foreach (var canvas in canvases)
        {
            if (Entries.Count >= MaxCanvases) break;
            if (canvas.renderMode != RenderMode.WorldSpace || Owned.Contains(canvas)) continue;
            // Nested canvases and unknown graphics retain their native behavior.
            if (canvas.transform.parent != null && canvas.transform.parent.GetComponentInParent<Canvas>(true) != null) continue;
            var graphics = canvas.GetComponentsInChildren<Graphic>(true);
            if (graphics.Length == 0 || graphics.Length > MaxGraphics || canvas.GetComponentsInChildren<Canvas>(true).Length != 1) continue;
            if (!FixedLayout(canvas, graphics)) continue;
            Entries.Add(new Entry(owner, id, canvas, graphics)); Owned.Add(canvas);
        }
    }
    private static bool FixedLayout(Canvas canvas, Graphic[] graphics)
    {
        if (canvas.GetComponentsInChildren<LayoutGroup>(true).Length != 0 || canvas.GetComponentsInChildren<ContentSizeFitter>(true).Length != 0 ||
            canvas.GetComponentsInChildren<AspectRatioFitter>(true).Length != 0 || canvas.GetComponentsInChildren<Animator>(true).Length != 0 || canvas.GetComponentsInChildren<Animation>(true).Length != 0) return false;
        foreach (var script in canvas.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (script == null || !FixedCanvasScript(script.GetType())) return false;
            if (script.GetType().Assembly == typeof(Players.Utils.LookAtPlayerFlat4Sides).Assembly && script.transform != canvas.transform) return false;
        }
        foreach (var graphic in graphics)
        {
            // Native numeric readouts fit their fixed slots. Free-size overflow
            // text cannot be bounded from the slot and stays on the native path.
            if (graphic is TMP_Text text && !text.enableAutoSizing && text.overflowMode == TextOverflowModes.Overflow && text.GetComponentInParent<RectMask2D>(true) == null) return false;
        }
        return true;
    }
    internal static bool FixedCanvasScript(Type type) => type == typeof(CanvasScaler) || type == typeof(GraphicRaycaster) || type == typeof(TextMeshProUGUI) ||
        type == typeof(TMP_InputField) || type == typeof(TMP_SelectionCaret) || type == typeof(RectMask2D) || type == typeof(LayoutElement) ||
        type == typeof(Image) || type == typeof(RawImage) || type == typeof(Players.Utils.LookAtPlayerFlat2Sides) || type == typeof(Players.Utils.LookAtPlayerFlat4Sides);
    private static void AfterAdd(ShipPartVisuals __instance, StatefulPart part)
    {
        try { if (__instance.TryGetFloatingPartVisual(part.Id, out var visual)) Register(__instance, part.Id, visual.GameObject); }
        catch (Exception error) { Fail(error); }
    }
    private static void BeforeRemove(ShipPartVisuals __instance, StatefulPart part)
    {
        try
        {
            for (var i = Entries.Count - 1; i >= 0; i--)
                if (Entries[i].Owner == __instance && Entries[i].PartId == part.Id) Remove(i);
        }
        catch (Exception error) { Fail(error); }
    }
    private static void Remove(int index)
    {
        var entry = Entries[index]; entry.Restore(); Owned.Remove(entry.Canvas); Entries.RemoveAt(index);
    }
    private static void Discover()
    {
        _scan = false;
        foreach (var owner in UnityEngine.Object.FindObjectsByType<ShipPartVisuals>(FindObjectsSortMode.None))
            // Visual prefabs need not carry ShipPartIdentifier (physics prefabs
            // do). The inspected native map is also valid after scene cleanup.
            foreach (var pair in _parts(owner)) Register(owner, pair.Key, pair.Value.GameObject);
    }
    private static void BeforeRender()
    {
        if (!Enabled || _inside) return;
        _inside = true;
        try { Evaluate(); }
        catch (Exception error) { Fail(error); }
        finally { _inside = false; }
    }
    private static void Evaluate()
    {
        if (_scan) Discover();
        var count = Camera.allCamerasCount;
        if (count == 0 || count > MaxCameras) { RestoreAll(); return; }
        count = Camera.GetAllCameras(Cameras);
        var views = 0;
        for (var i = 0; i < count; i++)
        {
            var camera = Cameras[i];
            if (camera == null || !camera.isActiveAndEnabled) continue;
            var view = Views[views++]; view.Camera = camera; view.Layers = camera.cullingMask;
            _planes(camera, view.Planes);
        }
        var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
        for (var i = Entries.Count - 1; i >= 0; i--)
        {
            var entry = Entries[i]; var canvas = entry.Canvas;
            if (canvas == null || entry.Owner == null) { Remove(i); continue; }
            // Inspected native readouts change text, not slot layout. A root
            // resize invalidates the fixed envelope and returns ownership.
            if (entry.Rect.rect != entry.Layout) { Remove(i); continue; }
            if (!canvas.gameObject.activeInHierarchy || canvas.renderMode != RenderMode.WorldSpace || !canvas.isRootCanvas || views == 0 ||
                (selected != null && selected.transform.IsChildOf(entry.Rect))) { entry.Restore(); continue; }
            // Never enable a canvas disabled by its native owner.
            if (!entry.Hidden && !canvas.enabled) continue;
            var visible = false;
            foreach (var input in entry.Inputs) if (input != null && input.isFocused) { visible = true; break; }
            var bounds = TransformBounds(entry.LocalBounds, entry.Rect.localToWorldMatrix);
            bounds.Expand(0.5f); // Conservative presentation margin, no game values changed.
            if (!Finite(bounds.center) || !Finite(bounds.extents)) visible = true;
            var relevant = false;
            for (var v = 0; !visible && v < views; v++)
            {
                if ((Views[v].Layers & entry.Layers) == 0) continue;
                relevant = true;
                if (Intersects(Views[v].Planes, bounds)) visible = true;
            }
            // Camera replacements or an unrecognized layer setup must fail open.
            if (visible && LabelDistance > 0)
            {
                var nearby = false;
                for (var v = 0; v < views; v++)
                    if ((Views[v].Layers & entry.Layers) != 0 && (Views[v].Camera.transform.position - bounds.center).sqrMagnitude <= LabelDistance * LabelDistance)
                    { nearby = true; break; }
                // Selection/input focus takes precedence over distance limits.
                var focused = selected != null && selected.transform.IsChildOf(entry.Rect);
                foreach (var input in entry.Inputs) if (input != null && input.isFocused) { focused = true; break; }
                if (!nearby && !focused) visible = false;
            }
            if (visible && entry.Passive)
            {
                var anyText = false; var allTiny = true;
                foreach (var graphic in entry.Graphics)
                    if (graphic is TMP_Text text) { anyText = true; if (!VisualTuning.HideTinyLabel(text)) { allTiny = false; break; } }
                if (anyText && allTiny) visible = false;
            }
            if (!relevant || visible) entry.Restore();
            else if (canvas.enabled) { canvas.enabled = false; entry.Hidden = true; }
        }
        Array.Clear(Cameras, 0, Cameras.Length);
        for (var i = 0; i < views; i++) Views[i].Camera = null!;
    }
    internal static Bounds TransformBounds(Bounds local, Matrix4x4 matrix)
    {
        var ext = local.extents;
        var x = matrix.MultiplyVector(new Vector3(ext.x, 0, 0));
        var y = matrix.MultiplyVector(new Vector3(0, ext.y, 0));
        var z = matrix.MultiplyVector(new Vector3(0, 0, ext.z));
        return new Bounds(matrix.MultiplyPoint3x4(local.center), new Vector3(Math.Abs(x.x) + Math.Abs(y.x) + Math.Abs(z.x), Math.Abs(x.y) + Math.Abs(y.y) + Math.Abs(z.y), Math.Abs(x.z) + Math.Abs(y.z) + Math.Abs(z.z)) * 2);
    }
    private static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
    internal static bool Intersects(Plane[] planes, Bounds bounds)
    {
        if (planes.Length != 6 || !Finite(bounds.center) || !Finite(bounds.extents)) return true;
        for (var i = 0; i < planes.Length; i++)
        {
            // Unity's infinite-far projection can extract a NaN normal with
            // +Infinity distance for the last plane. It imposes no far limit.
            if (i == 5 && planes[i].distance == float.PositiveInfinity) continue;
            if (!Finite(planes[i].normal) || float.IsNaN(planes[i].distance) || float.IsInfinity(planes[i].distance)) return true;
        }
        var center = bounds.center; var ext = bounds.extents;
        for (var i = 0; i < planes.Length; i++)
        {
            if (i == 5 && planes[i].distance == float.PositiveInfinity) continue;
            var n = planes[i].normal;
            var distance = n.x * center.x + n.y * center.y + n.z * center.z + planes[i].distance;
            var radius = Math.Abs(n.x) * ext.x + Math.Abs(n.y) * ext.y + Math.Abs(n.z) * ext.z;
            if (distance + radius < 0) return false;
        }
        return true;
    }
    private static void RestoreAll() { foreach (var entry in Entries) entry.Restore(); }
    internal static void Clear()
    {
        try { RestoreAll(); }
        finally { Entries.Clear(); Owned.Clear(); Array.Clear(Cameras, 0, Cameras.Length); for (var i = 0; i < Views.Length; i++) if (Views[i] != null) Views[i].Camera = null!; _scan = true; }
    }
    internal static void Shutdown() { Enabled = false; Canvas.preWillRenderCanvases -= BeforeRender; Clear(); }
    private static void Fail(Exception error) { Shutdown(); Log?.Invoke("Canvas culling stopped; native canvases restored: " + error.GetBaseException().Message); }
}
