using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Parts.Common;
using Ships.Parts.GasMeter;
using Ships.Parts.MathGate;
using Ships.Parts.SignalDisplay;
using Ships.Parts.UnaryMathGate;
using Ships.Visuals;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StellarPerformance;

internal static class WorldUiVisibility
{
    static WorldUiVisibility() { }

    internal sealed class PendingDisplay
    {
        internal IStatefulPartState? Displayed, Latest;
        internal bool Pending;
        internal bool Observe(IStatefulPartState next, bool visible)
        {
            Latest = next;
            if (Displayed != null && SamePresentation(Displayed, next)) { Pending = false; return false; }
            Pending = !visible;
            if (visible) Displayed = next;
            return visible;
        }
    }

    private sealed class Display
    {
        internal readonly DefaultShipPartVisuals Visual;
        internal readonly PendingDisplay State = new PendingDisplay();
        internal readonly List<Group> Groups = new List<Group>();
        internal Display(DefaultShipPartVisuals visual) { Visual = visual; }
    }

    private sealed class Group
    {
        internal readonly Canvas Canvas;
        internal readonly RectTransform[] Rects;
        internal readonly TMP_InputField[] Inputs;
        internal bool Suppressed, Visible = true;
        internal bool BoundsReady;
        internal Bounds LocalBounds;
        internal Group(Canvas canvas, RectTransform[] rects, TMP_InputField[] inputs) { Canvas = canvas; Rects = rects; Inputs = inputs; }
        internal void Restore()
        {
            if (Suppressed && Canvas != null) Canvas.enabled = true;
            Suppressed = false;
        }
    }

    private static readonly ConditionalWeakTable<DefaultShipPartVisuals, Display> Displays = new ConditionalWeakTable<DefaultShipPartVisuals, Display>();
    private static readonly List<Display> Tracked = new List<Display>();
    private static readonly List<Group> Groups = new List<Group>();
    private static readonly Dictionary<RectTransform, Group> RectGroups = new Dictionary<RectTransform, Group>();
    private static readonly Vector3[] Corners = new Vector3[4];
    private static readonly Plane[][] Frusta = CreateFrusta();
    // Bind the array overload explicitly: Unity 6 also exposes Span overloads, which
    // must not pull netstandard 2.1 into this net472 mod's compile-time contract.
    private static readonly Action<Camera, Plane[]> CalculatePlanes = (Action<Camera, Plane[]>)Delegate.CreateDelegate(typeof(Action<Camera, Plane[]>), AccessTools.Method(typeof(GeometryUtility), nameof(GeometryUtility.CalculateFrustumPlanes), new[] { typeof(Camera), typeof(Plane[]) }));
    private static readonly int[] Masks = new int[32];
    private static Camera[] Cameras = new Camera[32];
    private static bool _installed;
    private static bool _updating;
    internal static Action<string>? Log = null;
    internal static bool CullCanvases = true;
    internal static float MaximumDistance = 0f; // Zero preserves the game's drawing distance.
    internal static long Deferred, Unchanged;
    internal static int VisibleCanvases, HiddenCanvases;

    private static Plane[][] CreateFrusta()
    {
        var result = new Plane[32][];
        for (var i = 0; i < result.Length; i++) result[i] = new Plane[6];
        return result;
    }

