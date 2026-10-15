using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Managers.Server.Network;
using WorldObjects.Services;

namespace StellarPerformance;

internal static class WorldSnapshotPatch
{
    static WorldSnapshotPatch() { }

    internal static void Install(HarmonyLib.Harmony harmony) => harmony.Patch(AccessTools.Method(typeof(MasterSyncGameObject), nameof(MasterSyncGameObject.SyncUnreliable)), transpiler: new HarmonyMethod(typeof(WorldSnapshotPatch), nameof(Transpile)));

    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, ILGenerator generator) => Rewrite(instructions, generator, AccessTools.PropertyGetter(typeof(WorldObjectsServerTracker), nameof(WorldObjectsServerTracker.WorldObjects)));

    internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodInfo getter)
    {
        var codes = instructions.Select(code => new CodeInstruction(code)).ToList();
        var matches = codes.Select((code, index) => new { code, index }).Where(item => item.code.Calls(getter)).ToArray();
        if (matches.Length != 2 || matches.Any(item => item.code.blocks.Count != 0)) throw new InvalidOperationException("World-object snapshot access pattern changed.");
        var snapshot = generator.DeclareLocal(getter.ReturnType);
        var second = matches[1].index;
        codes[second].opcode = OpCodes.Pop;
        codes[second].operand = null;
        codes.Insert(second + 1, new CodeInstruction(OpCodes.Ldloc, snapshot));
        var first = matches[0].index;
        codes.InsertRange(first + 1, new[] { new CodeInstruction(OpCodes.Dup), new CodeInstruction(OpCodes.Stloc, snapshot) });
        return codes;
    }
}
