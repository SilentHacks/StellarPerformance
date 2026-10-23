using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Core.Services;
using HarmonyLib;
using Ships;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.State;
using Ships.Interface.Services;
using Ships.Parts.Common.Model;
using Ships.Parts.WarpDrive;
using Ships.Services;
using WorldTracking.Interface.Services;
using WorldTracking.Spaces;
using WorldTracking.Spaces.Model;

namespace StellarPerformance;

// Optional adapter for the inspected Foundations binary. With no warp parts,
// both its grounded action and the native ungrounded action are empty loops.
// Keep the full composed action for any nonempty or unsupported input.
internal static class EmptyWarpUpdates
{
    static EmptyWarpUpdates() { }
    internal const string Owner = "stellarperformance.empty-warp-updates";
    internal const string FoundationsSha256 = "9E104285801DBA1B810AD85DDFFDEDFF9CFB14D4AF7EFF5D1FA66D879C244124";
    private static MethodInfo _before = null!;
    private static Func<TrackedShip, ShipStatefulPartsCache> _cache = null!;
    private static Func<ShipStatefulPartsCache, Dictionary<Type, HashSet<ushort>>> _parts = null!;
    private static Func<ShipWarpDrivesUpdater, IShipStateRead> _ship = null!;
    private static Func<ShipWarpDrivesUpdater, ISpacesServerProvider> _spaces = null!;
    private static Func<ShipWarpDrivesUpdater, IShipsServerProvider> _ships = null!;
    private static Func<SpacesServer, Dictionary<uint, ISpaceServerState>> _spaceStates = null!;
    private static HashSet<Type> _nativeSpaceStates = null!;
    [ThreadStatic] private static ShipWarpDrivesUpdater? _active;
    internal static bool Initialized { get; private set; }
    internal static bool Installed { get; private set; }
    internal static long Queries, Skipped, Fallbacks;
    internal static MethodInfo Target => AccessTools.Method(typeof(ShipWarpDrivesUpdater), "UpdateStates", new[] { typeof(float) });

    private sealed class Contract
    {
        internal readonly MethodInfo Before;
        internal readonly Type[] SpaceStates;
        internal readonly MethodBase[] Decisions;
        internal Contract(MethodInfo before, Type[] spaceStates, MethodBase[] decisions)
        { Before = before; SpaceStates = spaceStates; Decisions = decisions; }
    }

