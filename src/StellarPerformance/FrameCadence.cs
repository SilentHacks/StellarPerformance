using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Managers.Server;
using Planets.Network;
using Planets.Terrain;
using Players;
using Players.Interface.Model;
using Players.Services;
using Ships.Cables;
using Ships.Cables.VisualObjects;
using UnityEngine;

namespace StellarPerformance;

// Terrain LOD selection is a pure function of the planet-relative observer position. While the
// observer has not moved meaningfully, the previous traversal's observer lists are reused.
internal static class PlanetTraversalCadence
{
    static PlanetTraversalCadence() { }

    internal sealed class State
    {
        internal Vector3 Position;
        internal int Frame = int.MinValue;
        internal bool Skip;
    }

    internal static float MinimumMove = 0.25f;
    internal static float AltitudeFraction = 0.002f;
    internal static int MaximumFrames = 15;
    internal static long Skipped, Traversed;
    private static readonly ConditionalWeakTable<PlanetTreeTraversor, State> States = new ConditionalWeakTable<PlanetTreeTraversor, State>();
    internal static readonly MethodInfo NativeBefore = AccessTools.Method(typeof(PlanetTreeTraversor), nameof(PlanetTreeTraversor.BeforeTraversal), new[] { typeof(Vector3) }) ?? throw new MissingMethodException("PlanetTreeTraversor.BeforeTraversal");
    internal static readonly MethodInfo NativeTraverse = AccessTools.Method(typeof(PlanetTreeTraversor), nameof(PlanetTreeTraversor.TraverseQuadTree), new[] { typeof(PlanetQuadTree) }) ?? throw new MissingMethodException("PlanetTreeTraversor.TraverseQuadTree");
    private static readonly MethodInfo GuardedBeforeMethod = AccessTools.Method(typeof(PlanetTraversalCadence), nameof(GuardedBefore)) ?? throw new MissingMethodException("PlanetTraversalCadence.GuardedBefore");
    private static readonly MethodInfo GuardedTraverseMethod = AccessTools.Method(typeof(PlanetTraversalCadence), nameof(GuardedTraverse)) ?? throw new MissingMethodException("PlanetTraversalCadence.GuardedTraverse");

    internal static MethodInfo Target => AccessTools.Method(typeof(TrackedPlanetClient), nameof(TrackedPlanetClient.UpdateVisuals), new[] { typeof(Vector3) }) ?? throw new MissingMethodException("TrackedPlanetClient.UpdateVisuals");

    internal sealed class MeshState
    {
        internal int Generation = -1;
        internal int Generated = -1;
        internal bool Empty;
    }

