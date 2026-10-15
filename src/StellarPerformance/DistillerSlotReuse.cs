using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Items.Model;
using Ships.Interface.Model.Parts.State;
using Ships.Parts.EthanolDistiller;

namespace StellarPerformance;

internal static class DistillerSlotReuse
{
    static DistillerSlotReuse() { }

    private static readonly MethodInfo NativeSetSlots = AccessTools.PropertySetter(typeof(EthanolDistillerState), nameof(EthanolDistillerState.InventorySlots)) ?? throw new MissingMethodException("EthanolDistillerState.set_InventorySlots");
    private static readonly MethodInfo ReuseSlotsMethod = AccessTools.Method(typeof(DistillerSlotReuse), nameof(SetOrReuseSlots)) ?? throw new MissingMethodException("DistillerSlotReuse.SetOrReuseSlots");
    private static readonly Action<EthanolDistillerState, InventorySlot[]?> SetSlots = (Action<EthanolDistillerState, InventorySlot[]?>)Delegate.CreateDelegate(typeof(Action<EthanolDistillerState, InventorySlot[]?>), NativeSetSlots);

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        PatchSafety.RequireNoForeignOwners(NativeSetSlots, harmony.Id);
        var update = AccessTools.Method(typeof(EthanolDistillersUpdater), nameof(EthanolDistillersUpdater.Update), new[] { typeof(float) }) ?? throw new MissingMethodException("EthanolDistillersUpdater.Update");
        harmony.Patch(update, transpiler: new HarmonyMethod(typeof(DistillerSlotReuse), nameof(Transpile)));
    }

    internal static void SetOrReuseSlots(EthanolDistillerState state, InventorySlot[]? candidate)
    {
        var previous = state.InventorySlots;
        if (previous != null && candidate != null && SlotsEqual(previous, candidate)) return;
        SetSlots(state, candidate);
    }

    internal static bool SlotsEqual(InventorySlot[] a, InventorySlot[] b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
            if (!a[i].Equals(b[i])) return false;
        return true;
    }

    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.Select(code => new CodeInstruction(code)).ToList();
        var matches = codes.Where(code => code.Calls(NativeSetSlots)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("EthanolDistillersUpdater.Update InventorySlots setter pattern changed.");
        matches[0].opcode = OpCodes.Call;
        matches[0].operand = ReuseSlotsMethod;
        return codes;
    }
}
