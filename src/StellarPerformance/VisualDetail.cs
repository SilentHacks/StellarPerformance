using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Planets.EnvironmentGeneration.Tracking;

namespace StellarPerformance;

// Only client visual observers change. Collision observers, sound searches,
// resource placement and terrain evaluation retain native behaviour.
internal static class VisualDetail
{
    static VisualDetail() { }
    internal static bool Available;
    private static PresetPolicy Policy = PresetPolicy.Named("Balanced");
    internal static void Change(PresetPolicy policy) => Policy = policy;
    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var vegetation = AccessTools.Method(typeof(PlanetDenseEnvTreeTraversalObserver), nameof(PlanetDenseEnvTreeTraversalObserver.ObserveTraversal));
        PatchSafety.RequireOriginalBody(vegetation, harmony.Id);
        harmony.Patch(vegetation, transpiler: new HarmonyMethod(typeof(VisualDetail), nameof(Vegetation)));
        Available = true;
    }
    private static float ScaleVegetation(float radius, PlanetDenseEnvTreeTraversalObserver observer) =>
        Policy.VegetationRange == 1 || observer.GetType() != typeof(PlanetDenseEnvVisualsTreeTraversalObserver) ? radius : Math.Min(radius, Math.Max(20, radius * Policy.VegetationRange));
    private static IEnumerable<CodeInstruction> Vegetation(IEnumerable<CodeInstruction> codes)
    {
        var target = AccessTools.Field(typeof(PlanetDenseEnvTreeTraversalObserver.SearchLevel), nameof(PlanetDenseEnvTreeTraversalObserver.SearchLevel.SearchRadius)); var found = 0;
        foreach (var code in codes)
        {
            yield return code;
            if (code.opcode == OpCodes.Ldfld && Equals(code.operand, target))
            { found++; yield return new CodeInstruction(OpCodes.Ldarg_0); yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(VisualDetail), nameof(ScaleVegetation))); }
        }
        if (found != 1) throw new InvalidOperationException("Visual vegetation range contract changed.");
    }
}
