using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ships;
using Ships.Cables;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Model.State;
using Ships.Parts.Common.Model;
using Ships.Parts.DockingDoor;
using Ships.Parts.Electrolyzer;
using Ships.Parts.EthanolDistiller;
using Ships.Parts.FluidPump;
using Ships.Parts.FluidValve;
using Ships.Parts.GasExhaust;
using Ships.Parts.GasIntake;
using Ships.Parts.Gyroscope;
using Ships.Parts.MemoryRegister;
using Ships.Parts.ReactionWheel;
using Ships.Parts.Rotor;
using Ships.Parts.SignalDisplay;
using Ships.Parts.Thruster;
using Ships.Parts.WarpDrive;
using Ships.Parts.WarpExhaust;
using Ships.Parts.WoodPowerGenerator;
using Ships.Parts.WoodThruster;
using Ships.Services;

namespace StellarPerformance;

internal static class StatefulPartQueries
{
    static StatefulPartQueries() { }

    private sealed class TypeEntry
    {
        internal HashSet<ushort>? Ids;
        internal int Count;
        internal ReadOnlyCollection<StatefulPart> Parts = Empty;
    }

    private sealed class ShipEntry
    {
        internal readonly Dictionary<Type, TypeEntry> ByType = new Dictionary<Type, TypeEntry>();
        internal void Invalidate() => ByType.Clear();
    }

    internal static readonly ReadOnlyCollection<StatefulPart> Empty = Array.AsReadOnly(Array.Empty<StatefulPart>());
    private static readonly Func<TrackedShip, ShipStatefulPartsCache> CacheOf = BindCache();
    private static readonly Func<ShipStatefulPartsCache, Dictionary<Type, HashSet<ushort>>> PartsMapOf = NativeAccess.Read<ShipStatefulPartsCache, Dictionary<Type, HashSet<ushort>>>("_parts");
    private static readonly Func<ShipStatefulPartsCache, ShipState> StateOf = NativeAccess.Read<ShipStatefulPartsCache, ShipState>("_networkState");
    private static readonly ConditionalWeakTable<ShipStatefulPartsCache, ShipEntry> Entries = new ConditionalWeakTable<ShipStatefulPartsCache, ShipEntry>();
    private static readonly MethodInfo OpenGetParts = AccessTools.Method(typeof(StatefulPartQueries), nameof(GetParts)) ?? throw new MissingMethodException("StatefulPartQueries.GetParts");

    internal static readonly MethodInfo[] Targets = new[]
    {
        AccessTools.Method(typeof(TrackedShipServer), nameof(TrackedShipServer.UpdateSignalEmitters)),
        AccessTools.Method(typeof(TrackedShipServer), nameof(TrackedShipServer.UpdateParts)),
        AccessTools.Method(typeof(SignalCablesVisualUpdater), nameof(SignalCablesVisualUpdater.UpdateSignalCables)),
        AccessTools.Method(typeof(WarpExhaustVisuals), "UpdateAnimationTime"),
        AccessTools.Method(typeof(SpacesMagnetsServerCache), nameof(SpacesMagnetsServerCache.UpdateCache)),
        AccessTools.Method(typeof(WoodThrustersUpdater), nameof(WoodThrustersUpdater.Update)),
        AccessTools.Method(typeof(PowerGeneratorUpdaters), nameof(PowerGeneratorUpdaters.WriteProductionCapacity)),
        AccessTools.Method(typeof(PowerGeneratorUpdaters), nameof(PowerGeneratorUpdaters.Update)),
        AccessTools.Method(typeof(ShipWarpDrivesUpdater), nameof(ShipWarpDrivesUpdater.UpdateStates)),
        AccessTools.Method(typeof(ShipThrustersUpdater), nameof(ShipThrustersUpdater.WriteFluidTargetConsumption)),
        AccessTools.Method(typeof(ShipThrustersUpdater), nameof(ShipThrustersUpdater.Update)),
        AccessTools.Method(typeof(SignalDisplayUpdater), nameof(SignalDisplayUpdater.Update)),
        AccessTools.Method(typeof(RotorsUpdater), nameof(RotorsUpdater.Update)),
        AccessTools.Method(typeof(ReactionWheelsUpdater), nameof(ReactionWheelsUpdater.Update)),
        AccessTools.Method(typeof(MemoryRegistersUpdater), nameof(MemoryRegistersUpdater.Update)),
        AccessTools.Method(typeof(GyroscopesUpdater), "CalculateShipGyros"),
        AccessTools.Method(typeof(GasIntakesUpdater), nameof(GasIntakesUpdater.Update)),
        AccessTools.Method(typeof(GasExhaustsUpdater), nameof(GasExhaustsUpdater.Update)),
        AccessTools.Method(typeof(FluidValvesUpdater), nameof(FluidValvesUpdater.Update)),
        AccessTools.Method(typeof(FluidPumpsServerUpdater), nameof(FluidPumpsServerUpdater.Update)),
        AccessTools.Method(typeof(EthanolDistillersUpdater), nameof(EthanolDistillersUpdater.GuessPowerConsumptionAndProductionRate)),
        AccessTools.Method(typeof(EthanolDistillersUpdater), nameof(EthanolDistillersUpdater.Update)),
        AccessTools.Method(typeof(ElectrolyzersUpdater), nameof(ElectrolyzersUpdater.WritePowerRequest)),
        AccessTools.Method(typeof(ElectrolyzersUpdater), nameof(ElectrolyzersUpdater.Update)),
        AccessTools.Method(typeof(DockingDoorsUpdater), nameof(DockingDoorsUpdater.Update))
    };