    private sealed class Counter { internal int Value; }
    private static readonly ConditionalWeakTable<PlanetMeshTraversalObserver, Counter> Generations = new ConditionalWeakTable<PlanetMeshTraversalObserver, Counter>();
    private static readonly ConditionalWeakTable<Planets.Terrain.Meshes.PlanetMeshUpdater, MeshState> Meshes = new ConditionalWeakTable<Planets.Terrain.Meshes.PlanetMeshUpdater, MeshState>();
    private static readonly Func<Planets.Terrain.Meshes.PlanetMeshUpdater, PlanetMeshTraversalObserver> ObserverOf = NativeAccess.Read<Planets.Terrain.Meshes.PlanetMeshUpdater, PlanetMeshTraversalObserver>("_observer");
    private static readonly Func<Planets.Terrain.Meshes.PlanetMeshUpdater, bool> FlushOf = NativeAccess.Read<Planets.Terrain.Meshes.PlanetMeshUpdater, bool>("_shouldFlushMeshes");
    private static readonly Func<Planets.Terrain.Meshes.PlanetMeshUpdater, SortedList<ulong, Planets.Terrain.Model.ChunkMeshGpuData>> GeneratedOf = NativeAccess.Read<Planets.Terrain.Meshes.PlanetMeshUpdater, SortedList<ulong, Planets.Terrain.Model.ChunkMeshGpuData>>("_generatedMeshObjects");
    [ThreadStatic] private static MeshState? _updating;
    internal static long MeshChecksSkipped;

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        PatchSafety.RequireOriginalBody(Target, harmony.Id);
        var update = AccessTools.Method(typeof(Planets.Terrain.Meshes.PlanetMeshUpdater), nameof(Planets.Terrain.Meshes.PlanetMeshUpdater.UpdateTerrainMesh)) ?? throw new MissingMethodException("PlanetMeshUpdater.UpdateTerrainMesh");
        var delta = AccessTools.Method(typeof(Planets.Terrain.Meshes.PlanetMeshUpdater), "FindMeshesDelta") ?? throw new MissingMethodException("PlanetMeshUpdater.FindMeshesDelta");
        var observe = AccessTools.Method(typeof(PlanetMeshTraversalObserver), nameof(PlanetMeshTraversalObserver.BeforeTraverse), new[] { typeof(Vector3) }) ?? throw new MissingMethodException("PlanetMeshTraversalObserver.BeforeTraverse");
        PatchSafety.RequireOriginalBody(update, harmony.Id);
        harmony.Patch(Target, transpiler: new HarmonyMethod(typeof(PlanetTraversalCadence), nameof(Transpile)));
        harmony.Patch(observe, prefix: new HarmonyMethod(typeof(PlanetTraversalCadence), nameof(BeforeObserve)));
        harmony.Patch(update, prefix: new HarmonyMethod(typeof(PlanetTraversalCadence), nameof(BeforeMeshUpdate)), finalizer: new HarmonyMethod(typeof(PlanetTraversalCadence), nameof(AfterMeshUpdate)));
        harmony.Patch(delta, postfix: new HarmonyMethod(typeof(PlanetTraversalCadence), nameof(AfterDelta)));
    }

    internal static void BeforeObserve(PlanetMeshTraversalObserver __instance) => Generations.GetValue(__instance, _ => new Counter()).Value++;

    // The mesh delta is a function of the traversal result; it is re-evaluated after every
    // traversal (forced at least every MaximumFrames) or while any delta remains outstanding.
    internal static bool CanReuseMeshes(MeshState state, int generation, int generated, bool flush) =>
        !flush && state.Empty && state.Generation == generation && state.Generated == generated;

    internal static bool BeforeMeshUpdate(Planets.Terrain.Meshes.PlanetMeshUpdater __instance)
    {
        _updating = null;
        var observer = ObserverOf(__instance);
        if (observer == null) return true;
        var state = Meshes.GetValue(__instance, _ => new MeshState());
        var generation = Generations.TryGetValue(observer, out var counter) ? counter.Value : -1;
        var generated = GeneratedOf(__instance)?.Count ?? -1;
        if (CanReuseMeshes(state, generation, generated, FlushOf(__instance)))
        {
            if (DiagnosticCounters.Active) MeshChecksSkipped++;
            return false;
        }
        state.Generation = generation;
        state.Empty = false;
        _updating = state;
        return true;
    }

    internal static void AfterDelta(List<ulong> toRemove, List<Planets.Terrain.Model.ChunkMesh> toAdd)
    {
        if (_updating != null) _updating.Empty = (toRemove?.Count ?? 0) == 0 && (toAdd?.Count ?? 0) == 0;
    }

    internal static Exception? AfterMeshUpdate(Planets.Terrain.Meshes.PlanetMeshUpdater __instance, Exception? __exception)
    {
        var state = _updating;
        _updating = null;
        if (state == null) return __exception;
        if (__exception != null) { state.Empty = false; return __exception; }
        state.Generated = GeneratedOf(__instance)?.Count ?? -1;
        return __exception;
    }

    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.Select(code => new CodeInstruction(code)).ToList();
        int before = 0, traverse = 0;
        for (var i = 0; i < codes.Count; i++)
        {
            if (codes[i].Calls(NativeBefore))
            {
                if (codes[i].labels.Count != 0) throw new InvalidOperationException("Unexpected branch target at terrain traversal.");
                codes[i].opcode = OpCodes.Call;
                codes[i].operand = GuardedBeforeMethod;
                codes.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                i++; before++;
            }
            else if (codes[i].Calls(NativeTraverse))
            {
                codes[i].opcode = OpCodes.Call;
                codes[i].operand = GuardedTraverseMethod;
                traverse++;
            }
        }
        if (before != 1 || traverse != 1) throw new InvalidOperationException("Expected exactly one terrain traversal pair.");
        return codes;
    }

    internal static bool ShouldSkip(State state, Vector3 position, int frame, float altitude)
    {
        if (state.Frame == int.MinValue || frame < state.Frame || frame - state.Frame >= MaximumFrames) return false;
        if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z) || !Finite(altitude)) return false;
        var threshold = Math.Max(MinimumMove, altitude * AltitudeFraction);
        var dx = position.x - state.Position.x; var dy = position.y - state.Position.y; var dz = position.z - state.Position.z;
        return dx * dx + dy * dy + dz * dz < threshold * threshold;
    }

    internal static void GuardedBefore(PlanetTreeTraversor traversor, Vector3 position, TrackedPlanetClient owner)
    {
        var state = States.GetValue(traversor, _ => new State());
        var frame = Time.frameCount;
        state.Skip = ShouldSkip(state, position, frame, Altitude(owner, position));
        if (state.Skip)
        {
            if (DiagnosticCounters.Active) Skipped++;
            return;
        }
        state.Position = position;
        state.Frame = frame;
        if (DiagnosticCounters.Active) Traversed++;
        traversor.BeforeTraversal(position);
    }

    internal static void GuardedTraverse(PlanetTreeTraversor traversor, PlanetQuadTree quadTree)
    {
        if (States.TryGetValue(traversor, out var state) && state.Skip) return;
        traversor.TraverseQuadTree(quadTree);
    }

    private static float Altitude(TrackedPlanetClient owner, Vector3 position)
    {
        var planet = owner?.Planet;
        return planet == null ? 0f : Math.Max(0f, position.magnitude - planet.Radius);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

// Cosmetic cable values (group signal glow targets, fluid flow balancing) are recomputed at a
// fixed rate; per-frame animation keeps using the latest values so motion stays smooth.
internal static class CableVisualCadence
{
    static CableVisualCadence() { }

    internal static float Interval = 0.05f;
    internal static long Computed, Reused;
    private static readonly ConditionalWeakTable<SignalCablesVisualUpdater, Box> Next = new ConditionalWeakTable<SignalCablesVisualUpdater, Box>();
    // Bound only by Install: fluid cable topology also reads this type's cadence state.
    private static Func<SignalCablesVisualUpdater, CablesClientTracker>? _signalCablesOf;

    internal sealed class Box { internal float Value = float.NegativeInfinity; }

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var target = AccessTools.Method(typeof(SignalCablesVisualUpdater), nameof(SignalCablesVisualUpdater.UpdateSignalCables)) ?? throw new MissingMethodException("SignalCablesVisualUpdater.UpdateSignalCables");
        PatchSafety.RequireOriginalBody(target, harmony.Id, "stellarperformance.stateful-part-queries", "stellarperformance.client-signal-cables");
        _signalCablesOf = NativeAccess.Read<SignalCablesVisualUpdater, CablesClientTracker>("_cablesTracker");
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(CableVisualCadence), nameof(BeforeSignalCables)));
    }

    internal static bool Due(Box next, float now)
    {
        if (Interval <= 0 || now >= next.Value || now < next.Value - Interval * 4) { next.Value = now + Interval; return true; }
        return false;
    }

    internal static bool BeforeSignalCables(SignalCablesVisualUpdater __instance, IReadOnlyDictionary<uint, CableVisual> loadedCables)
    {
        var read = _signalCablesOf;
        if (read == null || Due(Next.GetValue(__instance, _ => new Box()), Time.unscaledTime))
        {
            if (DiagnosticCounters.Active) Computed++;
            return true;
        }
        if (DiagnosticCounters.Active) Reused++;
        var cables = read(__instance);
        foreach (var pair in loadedCables)
            if (pair.Value is SignalCableVisual visual && visual != null && cables.TryGetSignalCableValue(pair.Key, out var signal))
                visual.UpdateGlow((float)signal);
        return false;
    }
}

