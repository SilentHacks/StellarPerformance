using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Core.Services;
using FishNet;
using HarmonyLib;
using Ships;
using Ships.Interface.Model;
using Ships.Rooms;
using Ships.Services;
using Ships.Rooms.Tests;
using UnityEngine;

namespace StellarPerformance;

// A single-player host already owns authoritative room geometry. Keep the local
// mirror empty and forward position queries; never share a mutable room/tree.
internal static class SinglePlayerRooms
{
    static SinglePlayerRooms() { }
    internal const string Owner = "stellarperformance.single-player-rooms";
    private sealed class Mirror
    {
        internal readonly TrackedShipClient Client;
        internal readonly ShipRooms Authority;
        internal Mirror(TrackedShipClient client, ShipRooms authority) { Client = client; Authority = authority; }
    }
    private static readonly ConditionalWeakTable<ShipRooms, Mirror> Mirrors = new ConditionalWeakTable<ShipRooms, Mirror>();
    private static readonly List<WeakReference<ShipRooms>> Retained = new List<WeakReference<ShipRooms>>();
    private static Func<TrackedShip, ShipRooms> RoomsOf = null!;
    private static Func<ShipRoomGridVisualizer, bool> DrawWalls = null!, DrawRooms = null!, DrawLeaves = null!;
    private static readonly ConditionalWeakTable<ShipRoomGridVisualizer, ShipRooms> Visualizers = new ConditionalWeakTable<ShipRoomGridVisualizer, ShipRooms>();
    internal static Action<string>? Log;

    internal static void Bind()
    {
        RoomsOf = NativeAccess.Read<TrackedShip, ShipRooms>("<Rooms>k__BackingField");
        DrawWalls = NativeAccess.Read<ShipRoomGridVisualizer, bool>("generateWalls");
        DrawRooms = NativeAccess.Read<ShipRoomGridVisualizer, bool>("generateRooms");
        DrawLeaves = NativeAccess.Read<ShipRoomGridVisualizer, bool>("generateTreeLeaves");
    }
    internal static void Track(ShipRooms rooms, TrackedShipClient client, ShipRooms authority)
    {
        Retained.RemoveAll(reference => !reference.TryGetTarget(out var target) || !Mirrors.TryGetValue(target, out _));
        if (Retained.Count >= 8192 || Mirrors.TryGetValue(rooms, out _)) return;
        Mirrors.Add(rooms, new Mirror(client, authority)); Retained.Add(new WeakReference<ShipRooms>(rooms));
    }
    private static void Created(ShipRooms __instance, TrackedShip ship)
    {
        if (!(ship is TrackedShipClient client) || !InstanceFinder.IsServerStarted || InstanceFinder.ServerManager.Clients.Count != 1) return;
        var server = ServiceLocator.GetService<ShipsServerTracker>()?.GetShip(ship.Id);
        if (server != null) Track(__instance, client, RoomsOf(server));
    }
    internal static bool BeforeMutation(ShipRooms __instance) => !Mirrors.TryGetValue(__instance, out _);
    internal static bool BeforeSample(ShipRooms __instance, Vector3 position, ref RoomSampleResult __result)
    {
        if (!Mirrors.TryGetValue(__instance, out var mirror)) return true;
        __result = mirror.Authority.SampleRoomAtPosition(position); return false;
    }
    internal static void Restore(ShipRooms rooms)
    {
        if (!Mirrors.TryGetValue(rooms, out var mirror)) return;
        Mirrors.Remove(rooms); // All rebuilding and subsequent events use native paths.
        foreach (var hull in mirror.Client.State.GetHullPartObjects()) rooms.AddHullPart(hull);
        foreach (var part in mirror.Client.State.GetStatefulParts()) rooms.InitializeStatefulPart(part);
        if (mirror.Client.IsRigidbodyLoaded) rooms.UpdateRooms();
    }
    // A debug visualizer consumes the mutable native tree, so retain its full mirror.
    private static bool Drawing(ShipRoomGridVisualizer visualizer) => DrawWalls(visualizer) || DrawRooms(visualizer) || DrawLeaves(visualizer);
    private static void BeforeVisualizer(ShipRoomGridVisualizer __instance, ShipRooms rooms)
    {
        Visualizers.Remove(__instance); Visualizers.Add(__instance, rooms);
        if (Drawing(__instance) && Mirrors.TryGetValue(rooms, out _)) { Restore(rooms); rooms.UpdateRooms(); }
    }
    private static void BeforeVisualizerUpdate(ShipRoomGridVisualizer __instance)
    {
        if (Drawing(__instance) && Visualizers.TryGetValue(__instance, out var rooms) && Mirrors.TryGetValue(rooms, out _))
        { Restore(rooms); rooms.UpdateRooms(); }
    }
    internal static void Update()
    {
        if (Retained.Count == 0) return;
        if (!InstanceFinder.IsServerStarted) { Retained.Clear(); return; } // Old clients are being destroyed on disconnect.
        if (InstanceFinder.ServerManager.Clients.Count == 1) return;
        var restored = 0;
        foreach (var reference in Retained)
            if (reference.TryGetTarget(out var rooms) && Mirrors.TryGetValue(rooms, out _)) { Restore(rooms); restored++; }
        Retained.Clear();
        if (restored != 0) Log?.Invoke("Restored " + restored + " native client room mirrors after leaving single-player.");
    }
    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var constructor = AccessTools.GetDeclaredConstructors(typeof(ShipRooms)).Find(method => !method.IsStatic) ?? throw new MissingMethodException("ShipRooms constructor");
        var names = new[] { "AddHullPart", "RemoveHullPart", "AddRelativeHullPart", "RemoveRelativeHullPart", "UpdateRooms", "SampleRoomAtPosition",
            "InitializeStatefulPart", "RemoveStatefulPart", "OnStatefulPartStateChange", "LoadRoomStates", "TryGetRoomGasAtPosition" };
        PatchSafety.RequireNoForeignOwners(constructor, Owner);
        foreach (var name in names) PatchSafety.RequireNoForeignOwners(AccessTools.Method(typeof(ShipRooms), name), Owner);
        foreach (var method in IncrementalRooms.Contracts) PatchSafety.RequireNoForeignOwners(method, Owner, IncrementalRooms.Owner);
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(typeof(Ships.Rooms.Tests.ShipRoomGridVisualizer), "Setup"), Owner);
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(typeof(ShipRoomGridVisualizer), "Update"), Owner);
        Bind();
        harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(SinglePlayerRooms), nameof(Created)));
        foreach (var name in new[] { "AddHullPart", "RemoveHullPart", "AddRelativeHullPart", "RemoveRelativeHullPart", "UpdateRooms" })
            harmony.Patch(AccessTools.Method(typeof(ShipRooms), name), prefix: new HarmonyMethod(typeof(SinglePlayerRooms), nameof(BeforeMutation)));
        harmony.Patch(AccessTools.Method(typeof(ShipRooms), "SampleRoomAtPosition"), prefix: new HarmonyMethod(typeof(SinglePlayerRooms), nameof(BeforeSample)));
        harmony.Patch(AccessTools.Method(typeof(Ships.Rooms.Tests.ShipRoomGridVisualizer), "Setup"), prefix: new HarmonyMethod(typeof(SinglePlayerRooms), nameof(BeforeVisualizer)));
        harmony.Patch(AccessTools.Method(typeof(ShipRoomGridVisualizer), "Update"), prefix: new HarmonyMethod(typeof(SinglePlayerRooms), nameof(BeforeVisualizerUpdate)));
    }
}
