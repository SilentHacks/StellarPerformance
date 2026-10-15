using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using HarmonyLib;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.State;
using Ships.Parts.Common;
using Ships.Parts.FluidValve;
using Ships.Parts.FuelTank;
using Ships.Parts.MathGate;
using Ships.Parts.SignalDisplay;
using Ships.Parts.SignalSplitter;
using Ships.Parts.SignalTransmitter;
using Ships.Parts.Thruster;
using Ships.Parts.UnaryMathGate;
using Ships.Visuals;
using UnityEngine;
using UnityEngine.Rendering;

namespace StellarPerformance;

// An opt-in prototype. Only fixed opaque geometry using known object-space-independent
// stock URP shaders is combined. Gameplay objects, ID rendering and effects stay native.
internal static class ShipPartBatching
{
    static ShipPartBatching() { }

    private readonly struct Pose
    {
        private readonly Transform _transform;
        private readonly Vector3 _position, _scale;
        private readonly Quaternion _rotation;
        internal Pose(Transform transform) { _transform = transform; _position = transform.localPosition; _scale = transform.localScale; _rotation = transform.localRotation; }
        internal bool Valid() => _transform != null && Equal(_transform.localPosition, _position) && Equal(_transform.localScale, _scale) && Equal(_transform.localRotation, _rotation);
    }
    private sealed class Source
    {
        internal readonly MeshRenderer Renderer;
        internal readonly MeshFilter Filter;
        internal readonly Mesh Mesh;
        internal readonly Material Material;
        internal readonly Transform Part;
        internal readonly Vector3 PartPosition, PartScale, Position, Scale;
        internal readonly Quaternion PartRotation, Rotation;
        internal readonly int Layer;
        internal readonly ShadowCastingMode Shadows;
        internal readonly bool ReceiveShadows;
        internal readonly uint RenderingLayers;
        internal readonly int Channels;
        private readonly Pose[] _intermediates;
        internal Source(MeshRenderer renderer, MeshFilter filter, Transform part)
        {
            Renderer = renderer; Filter = filter; Mesh = filter.sharedMesh; Material = renderer.sharedMaterial; Part = part;
            PartPosition = part.localPosition; PartRotation = part.localRotation; PartScale = part.localScale;
            Position = renderer.transform.localPosition; Rotation = renderer.transform.localRotation; Scale = renderer.transform.localScale;
            Layer = renderer.gameObject.layer; Shadows = renderer.shadowCastingMode; ReceiveShadows = renderer.receiveShadows; RenderingLayers = renderer.renderingLayerMask;
            for (var i = 0; i < 14; i++) if (Mesh.HasVertexAttribute((VertexAttribute)i)) Channels |= 1 << i;
            var intermediates = new List<Pose>();
            for (var parent = renderer.transform == part ? null : renderer.transform.parent; parent != null && parent != part; parent = parent.parent) intermediates.Add(new Pose(parent));
            _intermediates = intermediates.ToArray();
        }
        internal bool Valid() => Renderer != null && Filter != null && Part != null && Renderer.gameObject.activeInHierarchy &&
            !Renderer.enabled && !Renderer.forceRenderingOff && Renderer.gameObject.layer == Layer && Renderer.shadowCastingMode == Shadows && Renderer.receiveShadows == ReceiveShadows && Renderer.renderingLayerMask == RenderingLayers &&
            Filter.sharedMesh == Mesh && Renderer.sharedMaterial == Material && !Renderer.HasPropertyBlock() &&
            Equal(Part.localPosition, PartPosition) && Equal(Part.localScale, PartScale) && Equal(Part.localRotation, PartRotation) &&
            Equal(Renderer.transform.localPosition, Position) && Equal(Renderer.transform.localScale, Scale) && Equal(Renderer.transform.localRotation, Rotation) && ValidIntermediates();
        private bool ValidIntermediates() { for (var i = 0; i < _intermediates.Length; i++) if (!_intermediates[i].Valid()) return false; return true; }
    }

