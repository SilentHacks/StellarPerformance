using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Planets.Network;
using Planets.Terrain;
using Ships.Cables;
using Ships.Cables.VisualObjects;
using UnityEngine;

namespace StellarPerformance;

// Opt-in cosmetic caps (user authorization 2026-10-22). Balanced and Faithful keep
// every path native. Simulation, circuits, fluids, collisions and network state are
// never gated; only client-side presentation targets are refreshed less often.
internal static class VisualCadence
{
    static VisualCadence() { }
    internal const float MinimumMove = .25f, AltitudeFraction = .002f;
    internal static bool Available;
    internal static int CableHz, TerrainFrames;
    private static int Epoch;
    private static Func<SignalCablesVisualUpdater, CablesClientTracker> SignalCables = null!;
    private static readonly ConditionalWeakTable<SignalCablesVisualUpdater, Box> SignalDue = new ConditionalWeakTable<SignalCablesVisualUpdater, Box>();
    private static readonly ConditionalWeakTable<PlanetTreeTraversor, Traversal> Traversals = new ConditionalWeakTable<PlanetTreeTraversor, Traversal>();
    internal static readonly MethodInfo NativeBefore = AccessTools.Method(typeof(PlanetTreeTraversor), nameof(PlanetTreeTraversor.BeforeTraversal), new[] { typeof(Vector3) });
    internal static readonly MethodInfo NativeTraverse = AccessTools.Method(typeof(PlanetTreeTraversor), nameof(PlanetTreeTraversor.TraverseQuadTree), new[] { typeof(PlanetQuadTree) });
    internal sealed class Box { internal float Next = float.NegativeInfinity; }
    internal sealed class Traversal
    {
        internal Vector3 Position;
        internal int Frame = int.MinValue, Epoch = -1;
        internal bool Skip;
    }
    internal static MethodInfo TerrainTarget => AccessTools.Method(typeof(TrackedPlanetClient), nameof(TrackedPlanetClient.UpdateVisuals), new[] { typeof(Vector3) });
    internal static MethodInfo SignalTarget => AccessTools.Method(typeof(SignalCablesVisualUpdater), nameof(SignalCablesVisualUpdater.UpdateSignalCables));

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        if (NativeBefore == null || NativeTraverse == null) throw new MissingMethodException("PlanetTreeTraversor traversal pair");
        PatchSafety.RequireOriginalBody(TerrainTarget, harmony.Id);
        PatchSafety.RequireOriginalBody(SignalTarget, harmony.Id);
        SignalCables = NativeAccess.Read<SignalCablesVisualUpdater, CablesClientTracker>("_cablesTracker");
        harmony.Patch(TerrainTarget, transpiler: new HarmonyMethod(typeof(VisualCadence), nameof(Transpile)));
        harmony.Patch(SignalTarget, prefix: new HarmonyMethod(typeof(VisualCadence), nameof(BeforeSignalCables)));
        Available = true;
    }
    internal static void Change(PresetPolicy policy)
    {
        CableHz = policy.CableHz; TerrainFrames = policy.TerrainFrames;
        // Any presentation change (including vegetation range) forces a fresh traversal.
        unchecked { Epoch++; }
    }

    // A non-positive rate is native: every call is due.
    internal static bool Due(Box box, float now, int hz)
    {
        if (hz <= 0) return true;
        var interval = 1f / hz;
        var reset = now < box.Next - interval * 4;
        if (!reset && now < box.Next) return false;
        // Keep a steady rate across frame quantisation; resynchronise after stalls or clock resets.
        box.Next = reset || now - box.Next >= interval ? now + interval : box.Next + interval;
        return true;
    }
    internal static bool FluidDue(Box box) => Due(box, Time.unscaledTime, CableHz);

    // Group signal values feed only cable glow on the client; the server evaluates
    // circuits independently. Skipped frames still animate glow toward the last values.
    internal static bool BeforeSignalCables(SignalCablesVisualUpdater __instance, IReadOnlyDictionary<uint, CableVisual> loadedCables)
    {
        if (CableHz <= 0 || Due(SignalDue.GetValue(__instance, _ => new Box()), Time.unscaledTime, CableHz)) return true;
        var cables = SignalCables(__instance);
        foreach (var pair in loadedCables)
            if (pair.Value is SignalCableVisual visual && visual != null && cables.TryGetSignalCableValue(pair.Key, out var signal))
                visual.UpdateGlow((float)signal);
        return false;
    }

    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.Select(code => new CodeInstruction(code)).ToList();
        int before = 0, traverse = 0;
        for (var i = 0; i < codes.Count; i++)
        {
            if (codes[i].Calls(NativeBefore))
            {
                codes[i].opcode = OpCodes.Call; codes[i].operand = AccessTools.Method(typeof(VisualCadence), nameof(GuardedBefore));
                codes.Insert(i, new CodeInstruction(OpCodes.Ldarg_0)); i++; before++;
            }
            else if (codes[i].Calls(NativeTraverse))
            {
                codes[i].opcode = OpCodes.Call; codes[i].operand = AccessTools.Method(typeof(VisualCadence), nameof(GuardedTraverse)); traverse++;
            }
        }
        if (before != 1 || traverse != 1) throw new InvalidOperationException("Expected exactly one terrain traversal pair.");
        return codes;
    }
    // Visual LOD, vegetation, hotspot and ambient-sound searches are functions of the
    // planet-relative observer position. While the observer is nearly still the previous
    // search results remain in place; any meaningful move or the frame limit refreshes.
    internal static bool ShouldSkip(Traversal state, Vector3 position, int frame, float altitude, int maximumFrames, int epoch)
    {
        if (maximumFrames <= 1 || state.Epoch != epoch || state.Frame == int.MinValue || frame < state.Frame || frame - state.Frame >= maximumFrames) return false;
        if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z) || !Finite(altitude)) return false;
        var threshold = Math.Max(MinimumMove, altitude * AltitudeFraction);
        return (position - state.Position).sqrMagnitude < threshold * threshold;
    }
    internal static void GuardedBefore(PlanetTreeTraversor traversor, Vector3 position, TrackedPlanetClient owner)
    {
        var state = Traversals.GetValue(traversor, _ => new Traversal());
        var frame = Time.frameCount;
        var planet = owner?.Planet;
        var altitude = planet == null ? 0f : Math.Max(0f, position.magnitude - planet.Radius);
        state.Skip = ShouldSkip(state, position, frame, altitude, TerrainFrames, Epoch);
        if (state.Skip) return;
        state.Position = position; state.Frame = frame; state.Epoch = Epoch;
        traversor.BeforeTraversal(position);
    }
    internal static void GuardedTraverse(PlanetTreeTraversor traversor, PlanetQuadTree quadTree)
    {
        if (Traversals.TryGetValue(traversor, out var state) && state.Skip) return;
        traversor.TraverseQuadTree(quadTree);
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