// The picking camera renders object ids for a 2x2 asynchronous centre readback. Rendering it on
// every Nth frame bounds hover latency to a few frames while removing most of its camera cost.
internal static class PickingCameraCadence
{
    static PickingCameraCadence() { }

    internal static int Divisor = 3;
    private static Camera? _camera;
    private static bool _suppressed;
    private static float _nextSearch;

    internal static void Update()
    {
        if (Divisor <= 1) return;
        var camera = _camera;
        if (camera == null)
        {
            _suppressed = false;
            if (Time.unscaledTime < _nextSearch) return;
            _nextSearch = Time.unscaledTime + 1f;
            foreach (var candidate in Camera.allCameras)
                if (candidate != null && candidate.name == "IdCamera" && candidate.targetTexture != null) { _camera = camera = candidate; break; }
            if (camera == null) return;
        }
        if (!camera.enabled && !_suppressed) return;
        var render = Time.frameCount % Divisor == 0;
        if (render == camera.enabled) return;
        camera.enabled = render;
        _suppressed = !render;
    }

    internal static void Shutdown()
    {
        if (_camera != null && _suppressed) _camera.enabled = true;
        _suppressed = false; _camera = null;
    }
}

// Host factory simulation (signals, fluids, machines, power, rooms) advances on every Nth network
// tick with an N-times larger step. Physics and networking keep the native tick. Piloting and
// lever/reposition interaction run every tick so flight control latency stays native.
internal static class SimulationCadence
{
    static SimulationCadence() { }