    private sealed class Batch
    {
        internal readonly List<Source> Sources;
        internal readonly Mesh Mesh;
        internal readonly GameObject Object;
        internal Batch(List<Source> sources, Mesh mesh, GameObject obj) { Sources = sources; Mesh = mesh; Object = obj; }
        internal void Restore()
        {
            // Disable the replacement immediately; Destroy is deferred by Unity.
            if (Object != null) { Object.SetActive(false); UnityEngine.Object.Destroy(Object); }
            for (var i = 0; i < Sources.Count; i++) if (Sources[i].Renderer != null) Sources[i].Renderer.enabled = true;
            if (Mesh != null) UnityEngine.Object.Destroy(Mesh);
        }
    }

    private sealed class Ship
    {
        internal readonly ShipPartVisuals Visuals;
        internal readonly List<Batch> Batches = new List<Batch>();
        internal bool Dirty = true;
        internal int ReadyFrame;
        internal Ship(ShipPartVisuals visuals) { Visuals = visuals; ReadyFrame = Time.frameCount + 3; }
        internal void Restore() { for (var i = 0; i < Batches.Count; i++) Batches[i].Restore(); Batches.Clear(); }
    }

    private readonly struct Key : IEquatable<Key>
    {
        internal readonly Material Material;
        internal readonly int X, Y, Z, Layer, Channels;
        internal readonly ShadowCastingMode Shadows;
        internal readonly bool ReceiveShadows;
        internal readonly uint RenderingLayers;
        internal Key(Source source)
        {
            Material = source.Material; var p = source.PartPosition;
            X = Cell(p.x); Y = Cell(p.y); Z = Cell(p.z); Layer = source.Renderer.gameObject.layer;
            Shadows = source.Renderer.shadowCastingMode; ReceiveShadows = source.Renderer.receiveShadows; RenderingLayers = source.Renderer.renderingLayerMask;
            Channels = source.Channels;
        }
        public bool Equals(Key other) => Material == other.Material && X == other.X && Y == other.Y && Z == other.Z && Layer == other.Layer && Channels == other.Channels && Shadows == other.Shadows && ReceiveShadows == other.ReceiveShadows && RenderingLayers == other.RenderingLayers;
        public override bool Equals(object? obj) => obj is Key other && Equals(other);
        public override int GetHashCode() => unchecked(Material.GetInstanceID() * 397 ^ X * 31 ^ Y * 17 ^ Z ^ Layer ^ (int)RenderingLayers);
    }