    private static Func<TrackedShip, ShipStatefulPartsCache> BindCache()
    {
        var ship = Expression.Parameter(typeof(TrackedShip), "ship");
        var property = AccessTools.Property(typeof(TrackedShip), "StatefulPartsCache") ?? throw new MissingMemberException("TrackedShip.StatefulPartsCache");
        if (property.PropertyType != typeof(ShipStatefulPartsCache)) throw new InvalidOperationException("TrackedShip.StatefulPartsCache type changed.");
        return Expression.Lambda<Func<TrackedShip, ShipStatefulPartsCache>>(Expression.Property(ship, property), ship).Compile();
    }

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var add = AccessTools.Method(typeof(ShipStatefulPartsCache), nameof(ShipStatefulPartsCache.AddStatefulPart), new[] { typeof(StatefulPart) }) ?? throw new MissingMethodException("ShipStatefulPartsCache.AddStatefulPart");
        var remove = AccessTools.Method(typeof(ShipStatefulPartsCache), nameof(ShipStatefulPartsCache.RemoveStatefulPart), new[] { typeof(StatefulPart) }) ?? throw new MissingMethodException("ShipStatefulPartsCache.RemoveStatefulPart");
        foreach (var mutation in new[] { add, remove })
            harmony.Patch(mutation, prefix: new HarmonyMethod(typeof(StatefulPartQueries), nameof(Invalidate)), finalizer: new HarmonyMethod(typeof(StatefulPartQueries), nameof(OnMutationEnd)));
        foreach (var target in Targets)
        {
            if (target == null) throw new MissingMethodException("StatefulPartQueries target missing.");
            harmony.Patch(target, transpiler: new HarmonyMethod(typeof(StatefulPartQueries), nameof(Transpile)));
        }
    }

    internal static void Invalidate(ShipStatefulPartsCache __instance)
    {
        if (__instance != null && Entries.TryGetValue(__instance, out var entry)) entry.Invalidate();
    }

    internal static Exception? OnMutationEnd(Exception? __exception, ShipStatefulPartsCache __instance)
    {
        Invalidate(__instance);
        return __exception;
    }

    internal static IEnumerable<StatefulPart> GetParts<T>(IShipStateRead ship) where T : IStatefulPartState
    {
        if (!(ship is TrackedShip tracked)) return ship.GetAllStatefulParts<T>();
        var cache = CacheOf(tracked);
        var partsByType = PartsMapOf(cache);
        if (!partsByType.TryGetValue(typeof(T), out var ids) || ids.Count == 0) return Empty;
        var shipEntry = Entries.GetValue(cache, _ => new ShipEntry());
        if (shipEntry.ByType.TryGetValue(typeof(T), out var typeEntry) && ReferenceEquals(typeEntry.Ids, ids) && typeEntry.Count == ids.Count)
            return typeEntry.Parts;
        var state = StateOf(cache);
        var list = new List<StatefulPart>(ids.Count);
        foreach (var id in ids)
            if (state.TryGetPart(id, out var part) && part != null)
                list.Add(part);
        var resolved = list.Count == 0 ? Empty : Array.AsReadOnly(list.ToArray());
        if (typeEntry == null) shipEntry.ByType[typeof(T)] = typeEntry = new TypeEntry();
        typeEntry.Ids = ids;
        typeEntry.Count = ids.Count;
        typeEntry.Parts = resolved;
        return resolved;
    }

    internal static bool IsGetAllStatefulParts(MethodInfo method) =>
        method.IsGenericMethod && method.Name == nameof(IShipStateRead.GetAllStatefulParts) && method.GetParameters().Length == 0 &&
        (method.DeclaringType == typeof(IShipStateRead) || method.DeclaringType == typeof(TrackedShip));

    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.Select(code => new CodeInstruction(code)).ToList();
        var count = 0;
        foreach (var code in codes)
        {
            if (!(code.operand is MethodInfo method) || !IsGetAllStatefulParts(method) || (code.opcode != OpCodes.Call && code.opcode != OpCodes.Callvirt)) continue;
            code.opcode = OpCodes.Call;
            code.operand = OpenGetParts.MakeGenericMethod(method.GetGenericArguments()[0]);
            count++;
        }
        if (count == 0) throw new InvalidOperationException("Expected GetAllStatefulParts call pattern was not found.");
        return codes;
    }
}