    internal static int Divisor = 2;
    internal static long Steps, SkippedTicks;
    private static int _phase;
    private static Func<WorldPhysicsServerUpdater, PlayersServerTracker>? _playersOf;

    internal static MethodInfo Target => AccessTools.Method(typeof(WorldPhysicsServerUpdater), "UpdateShipParts", new[] { typeof(float) }) ?? throw new MissingMethodException("WorldPhysicsServerUpdater.UpdateShipParts");

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        PatchSafety.RequireOriginalBody(Target, harmony.Id);
        _playersOf = NativeAccess.Read<WorldPhysicsServerUpdater, PlayersServerTracker>("_playersServerTracker");
        harmony.Patch(Target, prefix: new HarmonyMethod(typeof(SimulationCadence), nameof(BeforeUpdateShipParts)) { priority = Priority.First });
    }

    internal static bool NeedsEveryTick(PlayerStateType type) =>
        type == PlayerStateType.Piloting || type == PlayerStateType.InteractingWithLever || type == PlayerStateType.WalkingRepositioningShip;

    internal static bool Decide(ref float deltaTime, bool everyTick)
    {
        if (Divisor <= 1 || everyTick || !(deltaTime > 0) || float.IsInfinity(deltaTime)) { _phase = 0; return true; }
        _phase++;
        if (_phase < Divisor) return false;
        deltaTime *= _phase;
        _phase = 0;
        return true;
    }

    internal static bool BeforeUpdateShipParts(WorldPhysicsServerUpdater __instance, ref float deltaTime)
    {
        var everyTick = false;
        try
        {
            var read = _playersOf;
            if (read == null) everyTick = true;
            var players = read?.Invoke(__instance)?.ActivePlayers;
            if (players != null)
                for (var i = 0; i < players.Count && !everyTick; i++)
                {
                    var player = players[i];
                    if (player != null && NeedsEveryTick(player.State.Type)) everyTick = true;
                }
        }
        catch (Exception) { everyTick = true; }
        var run = Decide(ref deltaTime, everyTick);
        if (DiagnosticCounters.Active) { if (run) Steps++; else SkippedTicks++; }
        return run;
    }
}

// MelonLoader's support component re-runs reflection (GetMethods, LINQ, CreateDelegate) every
// frame before calling Transform.SetAsLastSibling. MelonLoader's PatchShield blocks patching its
// assemblies, so mark the reflection path finished and perform the same sibling call directly.
internal static class LoaderOverhead
{
    static LoaderOverhead() { }

    private static Func<Component?>? _component;

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(candidate => candidate.GetType("MelonLoader.Support.ComponentSiblingFix", false) != null)
            ?? throw new TypeLoadException("MelonLoader.Support.ComponentSiblingFix is not loaded.");
        var fix = assembly.GetType("MelonLoader.Support.ComponentSiblingFix", true)!;
        var main = assembly.GetType("MelonLoader.Support.Main", true)!;
        var failure = AccessTools.Field(fix, "_failure") ?? throw new MissingFieldException(fix.FullName, "_failure");
        var method = AccessTools.Field(fix, "_method") ?? throw new MissingFieldException(fix.FullName, "_method");
        var component = AccessTools.Field(main, "component") ?? throw new MissingFieldException(main.FullName, "component");
        if (failure.FieldType != typeof(bool) || !typeof(Delegate).IsAssignableFrom(method.FieldType) || !typeof(Component).IsAssignableFrom(component.FieldType))
            throw new InvalidOperationException("MelonLoader support component layout changed.");
        if ((bool)failure.GetValue(null)! || method.GetValue(null) == null || component.GetValue(null) == null)
            throw new InvalidOperationException("MelonLoader sibling ordering is not active.");
        _component = System.Linq.Expressions.Expression.Lambda<Func<Component?>>(System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Field(null, component), typeof(Component))).Compile();
        failure.SetValue(null, true);
    }

    // Runs inside the support component's Update, where the native helper ran before melon updates.
    internal static void Update()
    {
        var component = _component?.Invoke();
        if (component == null) return;
        var transform = component.transform;
        transform.SetAsLastSibling();
        transform.SetAsLastSibling();
    }
}