    private static readonly Dictionary<ShipPartVisuals, Ship> Ships = new Dictionary<ShipPartVisuals, Ship>();
    private static readonly List<ShipPartVisuals> Removed = new List<ShipPartVisuals>();
    internal static bool Enabled;
    internal static bool ContinuousValidation = false;
    internal static int SourceRenderers, ReplacementRenderers, InspectedRenderers, UnsupportedRenderers;
    internal static Action<string>? Log;
    internal const string InspectedAssetHash = "86A3108EFBC4C48DE335949E38B0CA59069376975C58085E7F261A558908289F";
    private static bool _customShader;
    internal static bool SupportedShader(string name, bool inspectedGameAsset = false) => name == "Universal Render Pipeline/Lit" || name == "Universal Render Pipeline/Simple Lit" || name == "Universal Render Pipeline/Unlit" || (inspectedGameAsset && name == "Custom/PlanetObject");
    internal static bool MatchesShaderAsset(string path)
    {
        using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path))
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty) == InspectedAssetHash;
    }

    internal static bool UniformPositive(Matrix4x4 m)
    {
        var x = m.m00 * m.m00 + m.m10 * m.m10 + m.m20 * m.m20;
        var y = m.m01 * m.m01 + m.m11 * m.m11 + m.m21 * m.m21;
        var z = m.m02 * m.m02 + m.m12 * m.m12 + m.m22 * m.m22;
        if (float.IsNaN(x) || float.IsInfinity(x) || x < 1e-12f) return false;
        var tolerance = x * 1e-5f;
        if (Math.Abs(x - y) > tolerance || Math.Abs(x - z) > tolerance) return false;
        if (Math.Abs(m.m00 * m.m01 + m.m10 * m.m11 + m.m20 * m.m21) > tolerance ||
            Math.Abs(m.m00 * m.m02 + m.m10 * m.m12 + m.m20 * m.m22) > tolerance ||
            Math.Abs(m.m01 * m.m02 + m.m11 * m.m12 + m.m21 * m.m22) > tolerance) return false;
        return m.m00 * (m.m11 * m.m22 - m.m12 * m.m21) - m.m01 * (m.m10 * m.m22 - m.m12 * m.m20) + m.m02 * (m.m10 * m.m21 - m.m11 * m.m20) > 0;
    }
    internal static int Cell(float coordinate) => (int)Math.Floor(coordinate / 16f);
    internal static bool Equal(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
    internal static bool Equal(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        if (!ContinuousValidation)
        {
            var allowed = new[] { "stellarperformance.part-visual-updates", "stellarperformance.world-ui-visibility" };
            foreach (var type in new[] { typeof(ThrusterVisual), typeof(FluidTankVisuals), typeof(SignalTransmitterVisuals), typeof(SignalProcessorVisuals), typeof(MathGateVisuals), typeof(SignalSplitterVisuals), typeof(SignalDisplayVisuals), typeof(FluidValveVisual) })
                foreach (var name in new[] { "SetNetworkState", "ReactToNetworkStateChange" })
                {
                    var method = AccessTools.Method(type, name);
                    if (method != null && !PatchSafety.HasOnlyOwners(method, allowed))
                        throw new InvalidOperationException("A mod changes " + type.Name + "." + name + "; continuous validation is required.");
                }
        }
        var data = Path.GetDirectoryName(Path.GetDirectoryName(typeof(ShipPartVisuals).Assembly.Location))!;
        _customShader = MatchesShaderAsset(Path.Combine(data, "sharedassets0.assets"));
        Log?.Invoke("Inspected Custom/PlanetObject shader asset: " + (_customShader ? "verified" : "changed; stock URP shaders only"));
        harmony.Patch(AccessTools.Method(typeof(ShipPartVisuals), nameof(ShipPartVisuals.AddStatefulPart)), postfix: new HarmonyMethod(typeof(ShipPartBatching), nameof(AfterAdd)));
        harmony.Patch(AccessTools.Method(typeof(ShipPartVisuals), nameof(ShipPartVisuals.RemoveStatefulPart)), prefix: new HarmonyMethod(typeof(ShipPartBatching), nameof(BeforeRemove)));
        foreach (var name in new[] { "HighlightFloatingPart", "RemoveFloatingPartHighlight" })
            harmony.Patch(AccessTools.Method(typeof(ShipPartVisuals), name), prefix: new HarmonyMethod(typeof(ShipPartBatching), nameof(BeforeHighlight)));
        foreach (var name in new[] { "HoverEnter", "HoverLeave" })
            harmony.Patch(AccessTools.Method(typeof(Ships.Parts.Common.Services.ShipInteractionHoverToggler), name), prefix: new HarmonyMethod(typeof(ShipPartBatching), nameof(BeforeHover)));
        harmony.Patch(AccessTools.Method(typeof(ShipPartVisuals), nameof(ShipPartVisuals.OnStatefulPartStateChange)), prefix: new HarmonyMethod(typeof(ShipPartBatching), nameof(BeforePartStateChange)));
        Enabled = true;
    }

    internal static void AfterAdd(ShipPartVisuals __instance)
    {
        if (!Ships.TryGetValue(__instance, out var ship)) Ships.Add(__instance, ship = new Ship(__instance));
        ship.Dirty = true; ship.ReadyFrame = Time.frameCount + 3;
    }
    internal static void BeforeRemove(ShipPartVisuals __instance, StatefulPart part)
    {
        RestorePart(__instance, part.Id);
        if (Ships.TryGetValue(__instance, out var ship)) { ship.Dirty = true; ship.ReadyFrame = Time.frameCount + 3; }
    }
    internal static void BeforeHighlight(ShipPartVisuals __instance, ushort id) => RestorePart(__instance, id);
    internal static void BeforeHover(PartContext context)
    {
        foreach (var pair in Ships) if (pair.Key != null && pair.Key.ShipId == context.ShipId) RestorePart(pair.Key, context.PartId);
    }

    private static void RestorePart(ShipPartVisuals visuals, ushort id)
    {
        if (!Ships.TryGetValue(visuals, out var ship) || !visuals.TryGetFloatingPartVisual(id, out var visual)) return;
        var transform = visual.GameObject.transform;
        for (var i = ship.Batches.Count - 1; i >= 0; i--)
        {
            var batch = ship.Batches[i];
            for (var j = 0; j < batch.Sources.Count; j++)
                if (batch.Sources[j].Part == transform) { batch.Restore(); ship.Batches.RemoveAt(i); break; }
        }
        // Keep these parts native until the next topology edit. Hovering or moving a
        // valve must not trigger whole-ship mesh rebuilds or disable unrelated batches.
    }

    internal static void BeforePartStateChange(ShipPartVisuals __instance, StatefulPartStateChangeEvent change)
    {
        // Of the supported fixed visual classes, only a valve moves an opaque child
        // transform. Other changes affect excluded screens, particles or liquid shaders.
        if (NeedsPoseInvalidation(change)) RestorePart(__instance, change.PartId);
    }

    internal static bool NeedsPoseInvalidation(StatefulPartStateChangeEvent change) =>
        change.NewState is FluidValveState next && (!(change.PreviousState is FluidValveState previous) || !NativeStateReuse.Same(previous.ValveOpenedRate, next.ValveOpenedRate));

    internal static void Update()
    {
        if (!Enabled) return;
        Removed.Clear();
        foreach (var pair in Ships)
        {
            var ship = pair.Value;
            if (ship.Visuals == null) { ship.Restore(); Removed.Add(pair.Key); continue; }
            var invalid = false;
            if (ContinuousValidation)
                for (var i = 0; i < ship.Batches.Count && !invalid; i++)
                    for (var j = 0; j < ship.Batches[i].Sources.Count; j++) if (!ship.Batches[i].Sources[j].Valid()) { invalid = true; break; }
            if (invalid) { ship.Restore(); ship.Dirty = false; Log?.Invoke("Ship part batch restored after a renderer/material/transform change; this ship stays native until its next topology change."); }
            if (!ship.Dirty || Time.frameCount < ship.ReadyFrame) continue;
            ship.Dirty = false;
            try { Build(ship); }
            catch (Exception error) { ship.Restore(); Log?.Invoke("Ship part batching retained native rendering: " + error.GetType().Name + ": " + error.Message); }
        }
        for (var i = 0; i < Removed.Count; i++) Ships.Remove(Removed[i]);
        SourceRenderers = ReplacementRenderers = 0;
        foreach (var pair in Ships) foreach (var batch in pair.Value.Batches) { SourceRenderers += batch.Sources.Count; ReplacementRenderers++; }
    }

    private static bool FixedBody(DefaultShipPartVisuals part)
    {
        var type = part.GetType();
        if (!(type == typeof(ThrusterVisual) || type == typeof(FluidTankVisuals) || type == typeof(SignalTransmitterVisuals) || type == typeof(SignalProcessorVisuals) || type == typeof(MathGateVisuals) || type == typeof(SignalSplitterVisuals) || type == typeof(SignalDisplayVisuals) || type == typeof(FluidValveVisual))) return false;
        // Native fixed bodies can be event-driven. Unknown prefab scripts require
        // continuous verification and otherwise retain their native renderers.
        if (!ContinuousValidation)
            foreach (var component in part.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null) return false;
                var assembly = component.GetType().Assembly.GetName().Name ?? string.Empty;
                if (assembly != "Ships" && assembly != "Core" && assembly != "Players" && assembly != "UI" && assembly != "Unity.TextMeshPro" && !assembly.StartsWith("UnityEngine.", StringComparison.Ordinal)) return false;
            }
        return true;
    }

    private static bool FixedHierarchy(Transform transform, Transform part)
    {
        for (var depth = 0; depth < 8 && transform != null; depth++, transform = transform.parent)
        {
            if (transform == part) return true;
            // Gauges, screen billboards and scripted moving subassemblies remain native.
            if (transform.GetComponents<MonoBehaviour>().Length != 0) return false;
        }
        return false;
    }

    private static void Build(Ship ship)
    {
        var byKey = new Dictionary<Key, List<Source>>();
        var inspected = 0; var rejected = 0;
        foreach (var part in ship.Visuals.GetComponentsInChildren<DefaultShipPartVisuals>())
        {
            if (!FixedBody(part)) continue;
            foreach (var renderer in part.GetComponentsInChildren<MeshRenderer>())
            {
                inspected++;
                var filter = renderer.GetComponent<MeshFilter>();
                var material = renderer.sharedMaterial;
                var transform = renderer.transform;
                // A fixed body with matching vertex channels/material, no scripted
                // intermediate transform, and only inspected rendering dependencies.
                if (!renderer.enabled || renderer.forceRenderingOff || filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable || filter.sharedMesh.subMeshCount != 1 ||
                    renderer.sharedMaterials.Length != 1 || material == null || material.shader == null || !SupportedShader(material.shader.name, _customShader) || material.renderQueue > 2500 || material.renderQueue < 0 ||
                    renderer.HasPropertyBlock() || renderer.additionalVertexStreams != null || filter.sharedMesh.blendShapeCount != 0 ||
                    ((material.shader.name != "Custom/PlanetObject") && (renderer.lightProbeUsage != LightProbeUsage.Off || renderer.reflectionProbeUsage != ReflectionProbeUsage.Off)) || renderer.lightmapIndex >= 0 ||
                    !UniformPositive(ship.Visuals.transform.worldToLocalMatrix * renderer.localToWorldMatrix) ||
                    !FixedHierarchy(transform, part.transform) || renderer.GetComponentInParent<LODGroup>() != null || renderer.GetComponentInParent<Animator>() != null ||
                    renderer.GetComponentInParent<Animation>() != null || renderer.GetComponent<TMPro.TMP_Text>() != null)
                { rejected++; continue; }
                var source = new Source(renderer, filter, part.transform);
                var key = new Key(source);
                if (!byKey.TryGetValue(key, out var sources)) byKey.Add(key, sources = new List<Source>());
                sources.Add(source);
            }
        }
        InspectedRenderers += inspected; UnsupportedRenderers += rejected;
        var batches = 0; var combined = 0;
        foreach (var pair in byKey)
            foreach (var sources in Partition(pair.Value))
            {
                var instances = new CombineInstance[sources.Count];
                for (var i = 0; i < sources.Count; i++) instances[i] = new CombineInstance { mesh = sources[i].Mesh, subMeshIndex = 0, transform = ship.Visuals.transform.worldToLocalMatrix * sources[i].Renderer.localToWorldMatrix };
                var mesh = new Mesh { name = "StellarPerformance.FixedPartBatch" };
                GameObject? obj = null;
                try
                {
                    mesh.CombineMeshes(instances, true, true, false);
                    mesh.RecalculateBounds();
                    obj = new GameObject("StellarPerformance.FixedPartBatch");
                    obj.transform.SetParent(ship.Visuals.transform, false); obj.layer = pair.Key.Layer;
                    obj.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var output = obj.AddComponent<MeshRenderer>();
                    output.sharedMaterial = pair.Key.Material; output.shadowCastingMode = pair.Key.Shadows;
                    output.receiveShadows = pair.Key.ReceiveShadows; output.renderingLayerMask = pair.Key.RenderingLayers;
                    output.lightProbeUsage = LightProbeUsage.Off; output.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    ship.Batches.Add(new Batch(sources, mesh, obj));
                    foreach (var source in sources) source.Renderer.enabled = false;
                    batches++; combined += sources.Count;
                }
                catch { if (obj != null) { obj.SetActive(false); UnityEngine.Object.Destroy(obj); } UnityEngine.Object.Destroy(mesh); throw; }
            }
        Log?.Invoke("Ship part batching: inspected=" + inspected + ", unsupported=" + rejected + ", source renderers=" + combined + ", replacement renderers=" + batches + ".");
    }

    private static IEnumerable<List<Source>> Partition(List<Source> sources)
    {
        var chunk = new List<Source>(); var vertices = 0;
        foreach (var source in sources)
        {
            var count = source.Mesh.vertexCount;
            if (count > 60000) continue;
            if (chunk.Count == 256 || vertices + count > 60000)
            {
                if (chunk.Count >= 4) yield return chunk;
                chunk = new List<Source>(); vertices = 0;
            }
            chunk.Add(source); vertices += count;
        }
        if (chunk.Count >= 4) yield return chunk;
    }

    internal static void Shutdown()
    {
        foreach (var pair in Ships) pair.Value.Restore();
        Ships.Clear(); Removed.Clear(); Enabled = false;
    }
}
