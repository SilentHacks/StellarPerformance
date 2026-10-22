using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using HarmonyLib;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.State;
using Ships.Parts.Common;
using Ships.Parts.Common.Model.Reactions;
using Ships.Parts.Constant;
using Ships.Parts.Electrolyzer;
using Ships.Parts.EthanolDistiller;
using Ships.Parts.FluidValve;
using Ships.Parts.FuelTank;
using Ships.Parts.GasExhaust;
using Ships.Parts.GasMeter;
using Ships.Parts.MathGate;
using Ships.Parts.SignalDisplay;
using Ships.Parts.SignalSplitter;
using Ships.Parts.SignalTransmitter;
using Ships.Parts.Thruster;
using Ships.Parts.UnaryMathGate;
using Ships.Parts.WoodThruster;
using Ships.Visuals;
using UnityEngine;
using UnityEngine.Rendering;

namespace StellarPerformance;

// Only fixed opaque geometry using known object-space-independent
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
        internal readonly ushort PartId;
        internal int IndexStart, IndexCount;
        internal readonly Vector3 PartPosition, PartScale, Position, Scale;
        internal readonly Quaternion PartRotation, Rotation;
        internal readonly int Layer;
        internal readonly ShadowCastingMode Shadows;
        internal readonly bool ReceiveShadows;
        internal readonly uint RenderingLayers;
        internal readonly int Channels;
        private readonly Pose[] _intermediates;
        internal Source(MeshRenderer renderer, MeshFilter filter, Transform part, ushort partId)
        {
            PartId = partId;
            Renderer = renderer; Filter = filter; Mesh = filter.sharedMesh; Material = renderer.sharedMaterial; Part = part;
            PartPosition = part.localPosition; PartRotation = part.localRotation; PartScale = part.localScale;
            Position = renderer.transform.localPosition; Rotation = renderer.transform.localRotation; Scale = renderer.transform.localScale;
            Layer = renderer.gameObject.layer; Shadows = renderer.shadowCastingMode; ReceiveShadows = renderer.receiveShadows; RenderingLayers = renderer.renderingLayerMask;
            for (var i = 0; i < 14; i++) if (Mesh.HasVertexAttribute((VertexAttribute)i)) Channels |= 1 << i;
            var intermediates = new List<Pose>();
            for (var parent = renderer.transform == part ? null : renderer.transform.parent; parent != null && parent != part; parent = parent.parent) intermediates.Add(new Pose(parent));
            _intermediates = intermediates.ToArray();
        }
        internal bool Valid(bool native = false) => Renderer != null && Filter != null && Part != null && Mesh != null && Material != null && Renderer.gameObject.activeInHierarchy &&
            Renderer.enabled == native && !Renderer.forceRenderingOff && Renderer.gameObject.layer == Layer && Renderer.shadowCastingMode == Shadows && Renderer.receiveShadows == ReceiveShadows && Renderer.renderingLayerMask == RenderingLayers &&
            Filter.sharedMesh == Mesh && Renderer.sharedMaterial == Material && SingleMaterial(Renderer) && !Renderer.HasPropertyBlock() && Renderer.additionalVertexStreams == null &&
            Equal(Part.localPosition, PartPosition) && Equal(Part.localScale, PartScale) && Equal(Part.localRotation, PartRotation) &&
            Equal(Renderer.transform.localPosition, Position) && Equal(Renderer.transform.localScale, Scale) && Equal(Renderer.transform.localRotation, Rotation) && ValidIntermediates();
        private bool ValidIntermediates() { for (var i = 0; i < _intermediates.Length; i++) if (!_intermediates[i].Valid()) return false; return true; }
    }

    private sealed class Batch
    {
        internal readonly List<Source> Sources;
        internal readonly Mesh Mesh;
        internal readonly GameObject Object;
        internal readonly int[] Indices;
        internal bool Suspended;
        private bool _released;
        internal int ReadyFrame;
        internal Batch(List<Source> sources, Mesh mesh, GameObject obj, int[] indices) { Sources = sources; Mesh = mesh; Object = obj; Indices = indices; CachedIndexBytes += indices.Length * sizeof(int); }
        internal void Suspend()
        {
            if (Suspended) return;
            if (Object != null) Object.SetActive(false);
            for (var i = 0; i < Sources.Count; i++) if (Sources[i].Renderer != null) { Sources[i].Renderer.enabled = true; }
            Suspended = true; ReadyFrame = Time.frameCount + 3;
        }
        internal void Restore(Ship ship)
        {
            if (_released) return;
            _released = true;
            Suspend();
            if (Object != null) UnityEngine.Object.Destroy(Object);
            if (Mesh != null) UnityEngine.Object.Destroy(Mesh);
            CachedIndexBytes -= Indices.Length * sizeof(int);
            foreach (var source in Sources) ship.Owned.Remove(source.Renderer);
        }
        internal bool Held(Ship ship) { foreach (var source in Sources) if (ship.Holds.Held(source.PartId)) return true; return false; }
        internal bool Recover(Ship ship)
        {
            var write = 0; var kept = 0; var changed = false;
            for (var i = 0; i < Sources.Count; i++)
            {
                var source = Sources[i];
                if (!source.Valid(true)) { ship.Owned.Remove(source.Renderer); changed = true; continue; }
                Array.Copy(Indices, source.IndexStart, Indices, write, source.IndexCount);
                source.IndexStart = write; write += source.IndexCount; Sources[kept++] = source;
            }
            if (kept != Sources.Count) Sources.RemoveRange(kept, Sources.Count - kept);
            if (kept < 4 || Object == null || Mesh == null) { Restore(ship); return false; }
            // Keep the original vertex data and conservative bounds. Only exact
            // triangle ranges belonging to changed native sources are removed.
            if (changed) Mesh.SetIndices(Indices, 0, write, MeshTopology.Triangles, 0, false);
            foreach (var source in Sources) source.Renderer.enabled = false;
            Object.SetActive(true); Suspended = false;
            return true;
        }
    }

    private sealed class Ship
    {
        internal readonly ShipPartVisuals Visuals;
        internal readonly List<Batch> Batches = new List<Batch>();
        internal readonly HashSet<MeshRenderer> Owned = new HashSet<MeshRenderer>();
        internal readonly BatchInteractionHolds Holds = new BatchInteractionHolds();
        internal IEnumerator<int>? Builder;
        internal bool Dirty = true;
        internal bool Disabled;
        internal int ReadyFrame;
        internal Ship(ShipPartVisuals visuals) { Visuals = visuals; ReadyFrame = Time.frameCount + 3; }
        internal void CancelBuild() { Builder?.Dispose(); Builder = null; }
        internal void Restore() { CancelBuild(); for (var i = 0; i < Batches.Count; i++) Batches[i].Restore(this); Batches.Clear(); Owned.Clear(); }
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
    private static readonly List<Ship> Work = new List<Ship>();
    private static readonly List<Material> Materials = new List<Material>(4);
    private static readonly List<int> IndexScratch = new List<int>();
    private static int _nextShip;
    internal static int CachedIndexBytes;
    internal const int MaxCachedIndexBytes = 16 * 1024 * 1024;
    internal const int MaxBatchVertices = 16000, MaxBatchSources = 64, MaxBatchIndices = 96000;
    private static bool SingleMaterial(Renderer renderer) { Materials.Clear(); renderer.GetSharedMaterials(Materials); return Materials.Count == 1; }
    internal static bool Ready(int now, int deadline) => unchecked(now - deadline) >= 0;
    internal static bool Enabled;
    internal static bool ContinuousValidation = false;
    internal static int SourceRenderers, ReplacementRenderers, InspectedRenderers, UnsupportedRenderers;
    internal static Action<string>? Log;
    internal const string InspectedAssetHash = "86A3108EFBC4C48DE335949E38B0CA59069376975C58085E7F261A558908289F";
    private static bool _customShader;
    private static AccessTools.FieldRef<ShipPartVisuals, Dictionary<ushort, IShipPartVisual>> _parts = null!;
    internal static readonly Type[] FixedTypes = { typeof(DefaultShipPartVisuals), typeof(ThrusterVisual), typeof(FluidTankVisuals), typeof(SignalTransmitterVisuals), typeof(SignalProcessorVisuals), typeof(MathGateVisuals), typeof(SignalSplitterVisuals), typeof(SignalDisplayVisuals), typeof(FluidValveVisual), typeof(EthanolDistillerVisuals), typeof(ElectrolyzerVisuals), typeof(ConstantVisuals), typeof(GasMeterVisuals), typeof(GasExhaustVisuals), typeof(WoodThrusterVisuals) };
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
        _parts = AccessTools.FieldRefAccess<ShipPartVisuals, Dictionary<ushort, IShipPartVisual>>("_floatingParts");
        if (!ContinuousValidation)
        {
            var allowed = Array.Empty<string>();
            foreach (var type in FixedTypes)
                foreach (var name in new[] { "SetNetworkState", "ReactToNetworkStateChange", "Update", "LateUpdate" })
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
        harmony.Patch(AccessTools.Method(typeof(ShipPartVisuals), "HighlightFloatingPart"), prefix: new HarmonyMethod(typeof(ShipPartBatching), nameof(BeforeHighlight)));
        harmony.Patch(AccessTools.Method(typeof(ShipPartVisuals), "RemoveFloatingPartHighlight"), prefix: new HarmonyMethod(typeof(ShipPartBatching), nameof(BeforeUnhighlight)), postfix: new HarmonyMethod(typeof(ShipPartBatching), nameof(AfterUnhighlight)));
        harmony.Patch(AccessTools.Method(typeof(Ships.Parts.Common.Services.ShipInteractionHoverToggler), "HoverEnter"), prefix: new HarmonyMethod(typeof(ShipPartBatching), nameof(BeforeHover)));
        harmony.Patch(AccessTools.Method(typeof(Ships.Parts.Common.Services.ShipInteractionHoverToggler), "HoverLeave"), prefix: new HarmonyMethod(typeof(ShipPartBatching), nameof(BeforeHoverLeave)), postfix: new HarmonyMethod(typeof(ShipPartBatching), nameof(AfterHoverLeave)));
        harmony.Patch(AccessTools.Method(typeof(ShipPartVisuals), nameof(ShipPartVisuals.OnStatefulPartStateChange)), prefix: new HarmonyMethod(typeof(ShipPartBatching), nameof(BeforePartStateChange)));
        Enabled = true;
    }

    internal static void AfterAdd(ShipPartVisuals __instance)
    {
        if (!Ships.TryGetValue(__instance, out var ship)) { Ships.Add(__instance, ship = new Ship(__instance)); Work.Add(ship); }
        ship.CancelBuild();
        ship.Dirty = true; ship.ReadyFrame = Time.frameCount + 3;
    }
    internal static void BeforeRemove(ShipPartVisuals __instance, StatefulPart part)
    {
        RestorePart(__instance, part.Id);
        if (Ships.TryGetValue(__instance, out var ship)) { ship.CancelBuild(); ship.Holds.Remove(part.Id); ship.Dirty = true; ship.ReadyFrame = Time.frameCount + 3; }
    }
    internal static void BeforeHighlight(ShipPartVisuals __instance, ushort id) => Interaction(__instance, id, null, true);
    internal static void BeforeUnhighlight(ShipPartVisuals __instance, ushort id) => RestorePart(__instance, id);
    internal static void AfterUnhighlight(ShipPartVisuals __instance, ushort id) => Interaction(__instance, id, null, false);
    internal static void BeforeHover(PartContext context, string partName) => Hover(context, partName, true);
    internal static void BeforeHoverLeave(PartContext context)
    {
        foreach (var pair in Ships) if (pair.Key != null && pair.Key.ShipId == context.ShipId) RestorePart(pair.Key, context.PartId);
    }
    internal static void AfterHoverLeave(PartContext context, string partName) => Hover(context, partName, false);
    private static void Hover(PartContext context, string partName, bool entering)
    {
        foreach (var pair in Ships) if (pair.Key != null && pair.Key.ShipId == context.ShipId) Interaction(pair.Key, context.PartId, partName ?? string.Empty, entering);
    }
    private static void Interaction(ShipPartVisuals visuals, ushort id, string? name, bool entering)
    {
        if (!Ships.TryGetValue(visuals, out var ship) || ship.Disabled) return;
        try
        {
            if (entering)
            {
                if (!(name == null ? ship.Holds.Highlight(id) : ship.Holds.Enter(id, name))) { Disable(ship, "Interaction tracking bound reached."); return; }
                RestorePart(visuals, id);
            }
            else
            {
                if (name == null) ship.Holds.Unhighlight(id); else ship.Holds.Leave(id, name);
                // Debounce movement between adjacent native interaction objects.
                foreach (var batch in ship.Batches)
                    if (batch.Suspended)
                        foreach (var source in batch.Sources) if (source.PartId == id) { batch.ReadyFrame = Time.frameCount + 6; break; }
            }
        }
        catch (Exception error) { Disable(ship, error.Message); }
    }

    private static void RestorePart(ShipPartVisuals visuals, ushort id)
    {
        if (!Ships.TryGetValue(visuals, out var ship) || ship.Disabled) return;
        try
        {
            for (var i = 0; i < ship.Batches.Count; i++)
            {
                var batch = ship.Batches[i];
                for (var j = 0; j < batch.Sources.Count; j++) if (batch.Sources[j].PartId == id) { batch.Suspend(); break; }
            }
        }
        catch (Exception error) { Disable(ship, error.Message); }
    }

    internal static void BeforePartStateChange(ShipPartVisuals __instance, StatefulPartStateChangeEvent change)
    {
        // Of the supported fixed visual classes, only a valve moves an opaque child
        // transform. Other changes affect excluded screens, particles or liquid shaders.
        if (NeedsPoseInvalidation(change)) RestorePart(__instance, change.PartId);
    }

    internal static bool NeedsPoseInvalidation(StatefulPartStateChangeEvent change) =>
        change.NewState is FluidValveState next && (!(change.PreviousState is FluidValveState previous) || !CoreBits.Same(previous.ValveOpenedRate, next.ValveOpenedRate));

    internal static void Update()
    {
        if (!Enabled) return;
        Removed.Clear();
        foreach (var pair in Ships)
        {
            var ship = pair.Value;
            if (ship.Visuals == null) { ship.Restore(); ship.Holds.Clear(); Work.Remove(ship); Removed.Add(pair.Key); continue; }
            var invalid = false;
            if (ContinuousValidation)
                for (var i = 0; i < ship.Batches.Count && !invalid; i++)
                    if (!ship.Batches[i].Suspended) for (var j = 0; j < ship.Batches[i].Sources.Count; j++) if (!ship.Batches[i].Sources[j].Valid()) { invalid = true; break; }
            if (invalid) Disable(ship, "Renderer/material/transform changed outside inspected native events.");
        }
        for (var i = 0; i < Removed.Count; i++) Ships.Remove(Removed[i]);
        // A cooperative CPU budget, not a hard real-time promise. Each native mesh
        // operation also has independent source/vertex/index bounds.
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 800;
        var idle = 0;
        for (var steps = 0; Work.Count != 0 && steps < 32 && Stopwatch.GetTimestamp() < deadline; steps++)
        {
            if (_nextShip >= Work.Count) _nextShip = 0;
            var ship = Work[_nextShip++];
            try
            {
                var work = Step(ship);
                if (work == 2) break; // At most one mesh commit/recovery per frame.
                if (work == 0) { if (++idle >= Work.Count) break; } else idle = 0;
            }
            catch (Exception error) { Disable(ship, error.GetType().Name + ": " + error.Message); }
        }
        SourceRenderers = ReplacementRenderers = 0;
        foreach (var pair in Ships) foreach (var batch in pair.Value.Batches) if (!batch.Suspended) { SourceRenderers += batch.Sources.Count; ReplacementRenderers++; }
    }

    private static int Step(Ship ship)
    {
        if (ship.Disabled) return 0;
        for (var i = 0; i < ship.Batches.Count; i++)
        {
            var batch = ship.Batches[i];
            if (!batch.Suspended || !Ready(Time.frameCount, batch.ReadyFrame) || batch.Held(ship)) continue;
            if (!batch.Recover(ship)) ship.Batches.RemoveAt(i);
            return 2;
        }
        if (ship.Dirty && Ready(Time.frameCount, ship.ReadyFrame)) { ship.Dirty = false; ship.Builder = Build(ship).GetEnumerator(); }
        if (ship.Builder == null) return 0;
        if (ship.Builder.MoveNext()) return ship.Builder.Current;
        ship.CancelBuild(); return 1;
    }

    private static void Disable(Ship ship, string reason)
    {
        ship.Disabled = true; ship.Dirty = false; ship.Restore(); ship.Holds.Clear();
        Log?.Invoke("Ship part batching retained native rendering for this ship: " + reason);
    }

    internal static bool FixedBody(DefaultShipPartVisuals part)
    {
        var type = part.GetType();
        if (Array.IndexOf(FixedTypes, type) < 0) return false;
        // Generic visual parts have no native behavior of their own. Require every
        // attached script to be a known interaction/identity component before batching.
        if (type == typeof(DefaultShipPartVisuals))
            foreach (var component in part.GetComponentsInChildren<MonoBehaviour>(true))
                if (component == null || !StaticGenericComponent(component.GetType())) return false;
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

    internal static bool StaticGenericComponent(Type type) => type == typeof(DefaultShipPartVisuals) ||
        type == typeof(DefaultShipPartInteractions) || type == typeof(Ships.Parts.Common.Model.PartSocketsTracker) ||
        type == typeof(Ships.Parts.Common.Model.ShipPartIdentifier) || type == typeof(Ships.Parts.Common.Utils.PartTipInteraction) ||
        type == typeof(Ships.Cables.VisualObjects.SocketInteractionObject) ||
        (typeof(PartInteraction).IsAssignableFrom(type) && type.Assembly == typeof(PartInteraction).Assembly);

    internal static bool FixedHierarchy(Transform transform, Transform part)
    {
        for (var depth = 0; depth < 8 && transform != null; depth++, transform = transform.parent)
        {
            if (transform == part) return true;
            // Gauges, screen billboards and scripted moving subassemblies remain native.
            if (transform.GetComponents<MonoBehaviour>().Length != 0) return false;
        }
        return false;
    }

    private static IEnumerable<int> Build(Ship ship)
    {
        var byKey = new Dictionary<Key, List<Source>>();
        var inspected = 0; var rejected = 0;
        foreach (var entry in _parts(ship.Visuals))
        {
            yield return 1;
            var part = entry.Value as DefaultShipPartVisuals;
            if (part == null || !FixedBody(part)) continue;
            if (ship.Holds.Held(entry.Key)) continue;
            foreach (var renderer in part.GetComponentsInChildren<MeshRenderer>())
            {
                if (ship.Owned.Contains(renderer)) continue;
                inspected++;
                var filter = renderer.GetComponent<MeshFilter>();
                var material = renderer.sharedMaterial;
                var transform = renderer.transform;
                // A fixed body with matching vertex channels/material, no scripted
                // intermediate transform, and only inspected rendering dependencies.
                if (!renderer.enabled || renderer.forceRenderingOff || filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable || filter.sharedMesh.subMeshCount != 1 || filter.sharedMesh.GetTopology(0) != MeshTopology.Triangles ||
                    filter.sharedMesh.vertexCount > MaxBatchVertices || filter.sharedMesh.GetIndexCount(0) > MaxBatchIndices ||
                    !SingleMaterial(renderer) || material == null || material.shader == null || !SupportedShader(material.shader.name, _customShader) || material.renderQueue > 2500 || material.renderQueue < 0 ||
                    renderer.HasPropertyBlock() || renderer.additionalVertexStreams != null || filter.sharedMesh.blendShapeCount != 0 ||
                    ((material.shader.name != "Custom/PlanetObject") && (renderer.lightProbeUsage != LightProbeUsage.Off || renderer.reflectionProbeUsage != ReflectionProbeUsage.Off)) || renderer.lightmapIndex >= 0 ||
                    !UniformPositive(ship.Visuals.transform.worldToLocalMatrix * renderer.localToWorldMatrix) ||
                    !FixedHierarchy(transform, part.transform) || renderer.GetComponentInParent<LODGroup>() != null || renderer.GetComponentInParent<Animator>() != null ||
                    renderer.GetComponentInParent<Animation>() != null || renderer.GetComponent<TMPro.TMP_Text>() != null)
                { rejected++; continue; }
                var source = new Source(renderer, filter, part.transform, entry.Key);
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
                yield return 1;
                for (var i = sources.Count - 1; i >= 0; i--)
                    if (!sources[i].Valid(true) || ship.Owned.Contains(sources[i].Renderer) || ship.Holds.Held(sources[i].PartId)) sources.RemoveAt(i);
                if (sources.Count < 4) continue;
                var indexCount = 0;
                foreach (var source in sources) indexCount += (int)source.Mesh.GetIndexCount(0);
                if (CachedIndexBytes + indexCount * sizeof(int) > MaxCachedIndexBytes) continue;
                var instances = new CombineInstance[sources.Count];
                for (var i = 0; i < sources.Count; i++) instances[i] = new CombineInstance { mesh = sources[i].Mesh, subMeshIndex = 0, transform = ship.Visuals.transform.worldToLocalMatrix * sources[i].Renderer.localToWorldMatrix };
                var mesh = new Mesh { name = "StellarPerformance.FixedPartBatch" };
                GameObject? obj = null;
                try
                {
                    mesh.CombineMeshes(instances, true, true, false);
                    mesh.RecalculateBounds();
                    var indices = mesh.GetIndices(0);
                    // Verify the native combiner's layout before caching removable
                    // ranges. Unsupported layouts keep the original renderers.
                    if (indices.Length != indexCount) throw new InvalidOperationException("Unexpected combined index count.");
                    var offset = 0; var vertices = 0;
                    foreach (var source in sources)
                    {
                        IndexScratch.Clear(); source.Mesh.GetIndices(IndexScratch, 0);
                        source.IndexStart = offset; source.IndexCount = IndexScratch.Count;
                        for (var i = 0; i < IndexScratch.Count; i++) if (indices[offset + i] != IndexScratch[i] + vertices) throw new InvalidOperationException("Unexpected native mesh index layout.");
                        offset += IndexScratch.Count; vertices += source.Mesh.vertexCount;
                    }
                    obj = new GameObject("StellarPerformance.FixedPartBatch");
                    obj.transform.SetParent(ship.Visuals.transform, false); obj.layer = pair.Key.Layer;
                    obj.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var output = obj.AddComponent<MeshRenderer>();
                    output.sharedMaterial = pair.Key.Material; output.shadowCastingMode = pair.Key.Shadows;
                    output.receiveShadows = pair.Key.ReceiveShadows; output.renderingLayerMask = pair.Key.RenderingLayers;
                    output.lightProbeUsage = LightProbeUsage.Off; output.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    ship.Batches.Add(new Batch(sources, mesh, obj, indices));
                    foreach (var source in sources) { ship.Owned.Add(source.Renderer); source.Renderer.enabled = false; }
                    batches++; combined += sources.Count;
                }
                catch { if (obj != null) { obj.SetActive(false); UnityEngine.Object.Destroy(obj); } UnityEngine.Object.Destroy(mesh); throw; }
                yield return 2;
            }
        if (batches != 0) Log?.Invoke("Ship part batching: inspected=" + inspected + ", unsupported=" + rejected + ", source renderers=" + combined + ", replacement renderers=" + batches + ".");
    }

    private static IEnumerable<List<Source>> Partition(List<Source> sources)
    {
        var chunk = new List<Source>(); var vertices = 0; var indices = 0;
        foreach (var source in sources)
        {
            var count = source.Mesh.vertexCount;
            var rawIndices = source.Mesh.GetIndexCount(0);
            if (count > MaxBatchVertices || rawIndices > MaxBatchIndices) continue;
            var indexCount = (int)rawIndices;
            if (chunk.Count == MaxBatchSources || vertices + count > MaxBatchVertices || indices + indexCount > MaxBatchIndices)
            {
                if (chunk.Count >= 4) yield return chunk;
                chunk = new List<Source>(); vertices = indices = 0;
            }
            chunk.Add(source); vertices += count; indices += indexCount;
        }
        if (chunk.Count >= 4) yield return chunk;
    }

    internal static void Shutdown()
    {
        foreach (var pair in Ships) pair.Value.Restore();
        Ships.Clear(); Removed.Clear(); Work.Clear(); Materials.Clear(); IndexScratch.Clear(); _nextShip = 0; Enabled = false;
    }
}
