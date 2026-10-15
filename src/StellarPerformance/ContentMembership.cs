using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ships;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.State;

namespace StellarPerformance;

internal static class ContentMembership
{
    static ContentMembership() { }

    private static readonly ConditionalWeakTable<ShipState, MembershipSnapshot<StatefulPart>> Entries = new ConditionalWeakTable<ShipState, MembershipSnapshot<StatefulPart>>();

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var assembly = Assembly.Load("SDModKit");
        var runtime = assembly.GetType("SDModKit.Content.ContentRuntime", true)!;
        var partsOn = AccessTools.Method(runtime, "PartsOn");
        PatchSafety.RejectDuplicateCache(partsOn, "stellarworkshop.membership");
        PatchSafety.RequireOriginalBody(partsOn, harmony.Id);
        if (!partsOn.ReturnType.IsGenericType || partsOn.ReturnType.GetGenericTypeDefinition() != typeof(IEnumerable<>)) throw new InvalidOperationException("Unexpected content enumeration contract.");
        var patch = typeof(TypedContent<>).MakeGenericType(partsOn.ReturnType.GetGenericArguments()[0]);
        AccessTools.Method(patch, "Configure").Invoke(null, new object[] { assembly });
        foreach (var name in new[] { "AddStatefulPart", "AddStatefulPartWithoutInit", "RemoveStatefulPart" })
            harmony.Patch(AccessTools.Method(typeof(ShipState), name), transpiler: new HarmonyMethod(typeof(ContentMembership), nameof(InvalidateAfterMutation)));
        harmony.Patch(partsOn, prefix: new HarmonyMethod(AccessTools.Method(patch, "BeforePartsOn")));
    }

    internal static void Invalidate(ShipState state)
    {
        if (Entries.TryGetValue(state, out var snapshot)) snapshot.Invalidate();
    }

    internal static IEnumerable<CodeInstruction> InvalidateAfterMutation(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>();
        var count = 0;
        foreach (var original in instructions)
        {
            var code = new CodeInstruction(original);
            codes.Add(code);
            if (!(code.operand is MethodInfo method) || method.DeclaringType != typeof(Dictionary<ushort, StatefulPart>) || (method.Name != "Add" && method.Name != "Remove") || (code.opcode != OpCodes.Call && code.opcode != OpCodes.Callvirt)) continue;
            if (code.blocks.Count != 0) throw new InvalidOperationException("Unexpected part mutation exception boundary.");
            codes.Add(new CodeInstruction(OpCodes.Ldarg_0));
            codes.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ContentMembership), nameof(Invalidate))));
            count++;
        }
        if (count != 1) throw new InvalidOperationException("Part membership mutation pattern changed.");
        return codes;
    }

    internal static StatefulPart[] Snapshot(ShipState state, IEnumerable<StatefulPart> source) => Entries.GetValue(state, _ => new MembershipSnapshot<StatefulPart>()).Get(source);
}

internal static class TypedContent<T> where T : struct
{
    private static Func<IShipStateWrite, StatefulPart, T?> _build = null!;

    internal static void Configure(Assembly assembly)
    {
        var registry = assembly.GetType("SDModKit.Content.ContentRegistry", true)!;
        var partDef = AccessTools.Method(registry, "PartDef", new[] { typeof(ushort) });
        var data = assembly.GetType("SDModKit.Content.PartData", true)!;
        var config = AccessTools.Method(data, "For", new[] { typeof(uint), typeof(ushort), partDef.ReturnType });
        var ship = Expression.Parameter(typeof(IShipStateWrite), "ship");
        var part = Expression.Parameter(typeof(StatefulPart), "part");
        var definition = Expression.Variable(partDef.ReturnType, "definition");
        var shipId = Expression.Property(Expression.Convert(ship, typeof(IShipStateRead)), "Id");
        var partId = Expression.Field(part, nameof(StatefulPart.Id));
        var resolved = Expression.MemberInit(Expression.New(typeof(T)),
            Bind("Ship", ship), Bind("ShipId", shipId), Bind("PartId", partId), Bind("Part", part), Bind("Def", definition),
            Bind("Config", Expression.Call(config, shipId, partId, definition)));
        var body = Expression.Block(new[] { definition },
            Expression.Assign(definition, Expression.Call(partDef, Expression.Field(Expression.Property(part, nameof(StatefulPart.Settings)), "id"))),
            Expression.Condition(Expression.ReferenceEqual(definition, Expression.Constant(null, partDef.ReturnType)), Expression.Default(typeof(T?)), Expression.Convert(resolved, typeof(T?))));
        _build = Expression.Lambda<Func<IShipStateWrite, StatefulPart, T?>>(body, ship, part).Compile();
    }

    private static MemberAssignment Bind(string name, Expression value) => Expression.Bind(typeof(T).GetField(name, BindingFlags.Public | BindingFlags.Instance) ?? throw new MissingFieldException(typeof(T).FullName, name), value);

    private static bool BeforePartsOn(IShipStateWrite ship, ref IEnumerable<T> __result)
    {
        if (!(ship is TrackedShipServer tracked) || tracked.GetType() != typeof(TrackedShipServer)) return true;
        __result = Resolve(tracked);
        return false;
    }

    private static IEnumerable<T> Resolve(TrackedShipServer ship)
    {
        var snapshot = ContentMembership.Snapshot(ship.State, ship.StatefulParts);
        foreach (var part in snapshot)
        {
            if (part == null || part.Settings == null) continue;
            var resolved = _build(ship, part);
            if (resolved.HasValue) yield return resolved.Value;
        }
    }
}