    private static Contract Inspect()
    {
        if (!Compatibility.Validate(new[] { "Ships", "Ships.Interface", "WorldTracking", "Core", "UnityEngine.CoreModule" }, out var reason))
            throw new InvalidOperationException(reason);
        var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => assembly.GetName().Name == "StellarFoundations").ToArray();
        if (assemblies.Length != 1) throw new InvalidOperationException("Exactly one inspected StellarFoundations assembly is required.");
        var foundations = assemblies[0];
        using (var input = File.OpenRead(foundations.Location))
        using (var sha = SHA256.Create())
            if (BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "") != FoundationsSha256)
                throw new InvalidOperationException("StellarFoundations differs from the inspected binary.");
        var hooks = foundations.GetType("StellarFoundations.RuntimeHooks", true)!;
        var registry = foundations.GetType("StellarFoundations.FoundationRegistry", true)!;
        var before = AccessTools.Method(hooks, "BeforeWarp", new[] { typeof(IShipStateRead), typeof(ShipState) });
        if (before == null || !before.IsStatic || before.ReturnType != typeof(bool) || Target == null)
            throw new MissingMethodException("Inspected Foundations/native warp entry points changed.");
        var stateTypes = typeof(SpacesServer).Assembly.GetTypes().Where(type => !type.IsAbstract && !type.ContainsGenericParameters && typeof(ISpaceServerState).IsAssignableFrom(type)).ToArray();
        var decisions = new List<MethodBase> {
            before, AccessTools.Method(hooks, "AssemblyGround", new[] { typeof(uint), typeof(bool) }),
            AccessTools.Method(registry, "Grounded", new[] { typeof(uint) }), AccessTools.PropertyGetter(registry, "Count"),
            AccessTools.Method(typeof(TrackedShip), "GetAllStatefulParts").MakeGenericMethod(typeof(WarpDriveState)),
            AccessTools.Method(typeof(ShipStatefulPartsCache), "GetStatefulPartsOfType").MakeGenericMethod(typeof(WarpDriveState)),
            AccessTools.Method(typeof(ShipStatefulPartsCache), "AddStatefulPart"), AccessTools.Method(typeof(ShipStatefulPartsCache), "RemoveStatefulPart"),
            AccessTools.PropertyGetter(typeof(TrackedShip), "Id"), AccessTools.PropertyGetter(typeof(TrackedShip), "SpaceId"),
            AccessTools.PropertyGetter(typeof(TrackedShip), "PhysicsState"), AccessTools.PropertyGetter(typeof(TrackedShip), "State"),
            AccessTools.PropertyGetter(typeof(ShipState), "Id"), AccessTools.PropertyGetter(typeof(ShipState), "PhysicsState"),
            AccessTools.Method(typeof(ServiceLocator), "GetService", Type.EmptyTypes).MakeGenericMethod(typeof(ShipsServerTracker)),
            AccessTools.Method(typeof(ShipsServerTracker), "TryGetRootShip"), AccessTools.Method(typeof(ShipsServerTracker), "GetRootConnectedShips"),
            AccessTools.Method(typeof(SpacesServer), "GetSpace"), AccessTools.Method(typeof(SpacesServer), "TryGetSpace"),
            AccessTools.Method(typeof(SpacesServer), "TryGetSpaceServerState"),
            AccessTools.PropertyGetter(typeof(UnityEngine.Component), "gameObject"),
            AccessTools.PropertyGetter(typeof(UnityEngine.GameObject), "activeInHierarchy"),
            AccessTools.Method(typeof(UnityEngine.Component), "CompareTag", new[] { typeof(string) }),
            AccessTools.Method(typeof(UnityEngine.Object), "op_Equality", new[] { typeof(UnityEngine.Object), typeof(UnityEngine.Object) }),
            AccessTools.Method(typeof(UnityEngine.Object), "op_Implicit", new[] { typeof(UnityEngine.Object) }) };
        foreach (var type in stateTypes) decisions.Add(AccessTools.PropertyGetter(type, "GenericSpace"));
        AddDictionaryContracts<Type, HashSet<ushort>>(decisions);
        AddDictionaryContracts<uint, ISpaceServerState>(decisions);
        decisions.Add(AccessTools.PropertyGetter(typeof(HashSet<ushort>), "Count"));
        decisions.Add(AccessTools.PropertyGetter(typeof(HashSet<ushort>), "Comparer"));
        decisions.Add(AccessTools.Method(typeof(HashSet<ushort>), "GetEnumerator"));
        decisions.Add(AccessTools.PropertyGetter(typeof(EqualityComparer<Type>), "Default"));
        decisions.Add(AccessTools.PropertyGetter(typeof(EqualityComparer<uint>), "Default"));
        decisions.Add(AccessTools.PropertyGetter(typeof(EqualityComparer<ushort>), "Default"));
        AddEnumeratorContracts(typeof(HashSet<ushort>.Enumerator), decisions);
        // The absent-type path returns this native readonly empty collection;
        // the retained-empty-set path uses the native LINQ projection.
        decisions.Add(AccessTools.Method(typeof(ReadOnlyCollection<StatefulPart>), "GetEnumerator"));
        decisions.Add(AccessTools.Method(typeof(List<StatefulPart>), "GetEnumerator"));
        AddEnumeratorContracts(typeof(List<StatefulPart>.Enumerator), decisions);
        decisions.Add(typeof(Enumerable).GetMethods().Single(method => method.Name == "Select" && method.IsGenericMethodDefinition &&
            method.GetParameters().Length == 2 && method.GetParameters()[1].ParameterType.GetGenericArguments().Length == 2).MakeGenericMethod(typeof(ushort), typeof(StatefulPart)));
        if (decisions.Any(method => method == null)) throw new MissingMethodException("A bypassed empty-warp contract changed.");
        return new Contract(before, stateTypes, decisions.Distinct().ToArray());
    }

    private static void AddDictionaryContracts<TKey, TValue>(List<MethodBase> methods) where TKey : notnull
    {
        var dictionary = typeof(Dictionary<TKey, TValue>);
        methods.Add(AccessTools.Method(dictionary, "TryGetValue"));
        methods.Add(AccessTools.PropertyGetter(dictionary, "Comparer"));
    }
    private static void AddEnumeratorContracts(Type type, List<MethodBase> methods)
    {
        methods.Add(AccessTools.Method(type, "MoveNext"));
        methods.Add(AccessTools.PropertyGetter(type, "Current"));
        methods.Add(AccessTools.Method(type, "Dispose"));
    }

    // Metadata and field-expression binding only: safe for emitted-copy tests.
    // Harmony ownership queries and runtime patch installation stay in Install.
    internal static void Initialize() => Bind(Inspect());
    private static void Bind(Contract contract)
    {
        if (Initialized) return;
        _cache = NativeAccess.Read<TrackedShip, ShipStatefulPartsCache>("<StatefulPartsCache>k__BackingField");
        _parts = NativeAccess.Read<ShipStatefulPartsCache, Dictionary<Type, HashSet<ushort>>>("_parts");
        _ship = NativeAccess.Read<ShipWarpDrivesUpdater, IShipStateRead>("_ship");
        _spaces = NativeAccess.Read<ShipWarpDrivesUpdater, ISpacesServerProvider>("_spaces");
        _ships = NativeAccess.Read<ShipWarpDrivesUpdater, IShipsServerProvider>("_ships");
        _spaceStates = NativeAccess.Read<SpacesServer, Dictionary<uint, ISpaceServerState>>("_spaces");
        _nativeSpaceStates = new HashSet<Type>(contract.SpaceStates);
        _before = contract.Before;
        Initialized = true;
    }

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var contract = Inspect();
        var patches = HarmonyLib.Harmony.GetPatchInfo(Target);
        if (patches == null || patches.Prefixes.Count != 1 || patches.Prefixes[0].PatchMethod != contract.Before ||
            patches.Postfixes.Count != 0 || patches.Transpilers.Count != 0 || patches.Finalizers.Count != 0)
            throw new InvalidOperationException("Only inspected Foundations BeforeWarp may observe the native warp wrapper.");
        foreach (var method in contract.Decisions) PatchSafety.RequireNoForeignOwners(method);
        GuardDefaultComparer(EqualityComparer<Type>.Default, typeof(Type));
        GuardDefaultComparer(EqualityComparer<uint>.Default, typeof(uint));
        GuardDefaultComparer(EqualityComparer<ushort>.Default, typeof(ushort));
        Bind(contract);
        try
        {
            harmony.Patch(Target,
                prefix: new HarmonyMethod(typeof(EmptyWarpUpdates), nameof(Enter)) { priority = Priority.First, before = new[] { patches.Prefixes[0].owner } },
                finalizer: new HarmonyMethod(typeof(EmptyWarpUpdates), nameof(Leave)));
            harmony.Patch(_before, prefix: new HarmonyMethod(typeof(EmptyWarpUpdates), nameof(BeforeFoundations)));
            Installed = true;
        }
        catch
        {
            // Installation is atomic with respect to this adapter's three hooks.
            // Leave Foundations and every other module's patches in place.
            harmony.Unpatch(_before, HarmonyPatchType.Prefix, harmony.Id);
            harmony.Unpatch(Target, HarmonyPatchType.Prefix, harmony.Id);
            harmony.Unpatch(Target, HarmonyPatchType.Finalizer, harmony.Id);
            _active = null;
            Installed = false;
            throw;
        }
    }

    private static void GuardDefaultComparer(object comparer, Type valueType)
    {
        var type = comparer.GetType();
        if (type.Assembly != typeof(EqualityComparer<>).Assembly)
            throw new InvalidOperationException("Native empty-warp default comparer changed.");
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(type, "Equals", new[] { valueType, valueType }));
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(type, "GetHashCode", new[] { valueType }));
    }

    private static void Enter(ShipWarpDrivesUpdater __instance, out ShipWarpDrivesUpdater? __state)
    { __state = _active; _active = __instance; }
    private static Exception? Leave(Exception? __exception, ShipWarpDrivesUpdater? __state)
    { _active = __state; return __exception; }
    private static bool BeforeFoundations(IShipStateRead __0, ref bool __result)
    {
        if (DiagnosticCounters.Active) Queries++;
        if (!CanSkip(_active, __0))
        { if (DiagnosticCounters.Active) Fallbacks++; return true; }
        if (DiagnosticCounters.Active) Skipped++;
        __result = false;
        return false;
    }

    // This is a fresh proof from the actual updater/cache/providers on every
    // call, with no answer, ground, root, service, topology or part-list cache.
    // Skipping AssemblyGround also leaves its _tmpShipsReturn scratch unchanged.
    // Inspected consumers rebuild and consume that list synchronously; it is
    // never authoritative state. The contract covers valid native scenes, not
    // foreign retained-scratch observers or exceptions from corrupt root graphs.
    internal static bool CanSkip(ShipWarpDrivesUpdater? owner, IShipStateRead ship)
    {
        if (!Initialized || owner == null || owner.GetType() != typeof(ShipWarpDrivesUpdater) || ship == null ||
            ship.GetType() != typeof(TrackedShipServer) || !ReferenceEquals(_ship(owner), ship)) return false;
        var provider = _spaces(owner);
        var shipProvider = _ships(owner);
        if (provider == null || provider.GetType() != typeof(SpacesServer) || shipProvider == null || shipProvider.GetType() != typeof(ShipsServerTracker)) return false;
        var spaces = (SpacesServer)provider;
        var ships = (ShipsServerTracker)shipProvider;
        // Unity fake-null and inactive providers belong to teardown/unsupported
        // lifecycle paths, where the original composed action must remain live.
        if (spaces == null || ships == null || !spaces.gameObject.activeInHierarchy || !ships.gameObject.activeInHierarchy ||
            !spaces.CompareTag("ServiceObject") || !ships.CompareTag("ServiceObject")) return false;
        var cache = _cache((TrackedShip)ship);
        if (cache == null || cache.GetType() != typeof(ShipStatefulPartsCache)) return false;
        var parts = _parts(cache);
        if (parts == null || parts.GetType() != typeof(Dictionary<Type, HashSet<ushort>>) || !ReferenceEquals(parts.Comparer, EqualityComparer<Type>.Default)) return false;
        if (parts.TryGetValue(typeof(WarpDriveState), out var ids) &&
            (ids == null || ids.GetType() != typeof(HashSet<ushort>) || !ReferenceEquals(ids.Comparer, EqualityComparer<ushort>.Default) || ids.Count != 0)) return false;
        var states = _spaceStates(spaces);
        return states != null && states.GetType() == typeof(Dictionary<uint, ISpaceServerState>) &&
            ReferenceEquals(states.Comparer, EqualityComparer<uint>.Default) && states.TryGetValue(ship.SpaceId, out var state) &&
            state != null && _nativeSpaceStates.Contains(state.GetType());
    }
}
