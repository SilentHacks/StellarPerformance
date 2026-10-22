using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ships.Cables;
using Ships.Cables.Model;
using Ships.Interface.Model.Parts.Common;

namespace StellarPerformance;

// Cache only the native connection union used during signal discovery. Socket
// classification, values, arithmetic, operation order and callbacks remain native.
internal static class SignalConnectionScan
{
    static SignalConnectionScan() { }
    private const int EntryLimit = 1024, ByteLimit = 8 * 1024 * 1024, SocketLimit = 4096;
    private sealed class SocketStamp
    {
        internal EntityPartSocket Key;
        internal HashSet<SocketConnection> Set = null!;
        internal int Count;
        internal HashSet<SocketConnection>.Enumerator Version;
    }
    private sealed class Snapshot
    {
        internal SocketStamp[] Sockets = null!;
        internal IReadOnlyCollection<SocketConnection> Connections = null!;
        internal int Bytes;
    }
    private static ConditionalWeakTable<ConnectionGroup, Snapshot> _cache = new ConditionalWeakTable<ConnectionGroup, Snapshot>();
    private static int _entries, _bytes;
    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var target = AccessTools.Method(typeof(SignalConnectionGroupValues), nameof(SignalConnectionGroupValues.GetGroupInputsAndOutputs));
        PatchSafety.RequireOriginalBody(target, harmony.Id);
        PatchSafety.RequireNoForeignOwners(AccessTools.PropertyGetter(typeof(ConnectionGroup), nameof(ConnectionGroup.AllConnections)));
        harmony.Patch(target, transpiler: new HarmonyMethod(typeof(SignalConnectionScan), nameof(Rewrite)));
    }
    private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
    {
        var getter = AccessTools.PropertyGetter(typeof(ConnectionGroup), nameof(ConnectionGroup.AllConnections));
        var replacement = AccessTools.Method(typeof(SignalConnectionScan), nameof(Read));
        var found = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(getter)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; found++; }
            yield return instruction;
        }
        if (found != 1) throw new InvalidOperationException("Signal connection union contract changed.");
    }
    internal static IReadOnlyCollection<SocketConnection> Read(ConnectionGroup group)
    {
        if (_cache.TryGetValue(group, out var cached))
        {
            if (Current(group, cached)) return cached.Connections;
            _cache.Remove(group); _entries--; _bytes -= cached.Bytes;
        }
        var connections = group.AllConnections;
        var sockets = group.Sockets.Count;
        var bytes = 256L + 192L * sockets + 160L * connections.Count;
        if (sockets > SocketLimit || _entries >= EntryLimit || bytes > ByteLimit - _bytes) return connections;
        var snapshot = new Snapshot { Sockets = new SocketStamp[sockets], Connections = connections, Bytes = (int)bytes };
        var i = 0;
        foreach (var pair in group.Sockets)
            snapshot.Sockets[i++] = new SocketStamp { Key = pair.Key, Set = pair.Value, Count = pair.Value.Count, Version = pair.Value.GetEnumerator() };
        _cache.Add(group, snapshot); _entries++; _bytes += snapshot.Bytes;
        return connections;
    }
    private static bool Current(ConnectionGroup group, Snapshot snapshot)
    {
        if (group.Sockets.Count != snapshot.Sockets.Length) return false;
        var i = 0;
        foreach (var pair in group.Sockets)
        {
            var stamp = snapshot.Sockets[i++];
            if (!pair.Key.Equals(stamp.Key) || !ReferenceEquals(pair.Value, stamp.Set) || pair.Value.Count != stamp.Count) return false;
            // A copied enumerator checks the public HashSet's mutation version
            // in O(1). Count/reference/order checks also cover dictionary edits.
            var version = stamp.Version;
            try { version.MoveNext(); }
            catch (InvalidOperationException) { return false; }
        }
        return true;
    }
    internal static void Clear()
    {
        _cache = new ConditionalWeakTable<ConnectionGroup, Snapshot>(); _entries = 0; _bytes = 0;
    }
}
