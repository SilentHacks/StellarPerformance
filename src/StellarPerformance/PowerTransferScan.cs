using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Managers.Server.Network;
using Ships.Cables;
using Ships.Cables.Power;
using Ships.Interface.Model.Parts.Common;

namespace StellarPerformance;

// Native rebuilds append cable IDs to an existing list. Repeated IDs within one
// group's transfer pass write the same value and native equality suppresses their
// notifications. Retain the original list, all stale IDs and first-occurrence
// order; project only while the notification observers retain inspected behavior.
internal static class PowerTransferScan
{
    static PowerTransferScan() { }
    internal const string Owner = "stellarperformance.power-transfer-scan";
    internal const int EntryLimit = 1024, ByteLimit = 8 * 1024 * 1024, UniqueLimit = 65536, SourceLimit = 4 * 1024 * 1024;
    private sealed class Snapshot
    {
        internal List<uint>.Enumerator Version;
        internal List<uint>? Unique;
        internal int Bytes;
    }
    private static ConditionalWeakTable<List<uint>, Snapshot> _cache = new ConditionalWeakTable<List<uint>, Snapshot>();
    private static int _entries, _bytes;
    private static MethodInfo? _nativeObserver;
    [ThreadStatic] private static Action<CablePowerData>? _lastObservers;
    [ThreadStatic] private static bool _lastObserversAllowed;

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var target = AccessTools.Method(typeof(PowerServerTracker), nameof(PowerServerTracker.UpdatePowerTransfers));
        PatchSafety.RequireOriginalBody(target, Owner);
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(typeof(CablesServerTracker), nameof(CablesServerTracker.UpdatePowerCableState)));
        PatchSafety.RequireNoForeignOwners(AccessTools.PropertyGetter(typeof(CablesServerTracker), nameof(CablesServerTracker.OnCablePowerUpdated)));
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(typeof(CablePowerState), nameof(CablePowerState.Equals), new[] { typeof(CablePowerState) }));
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(typeof(CablePowerState), "op_Equality"));
        var nativeObserver = AccessTools.Method(typeof(MasterSync), nameof(MasterSync.OnCablePowerUpdate));
        PatchSafety.RequireNoForeignOwners(nativeObserver);
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(typeof(MasterSync), "QueueChange"));
        _nativeObserver = nativeObserver;
        harmony.Patch(target, transpiler: new HarmonyMethod(typeof(PowerTransferScan), nameof(Rewrite)));
    }

    internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
    {
        var cableIds = AccessTools.Field(typeof(PowerGroupServerState), nameof(PowerGroupServerState.CableIds));
        var cables = AccessTools.Field(typeof(PowerServerTracker), "_cablesServerTracker") ?? throw new MissingFieldException("PowerServerTracker._cablesServerTracker");
        if (cables.FieldType != typeof(CablesServerTracker)) throw new InvalidOperationException("Power cable tracker contract changed.");
        var read = AccessTools.Method(typeof(PowerTransferScan), nameof(Read));
        var found = 0;
        foreach (var code in instructions)
        {
            yield return code;
            if (code.opcode != OpCodes.Ldfld || !Equals(code.operand, cableIds)) continue;
            // The source list remains on the stack, followed by its native updater.
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Ldfld, cables);
            yield return new CodeInstruction(OpCodes.Call, read);
            found++;
        }
        if (found != 1) throw new InvalidOperationException("Power transfer cable enumeration contract changed.");
    }

    internal static List<uint> Read(List<uint> source, CablesServerTracker cables) =>
        ObserversSupported(cables.OnCablePowerUpdated) ? Project(source) : source;

    internal static bool ObserversSupported(Action<CablePowerData>? observers)
    {
        if (observers == null) return true;
        if (ReferenceEquals(observers, _lastObservers)) return _lastObserversAllowed;
        var allowed = ObserversMatch(observers, _nativeObserver);
        _lastObservers = observers;
        _lastObserversAllowed = allowed;
        return allowed;
    }

    // The native contract is bound only by guarded installation. Keeping this
    // comparison separate also permits offline delegate tests without loading
    // FishNet's Mono-only MasterSync inheritance on CoreCLR.
    internal static bool ObserversMatch(Action<CablePowerData> observers, MethodInfo? nativeObserver)
    {
        if (nativeObserver == null) return false;
        foreach (var observer in observers.GetInvocationList())
            if (observer.Method != nativeObserver) return false;
        return true;
    }

    internal static List<uint> Project(List<uint> source)
    {
        if (_cache.TryGetValue(source, out var cached))
        {
            // A copied List enumerator checks its mutation version in O(1),
            // including same-count replacement, sorting, and index assignment.
            var stamp = cached.Version;
            try { stamp.MoveNext(); return cached.Unique ?? source; }
            catch (InvalidOperationException) { _cache.Remove(source); _entries--; _bytes -= cached.Bytes; }
        }
        if (source.Count < 2 || source.Count > SourceLimit || _entries >= EntryLimit || ByteLimit - _bytes < 256) return source;
        var seen = new HashSet<uint>();
        var unique = new List<uint>(Math.Min(source.Count, 256));
        foreach (var id in source)
        {
            if (!seen.Add(id)) continue;
            // Temporary as well as retained projection storage is bounded.
            if (unique.Count >= UniqueLimit || 256L + 16L * (unique.Count + 1) > ByteLimit - _bytes) return source;
            unique.Add(id);
        }
        var projection = unique.Count == source.Count ? null : unique;
        var bytes = projection == null ? 256 : 256 + 16 * unique.Count;
        _cache.Add(source, new Snapshot { Version = source.GetEnumerator(), Unique = projection, Bytes = bytes });
        _entries++; _bytes += bytes;
        return projection ?? source;
    }

    internal static void Clear()
    {
        _cache = new ConditionalWeakTable<List<uint>, Snapshot>(); _entries = 0; _bytes = 0;
        _lastObservers = null; _lastObserversAllowed = false;
    }
}
