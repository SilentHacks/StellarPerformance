using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using HarmonyLib;
using Ships.Cables;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Parts.FluidValve;
using Ships.Parts.Thruster;

namespace StellarPerformance;

// Reuse immutable native records at their construction sites. All native calculations,
// UpdatePartState calls and event delivery remain in place. Never mutate a published record.
internal static class NativeStateReuse
{
    static NativeStateReuse() { }

    [StructLayout(LayoutKind.Explicit)]
    private struct FloatBits
    {
        [FieldOffset(0)] internal float Value;
        [FieldOffset(0)] internal int Bits;
    }

    internal static bool Same(float a, float b) => new FloatBits { Value = a }.Bits == new FloatBits { Value = b }.Bits;
    internal static bool Same(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
    internal static long Reused, Created;

    internal sealed class Recipe
    {
        internal readonly Type State;
        internal readonly string[] Properties;
        internal readonly MethodInfo Factory;
        internal readonly int Count;
        internal Recipe(Type state, int count, string factory, params string[] properties)
        {
            State = state; Count = count; Properties = properties;
            Factory = AccessTools.Method(typeof(NativeStateReuse), factory);
        }
    }

    internal static readonly MethodInfo Thrusters = AccessTools.Method(typeof(ShipThrustersUpdater), nameof(ShipThrustersUpdater.Update));
    internal static readonly MethodInfo Valves = AccessTools.Method(typeof(FluidValvesUpdater), nameof(FluidValvesUpdater.Update));
    internal static readonly MethodInfo Signals = AccessTools.Method(typeof(SignalConnectionsServerUpdater), "ExecuteOperations");
    internal static readonly MethodInfo TankWithFluid = AccessTools.Method(typeof(FluidTankState), nameof(FluidTankState.WithFluid));

    internal static Recipe[] Recipes(MethodBase method)
    {
        if (method == Thrusters) return new[] { new Recipe(typeof(ThrusterState), 1, nameof(Thruster), "ThrusterStrength") };
        if (method == Valves) return new[] { new Recipe(typeof(FluidValveState), 4, nameof(Valve), "CurrentTransferRate", "ValveOpenedRate") };
        if (method == Signals) return new[]
        {
            new Recipe(typeof(MathGateState), 1, nameof(MathGate), "Output"),
            new Recipe(typeof(DeltaState), 1, nameof(Delta), "PreviousValue", "DeltaValue"),
            new Recipe(typeof(SignalTransmitterState), 1, nameof(Transmitter), "SignalValue"),
            new Recipe(typeof(SignalProcessorState), 1, nameof(Processor), "PreviousValue", "Output")
        };
        throw new InvalidOperationException("Unknown native state construction path.");
    }

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        foreach (var target in new[] { Thrusters, Valves, Signals })
            foreach (var recipe in Recipes(target))
            {
                if (!PatchSafety.HasOnlyOwners(AccessTools.Method(recipe.State, "<Clone>$"), Array.Empty<string>()))
                    throw new InvalidOperationException(recipe.State.Name + " cloning has another owner.");
                foreach (var property in recipe.Properties)
                    if (!PatchSafety.HasOnlyOwners(AccessTools.PropertySetter(recipe.State, property), Array.Empty<string>()) || !PatchSafety.HasOnlyOwners(AccessTools.PropertyGetter(recipe.State, property), Array.Empty<string>()))
                        throw new InvalidOperationException(recipe.State.Name + "." + property + " has another owner.");
            }
        foreach (var method in new[] { Thrusters, Valves, Signals })
            harmony.Patch(method ?? throw new MissingMethodException("Native state updater missing."), transpiler: new HarmonyMethod(typeof(NativeStateReuse), nameof(Transpile)));
        // This prefix returns the existing instance only for the exact inspected native type.
        // Other Harmony owners can supply observable WithFluid behavior, so do not bypass them.
        if (!PatchSafety.HasOnlyOwners(TankWithFluid, Array.Empty<string>()))
            throw new InvalidOperationException("FluidTankState.WithFluid has another owner.");
        harmony.Patch(TankWithFluid, prefix: new HarmonyMethod(typeof(NativeStateReuse), nameof(BeforeTankWithFluid)));
    }

    internal static bool BeforeTankWithFluid(FluidTankState __instance, uint fluidItemId, float amount, float flow, ref IStatefulPartWithFluidContainer __result)
    {
        if (__instance.GetType() != typeof(FluidTankState)) return true;
        if (amount <= 0f) fluidItemId = 0;
        if (__instance.FluidItemId != fluidItemId || !Same(__instance.FluidQuantity, amount) || !Same(__instance.FluidFlow, flow)) { if (DiagnosticCounters.Active) Created++; return true; }
        if (DiagnosticCounters.Active) Reused++; __result = __instance; return false;
    }