    internal static bool SamePresentation(IStatefulPartState a, IStatefulPartState b)
    {
        if (a.GetType() != b.GetType()) return false;
        if (a.GetType() == typeof(SignalProcessorState)) return ((SignalProcessorState)a).Operation == ((SignalProcessorState)b).Operation;
        if (a.GetType() == typeof(MathGateState)) return ((MathGateState)a).Operation == ((MathGateState)b).Operation;
        if (a.GetType() == typeof(SignalDisplayState)) return NativeStateReuse.Same(((SignalDisplayState)a).SignalValue, ((SignalDisplayState)b).SignalValue);
        if (a.GetType() == typeof(GasMeterState)) return NativeStateReuse.Same(((GasMeterState)a).Density, ((GasMeterState)b).Density);
        return false;
    }

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        foreach (var type in new[] { typeof(SignalProcessorVisuals), typeof(MathGateVisuals), typeof(SignalDisplayVisuals), typeof(GasMeterVisuals) })
        {
            var initialize = AccessTools.DeclaredMethod(type, nameof(DefaultShipPartVisuals.SetNetworkState));
            var change = AccessTools.DeclaredMethod(type, nameof(DefaultShipPartVisuals.ReactToNetworkStateChange));
            if (!PatchSafety.HasOnlyOwners(initialize, Array.Empty<string>()) || !PatchSafety.HasOnlyOwners(change, Array.Empty<string>()))
                throw new InvalidOperationException(type.Name + " has an unknown presentation owner.");
            harmony.Patch(initialize, postfix: new HarmonyMethod(typeof(WorldUiVisibility), nameof(AfterInitialize)));
            harmony.Patch(change, prefix: new HarmonyMethod(typeof(WorldUiVisibility), nameof(BeforeChange)));
        }
        harmony.Patch(AccessTools.Method(typeof(ShipPartVisuals), nameof(ShipPartVisuals.AddStatefulPart)), postfix: new HarmonyMethod(typeof(WorldUiVisibility), nameof(AfterAdd)));
        foreach (var name in new[] { "Hide", "Show" })
            harmony.Patch(AccessTools.Method(typeof(UI.Common.HideableCanvas), name), prefix: new HarmonyMethod(typeof(WorldUiVisibility), nameof(BeforeNativeVisibility)));
        _installed = true;
        Canvas.preWillRenderCanvases += Update;
        RectTransform.reapplyDrivenProperties += OnRectChanged;
    }

    internal static void AfterAdd(ShipPartVisuals __instance, StatefulPart part)
    {
        if (!_installed) return;
        if (__instance.TryGetFloatingPartVisual(part.Id, out var visual) && visual is DefaultShipPartVisuals native && native.GetType().Assembly == typeof(DefaultShipPartVisuals).Assembly)
            Register(native);
    }

    private static Display Register(DefaultShipPartVisuals visual)
    {
        if (Displays.TryGetValue(visual, out var existing)) return existing;
        var display = new Display(visual);
        foreach (var canvas in visual.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas.renderMode != RenderMode.WorldSpace || !canvas.isRootCanvas) continue;
            var graphics = canvas.GetComponentsInChildren<Graphic>(true);
            if (graphics.Length == 0 || graphics.Length > 128) continue;
            var rects = new RectTransform[graphics.Length];
            for (var i = 0; i < graphics.Length; i++) rects[i] = graphics[i].rectTransform;
            var group = new Group(canvas, rects, canvas.GetComponentsInChildren<TMP_InputField>(true));
            foreach (var rect in rects) RectGroups[rect] = group;
            display.Groups.Add(group); Groups.Add(group);
        }
        Displays.Add(visual, display);
        var type = visual.GetType();
        if (type == typeof(SignalProcessorVisuals) || type == typeof(MathGateVisuals) || type == typeof(SignalDisplayVisuals) || type == typeof(GasMeterVisuals)) Tracked.Add(display);
        return display;
    }

    internal static void AfterInitialize(DefaultShipPartVisuals __instance, IStatefulPartState state)
    {
        var type = __instance.GetType();
        if (type != typeof(SignalProcessorVisuals) && type != typeof(MathGateVisuals) && type != typeof(SignalDisplayVisuals) && type != typeof(GasMeterVisuals)) return;
        var display = Register(__instance);
        display.State.Displayed = state; display.State.Latest = state; display.State.Pending = false;
        for (var i = 0; i < display.Groups.Count; i++) display.Groups[i].BoundsReady = false;
    }

    internal static bool BeforeChange(DefaultShipPartVisuals __instance, StatefulPartStateChangeEvent change)
    {
        if (!_installed) return true;
        if (change.NewState == null) return true;
        var type = change.NewState.GetType();
        if (type != typeof(SignalProcessorState) && type != typeof(MathGateState) && type != typeof(SignalDisplayState) && type != typeof(GasMeterState)) return true;
        if (!Displays.TryGetValue(__instance, out var display)) return true;
        var visible = display.Groups.Count == 0;
        for (var i = 0; i < display.Groups.Count; i++) visible |= display.Groups[i].Visible;
        var run = display.State.Observe(change.NewState, visible);
        if (run) for (var i = 0; i < display.Groups.Count; i++) display.Groups[i].BoundsReady = false;
        if (!run && DiagnosticCounters.Active) { if (display.State.Pending) Deferred++; else Unchanged++; }
        return run;
    }

    internal static void BeforeNativeVisibility(UI.Common.HideableCanvas __instance)
    {
        var canvas = __instance.GetComponent<Canvas>();
        for (var i = 0; i < Groups.Count; i++) if (Groups[i].Canvas == canvas) Groups[i].Restore();
    }

    // Called before Unity builds UI geometry. A display entering any relevant camera is
    // refreshed in this same frame, before its Canvas is re-enabled for rendering.
    internal static void Update()
    {
        if (_updating || !_installed) return;
        _updating = true;
        try { UpdateCore(); }
        catch (Exception error)
        {
            _installed = false; Canvas.preWillRenderCanvases -= Update;
            Restore();
            Log?.Invoke("World UI visibility retained native rendering after " + error.GetType().Name + ": " + error.Message);
        }
        finally { _updating = false; }
    }

    private static void UpdateCore()
    {
        var cameraCount = Camera.allCamerasCount;
        if (cameraCount > Cameras.Length) { Restore(); return; }
        var count = Camera.GetAllCameras(Cameras);
        var usable = 0;
        for (var i = 0; i < count; i++)
        {
            var camera = Cameras[i];
            if (camera == null || !camera.isActiveAndEnabled) continue;
            Cameras[usable] = camera; Masks[usable] = camera.cullingMask;
            CalculatePlanes(camera, Frusta[usable]); usable++;
        }
        if (DiagnosticCounters.Active) VisibleCanvases = HiddenCanvases = 0;
        for (var i = Groups.Count - 1; i >= 0; i--)
        {
            var group = Groups[i];
            if (group.Canvas == null)
            {
                foreach (var rect in group.Rects) RectGroups.Remove(rect);
                Groups.RemoveAt(i); continue;
            }
            if (!group.Canvas.gameObject.activeInHierarchy || (!group.Suppressed && !group.Canvas.enabled)) { group.Visible = false; continue; }
            var focused = false;
            for (var j = 0; j < group.Inputs.Length; j++) if (group.Inputs[j] != null && group.Inputs[j].isFocused) focused = true;
            if (!group.BoundsReady) group.BoundsReady = TryLocalBounds(group, out group.LocalBounds);
            var valid = group.BoundsReady;
            var bounds = valid ? TransformBounds(group.LocalBounds, group.Canvas.transform.localToWorldMatrix) : default;
            if (valid) bounds.Expand(2f);
            var visible = focused || !valid || usable == 0;
            if (!visible)
                for (var j = 0; j < usable; j++)
                    if ((Masks[j] & (1 << group.Canvas.gameObject.layer)) != 0 && Intersects(bounds, Frusta[j]) &&
                        (MaximumDistance <= 0 || bounds.SqrDistance(Cameras[j].transform.position) <= MaximumDistance * MaximumDistance)) { visible = true; break; }
            group.Visible = visible;
            if (visible) { group.Restore(); if (DiagnosticCounters.Active) VisibleCanvases++; }
            else
            {
                if (DiagnosticCounters.Active) HiddenCanvases++;
                if (CullCanvases && group.Canvas.enabled) { group.Suppressed = true; group.Canvas.enabled = false; }
            }
        }
        for (var i = Tracked.Count - 1; i >= 0; i--)
        {
            var display = Tracked[i];
            if (display.Visual == null) { Tracked.RemoveAt(i); continue; }
            if (!display.State.Pending || display.State.Latest == null) continue;
            var visible = false;
            for (var j = 0; j < display.Groups.Count; j++) visible |= display.Groups[j].Visible;
            if (visible) display.Visual.SetNetworkState(display.State.Latest);
        }
        Array.Clear(Cameras, usable, Cameras.Length - usable);
    }

    private static void OnRectChanged(RectTransform rect)
    {
        if (RectGroups.TryGetValue(rect, out var group)) group.BoundsReady = false;
    }

    private static bool TryLocalBounds(Group group, out Bounds bounds)
    {
        bounds = default; var found = false;
        var rects = group.Rects;
        var toLocal = group.Canvas.transform.worldToLocalMatrix;
        for (var i = 0; i < rects.Length; i++)
        {
            var rect = rects[i];
            if (rect == null) continue;
            rect.GetWorldCorners(Corners);
            for (var j = 0; j < 4; j++)
            {
                var p = toLocal.MultiplyPoint3x4(Corners[j]);
                if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z)) return false;
                if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; } else bounds.Encapsulate(p);
            }
        }
        return found;
    }

    internal static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
    {
        var center = matrix.MultiplyPoint3x4(bounds.center); var e = bounds.extents;
        var extents = new Vector3(Math.Abs(matrix.m00) * e.x + Math.Abs(matrix.m01) * e.y + Math.Abs(matrix.m02) * e.z,
            Math.Abs(matrix.m10) * e.x + Math.Abs(matrix.m11) * e.y + Math.Abs(matrix.m12) * e.z,
            Math.Abs(matrix.m20) * e.x + Math.Abs(matrix.m21) * e.y + Math.Abs(matrix.m22) * e.z);
        return new Bounds(center, extents * 2f);
    }

    internal static bool Intersects(Bounds bounds, Plane[] planes)
    {
        var center = bounds.center; var extents = bounds.extents;
        for (var i = 0; i < planes.Length; i++)
        {
            var n = planes[i].normal;
            var distance = n.x * center.x + n.y * center.y + n.z * center.z + planes[i].distance;
            var radius = Math.Abs(n.x) * extents.x + Math.Abs(n.y) * extents.y + Math.Abs(n.z) * extents.z;
            if (distance + radius < 0f) return false;
        }
        return true;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal static void Restore()
    {
        for (var i = 0; i < Groups.Count; i++) { Groups[i].Restore(); Groups[i].Visible = true; }
        for (var i = 0; i < Tracked.Count; i++)
        {
            var display = Tracked[i];
            if (display.Visual != null && display.State.Pending && display.State.Latest != null) display.Visual.SetNetworkState(display.State.Latest);
        }
    }
    internal static void Shutdown()
    {
        if (_installed) Canvas.preWillRenderCanvases -= Update;
        RectTransform.reapplyDrivenProperties -= OnRectChanged;
        Restore(); Groups.Clear(); Tracked.Clear(); RectGroups.Clear(); _installed = false;
        Array.Clear(Cameras, 0, Cameras.Length);
    }
}
