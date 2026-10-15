using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ships.Cables;
using Ships.Cables.Fluids;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Services;
using UnityEngine;

namespace StellarPerformance;

internal static class FluidSocketReads
{
    static FluidSocketReads() { }

    internal readonly struct Key : IEquatable<Key>
    {
        internal readonly GameObject Prefab;
        internal readonly bool IncludeInactive;
        internal Key(GameObject prefab, bool includeInactive) { Prefab = prefab; IncludeInactive = includeInactive; }
        public bool Equals(Key other) => ReferenceEquals(Prefab, other.Prefab) && IncludeInactive == other.IncludeInactive;
        public override bool Equals(object? obj) => obj is Key other && Equals(other);
        public override int GetHashCode() => unchecked(RuntimeHelpers.GetHashCode(Prefab) * 397) ^ (IncludeInactive ? 1 : 0);
    }

    internal static readonly MethodInfo ActiveRead = EngineRead(Type.EmptyTypes);
    internal static readonly MethodInfo InactiveRead = EngineRead(new[] { typeof(bool) });
    private static readonly Func<ConnectionsTracker, IShipsProvider?> Ships = BindShips();

    private static Func<ConnectionsTracker, IShipsProvider?> BindShips()
    {
        var tracker = Expression.Parameter(typeof(ConnectionsTracker), "tracker");
        var field = AccessTools.Field(typeof(ConnectionsTracker), "Ships") ?? throw new MissingFieldException("ConnectionsTracker.Ships");
        if (field.FieldType != typeof(IShipsProvider)) throw new InvalidOperationException("Live ships-provider field changed.");
        return Expression.Lambda<Func<ConnectionsTracker, IShipsProvider?>>(Expression.Field(tracker, field), tracker).Compile();
    }

    internal static IShipsProvider? GetShips(ConnectionsTracker tracker) => Ships(tracker);
    [ThreadStatic] private static ScopedReuse<Dictionary<Key, SocketObject[]>>? _pool;
    private static ScopedReuse<Dictionary<Key, SocketObject[]>> Pool => _pool ?? (_pool = new ScopedReuse<Dictionary<Key, SocketObject[]>>(() => new Dictionary<Key, SocketObject[]>(), map => map.Clear(), map => map.Count <= 128));

    private static MethodInfo EngineRead(Type[] arguments) => typeof(GameObject).GetMethods().Single(method => method.Name == nameof(GameObject.GetComponentsInChildren) && method.IsGenericMethodDefinition && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(arguments)).MakeGenericMethod(typeof(SocketObject));

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ConnectionsTracker), nameof(ConnectionsTracker.GetRelayedSocketConnections)), transpiler: new HarmonyMethod(typeof(FluidSocketReads), nameof(Transpile)));
        harmony.Patch(AccessTools.Method(typeof(FluidCablesVisualUpdater), nameof(FluidCablesVisualUpdater.UpdateFluidCables)), prefix: new HarmonyMethod(typeof(FluidSocketReads), nameof(Begin)) { priority = Priority.First }, finalizer: new HarmonyMethod(typeof(FluidSocketReads), nameof(End)) { priority = Priority.Last });
    }

    internal static MethodInfo ContentMethod() => AccessTools.Method(Assembly.Load("SDModKit").GetType("SDModKit.Game.ItemCables", true), "BeforeRelayedConnections", new[] { typeof(ConnectionsTracker), typeof(EntityPartSocket), typeof(IReadOnlyCollection<SocketConnection>).MakeByRefType() }) ?? throw new MissingMethodException("SDModKit item-cable relay prefix changed.");
    internal static void InstallContent(HarmonyLib.Harmony harmony) => harmony.Patch(ContentMethod(), transpiler: new HarmonyMethod(typeof(FluidSocketReads), nameof(Transpile)));

    internal static void Begin(out ScopedReuse<Dictionary<Key, SocketObject[]>>.Lease __state)
    {
        __state = Pool.Enter();
        Pool.Rent();
    }

    internal static Exception? End(Exception? __exception, ScopedReuse<Dictionary<Key, SocketObject[]>>.Lease? __state)
    {
        if (__state != null) Pool.Exit(__state);
        return __exception;
    }

    internal static SocketObject[] GetActiveSockets(GameObject prefab) => GetSockets(prefab, false);
    internal static SocketObject[] GetSockets(GameObject prefab, bool includeInactive)
    {
        var scope = _pool?.Current;
        if (scope == null || ReferenceEquals(prefab, null)) return prefab!.GetComponentsInChildren<SocketObject>(includeInactive);
        var map = scope.Items[0];
        var key = new Key(prefab, includeInactive);
        if (map.TryGetValue(key, out var sockets)) return sockets;
        sockets = prefab.GetComponentsInChildren<SocketObject>(includeInactive);
        if (map.Count < 2048) map.Add(key, sockets);
        return sockets;
    }

    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var active = __originalMethod.DeclaringType == typeof(ConnectionsTracker) && __originalMethod.Name == nameof(ConnectionsTracker.GetRelayedSocketConnections);
        if (!active && (__originalMethod.DeclaringType?.FullName != "SDModKit.Game.ItemCables" || __originalMethod.Name != "BeforeRelayedConnections")) throw new InvalidOperationException("Unsupported fluid socket read target.");
        var original = active ? ActiveRead : InactiveRead;
        var replacement = AccessTools.Method(typeof(FluidSocketReads), active ? nameof(GetActiveSockets) : nameof(GetSockets));
        var codes = instructions.Select(code => new CodeInstruction(code)).ToList();
        var matches = codes.Where(code => code.Calls(original)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Fluid socket hierarchy read pattern changed.");
        matches[0].opcode = OpCodes.Call;
        matches[0].operand = replacement;
        return active ? codes : RewriteShipProvider(codes);
    }

    internal static List<CodeInstruction> RewriteShipProvider(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.Select(code => new CodeInstruction(code)).ToList();
        var create = AccessTools.Method(typeof(Traverse), nameof(Traverse.Create), new[] { typeof(object) });
        var matches = codes.Select((code, index) => new { code, index }).Where(item => item.code.Calls(create)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("SDModKit live ships-provider lookup changed.");
        var start = matches[0].index;
        var fieldRead = typeof(Traverse).GetMethods().Single(method => method.Name == nameof(Traverse.Field) && !method.IsGenericMethod && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { typeof(string) }));
        if (start + 3 >= codes.Count || codes[start + 1].opcode != OpCodes.Ldstr || !Equals(codes[start + 1].operand, "Ships") || !codes[start + 2].Calls(fieldRead) || !(codes[start + 3].operand is MethodInfo get) || get.DeclaringType != typeof(Traverse) || get.Name != "GetValue" || !get.IsGenericMethod || get.GetGenericArguments().Single() != typeof(IShipsProvider) || codes.Skip(start + 1).Take(3).Any(code => code.labels.Count != 0 || code.blocks.Count != 0) || codes[start].blocks.Count != 0)
            throw new InvalidOperationException("SDModKit live ships-provider lookup pattern changed.");
        codes[start].opcode = OpCodes.Call;
        codes[start].operand = AccessTools.Method(typeof(FluidSocketReads), nameof(GetShips));
        codes.RemoveRange(start + 1, 3);
        return codes;
    }
}