    internal static ThrusterState Thruster(ThrusterState previous, float strength)
    {
        if (previous.GetType() == typeof(ThrusterState) && Same(previous.ThrusterStrength, strength)) { if (DiagnosticCounters.Active) Reused++; return previous; }
        if (DiagnosticCounters.Active) Created++; return previous with { ThrusterStrength = strength };
    }

    internal static FluidValveState Valve(FluidValveState previous, float transfer, float opened)
    {
        if (previous.GetType() == typeof(FluidValveState) && Same(previous.CurrentTransferRate, transfer) && Same(previous.ValveOpenedRate, opened)) { if (DiagnosticCounters.Active) Reused++; return previous; }
        if (DiagnosticCounters.Active) Created++; return previous with { CurrentTransferRate = transfer, ValveOpenedRate = opened };
    }

    internal static MathGateState MathGate(MathGateState previous, double output)
    {
        if (previous.GetType() == typeof(MathGateState) && Same(previous.Output, output)) { if (DiagnosticCounters.Active) Reused++; return previous; }
        if (DiagnosticCounters.Active) Created++; return previous with { Output = output };
    }

    internal static DeltaState Delta(DeltaState previous, double input, double delta)
    {
        if (previous.GetType() == typeof(DeltaState) && Same(previous.PreviousValue, input) && Same(previous.DeltaValue, delta)) { if (DiagnosticCounters.Active) Reused++; return previous; }
        if (DiagnosticCounters.Active) Created++; return previous with { PreviousValue = input, DeltaValue = delta };
    }

    internal static SignalTransmitterState Transmitter(SignalTransmitterState previous, double signal)
    {
        if (previous.GetType() == typeof(SignalTransmitterState) && Same(previous.SignalValue, signal)) { if (DiagnosticCounters.Active) Reused++; return previous; }
        if (DiagnosticCounters.Active) Created++; return previous with { SignalValue = signal };
    }

    internal static SignalProcessorState Processor(SignalProcessorState previous, double input, double output)
    {
        if (previous.GetType() == typeof(SignalProcessorState) && Same(previous.PreviousValue, input) && Same(previous.Output, output)) { if (DiagnosticCounters.Active) Reused++; return previous; }
        if (DiagnosticCounters.Active) Created++; return previous with { PreviousValue = input, Output = output };
    }

    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        => Rewrite(instructions, Recipes(__originalMethod));

    internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, Recipe[] recipes)
    {
        var codes = instructions.Select(c => new CodeInstruction(c)).ToList();
        foreach (var recipe in recipes)
        {
            var matches = 0;
            for (var i = 0; i < codes.Count; i++)
            {
                if (!(codes[i].operand is MethodInfo clone) || clone.DeclaringType != recipe.State || clone.Name != "<Clone>$") continue;
                var removed = new List<int> { i };
                var cursor = i + 1;
                for (var propertyIndex = 0; propertyIndex < recipe.Properties.Length; propertyIndex++)
                {
                    if (cursor >= codes.Count || codes[cursor].opcode != OpCodes.Dup) throw new InvalidOperationException("Record duplication pattern changed.");
                    removed.Add(cursor++);
                    var setter = AccessTools.PropertySetter(recipe.State, recipe.Properties[propertyIndex]);
                    var start = cursor;
                    while (cursor < codes.Count && !codes[cursor].Calls(setter))
                    {
                        // Inspected value expressions consist only of local/constant loads and arithmetic.
                        var code = codes[cursor];
                        if (!(code.opcode.Name!.StartsWith("ldloc") || code.opcode.Name.StartsWith("ldarg") || code.opcode.Name.StartsWith("ldc.") || code.opcode == OpCodes.Mul || code.opcode == OpCodes.Div || code.opcode == OpCodes.Conv_R8))
                            throw new InvalidOperationException("Record value expression changed in " + recipe.State.Name + "." + recipe.Properties[propertyIndex] + ": " + code);
                        cursor++;
                    }
                    if (cursor == start || cursor >= codes.Count) throw new InvalidOperationException("Record setter missing.");
                    if (propertyIndex + 1 < recipe.Properties.Length) removed.Add(cursor);
                    else { codes[cursor].opcode = OpCodes.Call; codes[cursor].operand = recipe.Factory; }
                    cursor++;
                }
                // A branch may target the original clone, but never an intermediate init operation.
                for (var j = i + 1; j < cursor; j++)
                    if (codes[j].labels.Count != 0 || codes[j].blocks.Count != 0) throw new InvalidOperationException("Record init has an internal branch/exception boundary.");
                foreach (var index in removed) { codes[index].opcode = OpCodes.Nop; codes[index].operand = null; }
                matches++; i = cursor - 1;
            }
            if (matches != recipe.Count) throw new InvalidOperationException("Native " + recipe.State.Name + " construction count changed: " + matches);
        }
        return codes;
    }
}
