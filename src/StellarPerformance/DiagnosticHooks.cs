using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using FishNet.Managing.Timing;
using FishNet.Serializing;
using HarmonyLib;
using Imports.CablesMesh;
using Managers.Client;
using Managers.Client.Services;
using Managers.Server;
using Managers.Server.Updaters;
using Managers.Server.Network;
using Managers.Server.Network.Serialization;
using Planets;
using Planets.EnvironmentGeneration;
using Planets.Lighting;
using Planets.Network;
using Planets.Rings;
using Planets.Services;
using Planets.Terrain;
using Planets.Terrain.Meshes;
using Saving.Services.IO;
using Ships;
using Ships.Cables;
using Ships.Cables.VisualObjects;
using Ships.Cables.Fluids;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Model.State;
using Ships.Physics;
using Ships.Physics.Model;
using Ships.Services;
using Steamworks;
using UnityEngine;
using WorldObjects.Interface.Model;
using WorldObjects.Services;
using WorldTracking.Spaces;

namespace StellarPerformance;

internal static partial class Diagnostics
{
    private static Func<TOwner, ICollection> BindCollection<TOwner>(string fieldName)
    {
        var owner = Expression.Parameter(typeof(TOwner), "owner");
        var field = AccessTools.Field(typeof(TOwner), fieldName) ?? throw new MissingFieldException(typeof(TOwner).FullName, fieldName);
        return Expression.Lambda<Func<TOwner, ICollection>>(Expression.Convert(Expression.Field(owner, field), typeof(ICollection)), owner).Compile();
    }

    private static Func<PlanetHotspotsEnvironmentVisuals, int> BindHotspotCounter()
    {
        var ownerType = typeof(PlanetHotspotsEnvironmentVisuals);
        var chunksField = AccessTools.Field(ownerType, "_instantiatedChunkObjects") ?? throw new MissingFieldException(ownerType.FullName, "_instantiatedChunkObjects");
        var dictField = AccessTools.Field(chunksField.FieldType, "_instantiatedChunkObjects") ?? throw new MissingFieldException(chunksField.FieldType.FullName, "_instantiatedChunkObjects");
        var nodeType = dictField.FieldType.GetGenericArguments()[1];
        var refsField = AccessTools.Field(nodeType, "ObjectRefs") ?? throw new MissingFieldException(nodeType.FullName, "ObjectRefs");
        var owner = Expression.Parameter(ownerType, "owner");
        var dict = Expression.Field(Expression.Field(owner, chunksField), dictField);
        var getDict = Expression.Lambda<Func<PlanetHotspotsEnvironmentVisuals, IDictionary>>(Expression.Convert(dict, typeof(IDictionary)), owner).Compile();
        var nodeParam = Expression.Parameter(typeof(object), "node");
        var getRefs = Expression.Lambda<Func<object, ICollection>>(Expression.Convert(Expression.Field(Expression.Convert(nodeParam, nodeType), refsField), typeof(ICollection)), nodeParam).Compile();
        return instance =>
        {
            var map = getDict(instance);
            if (map.Count == 0) return 0;
            var total = 0;
            foreach (var value in map.Values)
                if (value != null) total += getRefs(value).Count;
            return total;
        };
    }

    internal static void InstallServer(HarmonyLib.Harmony harmony)
    {
        Profile(harmony, typeof(WorldPhysicsServerUpdater), "PrePhysics", Metric.ServerTick);
        harmony.Patch(AccessTools.Method(typeof(WorldPhysicsServerUpdater), "PrePhysics"), prefix: new HarmonyMethod(typeof(Diagnostics), nameof(Population)));
        Profile(harmony, typeof(WorldPhysicsServerUpdater), "UpdateShipParts", Metric.ShipParts);
        Profile(harmony, typeof(WorldPhysicsServerUpdater), "UpdateLoadedShips", Metric.ServerShipLoading);
        Profile(harmony, typeof(WorldPhysicsServerUpdater), "UpdateShipsPhysics", Metric.ServerShipsPhysics);
        Profile(harmony, typeof(SignalConnectionsServerUpdater), nameof(SignalConnectionsServerUpdater.UpdateSignalValues), Metric.SignalEvaluation);
        Profile(harmony, typeof(FluidsServerTracker), nameof(FluidsServerTracker.PrepareFluidStates), Metric.FluidPreparation);
        Profile(harmony, typeof(ConnectionsServerTracker), nameof(ConnectionsServerTracker.CalculateEffectiveTransfers), Metric.ResourceTransfers);
        Profile(harmony, typeof(FluidsServerTracker), nameof(FluidsServerTracker.UpdateFluidsContainers), Metric.FluidBalance);
        Profile(harmony, typeof(WorldPhysicsServerUpdater), "UpdateLoadedPlanetColliders", Metric.PlanetColliders);
        Profile(harmony, typeof(PhysicsScenesServerUpdater), nameof(PhysicsScenesServerUpdater.PreparePhysicsScenes), Metric.ServerScenePrep);
        Profile(harmony, typeof(WorldPhysicsServerUpdater), "PostPhysics", Metric.ServerPostPhysics);
        Profile(harmony, typeof(MasterSyncGameObject), nameof(MasterSyncGameObject.SyncUnreliable), Metric.WorldSync);
        Profile(harmony, typeof(TimeManager), "SimulatePhysics", Metric.DefaultScenePhysics);
        harmony.Patch(AccessTools.PropertyGetter(typeof(WorldObjectsServerTracker), nameof(WorldObjectsServerTracker.WorldObjects)), postfix: new HarmonyMethod(typeof(Diagnostics), nameof(Snapshot)));
        Profile(harmony, typeof(SignalConnectionsServerUpdater), "ExecuteOperations", Metric.SignalOperations);
        if (Mode == DiagnosticMode.Full)
        {
            Profile(harmony, typeof(SignalConnectionGroupValues), nameof(SignalConnectionGroupValues.UpdateGroupSignalValue), Metric.SignalGroups);
            Profile(harmony, typeof(SignalInputProjection), nameof(SignalInputProjection.ReadInputs), Metric.SignalInputClassification);
            harmony.Patch(AccessTools.Method(typeof(ShipState), nameof(ShipState.SetPartState)), prefix: new HarmonyMethod(typeof(Diagnostics), nameof(BeforeSetPartState)) { priority = Priority.First });
            harmony.Patch(AccessTools.Method(typeof(MasterSync), "ServerQueueStatefulPartUpdate"), prefix: new HarmonyMethod(typeof(Diagnostics), nameof(BeforeServerQueuePartUpdate)) { priority = Priority.First });
            harmony.Patch(AccessTools.Method(typeof(PhysicsScenesServerUpdater), "BuildActiveSpacesCache"), prefix: new HarmonyMethod(typeof(Diagnostics), nameof(BeforeBuildActiveSpacesCache)) { priority = Priority.First });
            harmony.Patch(AccessTools.Method(typeof(WorldPhysicsServerUpdater), "UpdateShipInteriorLoading"), prefix: new HarmonyMethod(typeof(Diagnostics), nameof(BeforeServerInteriorLoading)) { priority = Priority.First });
        }
    }

    internal static void InstallCables(HarmonyLib.Harmony harmony)
    {
        Profile(harmony, typeof(ExtrudableCableMeshGenerator), nameof(ExtrudableCableMeshGenerator.GenerateCableMesh), Metric.CableMesh);
        Profile(harmony, typeof(SignalCablesVisualUpdater), nameof(SignalCablesVisualUpdater.UpdateSignalCables), Metric.SignalCables);
        harmony.Patch(AccessTools.Method(typeof(SignalCablesVisualUpdater), nameof(SignalCablesVisualUpdater.UpdateSignalCables)), prefix: new HarmonyMethod(typeof(Diagnostics), nameof(Cables)));
        Profile(harmony, typeof(FluidCablesVisualUpdater), nameof(FluidCablesVisualUpdater.UpdateFluidCables), Metric.FluidCables);
        Profile(harmony, typeof(HullMeshBuilder), nameof(HullMeshBuilder.BuildChunkMesh), Metric.HullChunk);
    }

    internal static void InstallClient(HarmonyLib.Harmony harmony)
    {
        if (Mode == DiagnosticMode.Full)
        {
            StaticSpawnedOf = BindCollection<PlanetStaticEnvironmentVisuals>("_spawnedObjects");
            HotspotCountOf = BindHotspotCounter();
        }
        Profile(harmony, typeof(ClientVisualsUpdater), "Update", Metric.ClientVisuals);
        Profile(harmony, typeof(ClientVisualsUpdater), "UpdateShipsVisuals", Metric.ShipVisuals);
        Profile(harmony, typeof(ClientVisualsUpdater), "UpdateLoadedVisualShips", Metric.VisualLoading);
        Profile(harmony, typeof(ClientWorldVisualsUpdater), "UpdateWorldVisuals", Metric.WorldVisuals);
        Profile(harmony, typeof(TrackedPlanetClient), nameof(TrackedPlanetClient.UpdateVisuals), Metric.PlanetVisuals);
        Profile(harmony, typeof(PlanetHotspotsEnvironmentVisuals), nameof(PlanetHotspotsEnvironmentVisuals.UpdateVisuals), Metric.PlanetHotspots);
        Profile(harmony, typeof(PlanetStaticEnvironmentVisuals), nameof(PlanetStaticEnvironmentVisuals.UpdateVisuals), Metric.PlanetStaticEnv);
        Profile(harmony, typeof(PlanetShaderUpdater), nameof(PlanetShaderUpdater.UpdatePlanetShaders), Metric.PlanetShaders);
        Profile(harmony, typeof(HintsActivator), nameof(HintsActivator.UpdateTips), Metric.HintsTips);
        Profile(harmony, typeof(WorldPhysicsClientUpdater), "PrePhysics", Metric.ClientPhysics);
        Profile(harmony, typeof(WorldPhysicsClientUpdater), "PreparePlanetsPhysics", Metric.ClientPlanetPhysics);
        Profile(harmony, typeof(WorldPhysicsClientUpdater), "PrepareShipsPhysics", Metric.ClientShipPhysics);
        Profile(harmony, typeof(WorldPhysicsClientUpdater), "UpdateLocalPlayer", Metric.ClientPlayerPhysics);
        Profile(harmony, typeof(PhysicsScenesServerUpdater), "SimulatePhysics", Metric.ServerPhysics);
        Profile(harmony, typeof(PlanetTreeTraversor), nameof(PlanetTreeTraversor.TraverseQuadTree), Metric.TerrainTraversal);
        Profile(harmony, typeof(PlanetMeshUpdater), nameof(PlanetMeshUpdater.UpdateTerrainMesh), Metric.TerrainMeshUpdate);
        Profile(harmony, typeof(PlanetMeshUpdater), "FindMeshesDelta", Metric.TerrainMeshDelta);
        Profile(harmony, typeof(PlanetMeshUpdater), "NeighboursCorrectionPass", Metric.TerrainNeighbors);
        Profile(harmony, typeof(PlanetDenseEnvironmentVisuals), nameof(PlanetDenseEnvironmentVisuals.UpdateVisuals), Metric.DenseEnvironment);
        Profile(harmony, typeof(RingAsteroidVisuals), nameof(RingAsteroidVisuals.UpdateVisuals), Metric.RingAsteroids);
        harmony.Patch(AccessTools.Method(typeof(WorldPhysicsClientUpdater), "PrePhysics"), transpiler: new HarmonyMethod(typeof(Diagnostics), nameof(TranspileClientSimulate)));
        if (Mode == DiagnosticMode.Full)
        {
            harmony.Patch(AccessTools.Method(typeof(WorldPhysicsClientUpdater), "UpdateShipInteriorLoading"), prefix: new HarmonyMethod(typeof(Diagnostics), nameof(BeforeClientInteriorLoading)) { priority = Priority.First });
            harmony.Patch(AccessTools.Method(typeof(ShipRigidbody), nameof(ShipRigidbody.SetPhysicsStateClient)), prefix: new HarmonyMethod(typeof(Diagnostics), nameof(BeforeSetPhysicsStateClient)) { priority = Priority.First });
            harmony.Patch(AccessTools.Method(typeof(TrackedShipClient), nameof(TrackedShipClient.UpdateParentTransform)),
                prefix: new HarmonyMethod(typeof(Diagnostics), nameof(BeforeUpdateParentTransform)) { priority = Priority.First },
                postfix: new HarmonyMethod(typeof(Diagnostics), nameof(AfterUpdateParentTransform)) { priority = Priority.Last });
            harmony.Patch(AccessTools.Method(typeof(PlanetHotspotsEnvironmentVisuals), "UpdateOrigin"), prefix: new HarmonyMethod(typeof(Diagnostics), nameof(BeforeHotspotsUpdateOrigin)) { priority = Priority.First });
            harmony.Patch(AccessTools.Method(typeof(PlanetStaticEnvironmentVisuals), "UpdateOrigin"), prefix: new HarmonyMethod(typeof(Diagnostics), nameof(BeforeStaticUpdateOrigin)) { priority = Priority.First });
            harmony.Patch(AccessTools.Method(typeof(PlanetShaderUpdater), "UpdateNearestAtmosphericPlanet"), postfix: new HarmonyMethod(typeof(Diagnostics), nameof(AfterUpdateNearestAtmosphericPlanet)) { priority = Priority.Last });
        }
    }

    internal static IEnumerable<CodeInstruction> TranspileClientSimulate(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.Select(code => new CodeInstruction(code)).ToList();
        var simulate = AccessTools.Method(typeof(PhysicsScene), nameof(PhysicsScene.Simulate), new[] { typeof(float) });
        var matches = codes.Where(code => code.Calls(simulate)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Client physics simulate call changed.");
        matches[0].opcode = OpCodes.Call;
        matches[0].operand = AccessTools.Method(typeof(Diagnostics), nameof(SimulateClientScene));
        return codes;
    }

    internal static void SimulateClientScene(ref PhysicsScene scene, float deltaTime)
    {
        var capture = _capture;
        if (capture == null) { scene.Simulate(deltaTime); return; }
        var started = Stopwatch.GetTimestamp();
        var failed = true;
        try { scene.Simulate(deltaTime); failed = false; }
        finally { capture.Timing(Metric.ClientSceneSimulate, Milliseconds(Stopwatch.GetTimestamp() - started), failed); }
    }

    internal static void InstallItemRouting(HarmonyLib.Harmony harmony) => Profile(harmony, Assembly.Load("ItemLines").GetType("ItemLines.ItemNetworks", true)!, "Run", Metric.ItemRouting);
    internal static void InstallRendering(HarmonyLib.Harmony harmony) => Profile(harmony, typeof(UnityEngine.Rendering.RenderPipelineManager), "DoRenderLoop_Internal", Metric.RenderSubmission);

    internal static void InstallModUpdates(HarmonyLib.Harmony harmony)
    {
        var assembly = Assembly.Load("SDModKit");
        Profile(harmony, assembly.GetType("SDModKit.Plugin", true)!, "OnUpdate", Metric.ModUpdates);
        var sdModType = assembly.GetType("SDModKit.Api.SDMod", true)!;
        var idGetter = AccessTools.PropertyGetter(sdModType, "Id") ?? throw new MissingMethodException("SDMod.get_Id");
        var modParam = Expression.Parameter(typeof(object), "mod");
        _sdModId = Expression.Lambda<Func<object, string>>(Expression.Call(Expression.Convert(modParam, sdModType), idGetter), modParam).Compile();
        var modUpdateTarget = ResolveSdModUpdateDispatcher(assembly, sdModType);
        harmony.Patch(modUpdateTarget, prefix: new HarmonyMethod(typeof(Diagnostics), nameof(BeginMod)) { priority = Priority.First }, finalizer: new HarmonyMethod(typeof(Diagnostics), nameof(EndMod)) { priority = Priority.Last });
        if (Mode != DiagnosticMode.Full) return;
        var toolRefsType = assembly.GetType("SDModKit.Game.ToolRefs", true)!;
        var nextBuildField = AccessTools.Field(toolRefsType, "_nextBuildSearch") ?? throw new MissingFieldException("ToolRefs._nextBuildSearch");
        var nextRecyclerField = AccessTools.Field(toolRefsType, "_nextRecyclerSearch") ?? throw new MissingFieldException("ToolRefs._nextRecyclerSearch");
        _nextBuildSearch = Expression.Lambda<Func<float>>(Expression.Field(null, nextBuildField)).Compile();
        _nextRecyclerSearch = Expression.Lambda<Func<float>>(Expression.Field(null, nextRecyclerField)).Compile();
        harmony.Patch(AccessTools.PropertyGetter(toolRefsType, "Build") ?? throw new MissingMethodException("ToolRefs.get_Build"),
            prefix: new HarmonyMethod(typeof(Diagnostics), nameof(BeforeToolRefsBuild)) { priority = Priority.First },
            postfix: new HarmonyMethod(typeof(Diagnostics), nameof(AfterToolRefsBuild)) { priority = Priority.Last });
        harmony.Patch(AccessTools.PropertyGetter(toolRefsType, "Recycler") ?? throw new MissingMethodException("ToolRefs.get_Recycler"),
            prefix: new HarmonyMethod(typeof(Diagnostics), nameof(BeforeToolRefsRecycler)) { priority = Priority.First },
            postfix: new HarmonyMethod(typeof(Diagnostics), nameof(AfterToolRefsRecycler)) { priority = Priority.Last });
    }

    internal static MethodInfo ResolveSdModUpdateDispatcher(Assembly sdModKit, Type sdModType)
    {
        var modKitType = sdModKit.GetType("SDModKit.ModKit", true)!;
        var onUpdate = AccessTools.Method(sdModType, "OnUpdate", Type.EmptyTypes) ?? throw new MissingMethodException("SDMod.OnUpdate");
        var candidates = modKitType.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
            .SelectMany(nested => nested.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            .Where(method =>
            {
                var parameters = method.GetParameters();
                if (parameters.Length != 1 || parameters[0].ParameterType != sdModType || method.ReturnType != typeof(void)) return false;
                var body = PatchProcessor.GetOriginalInstructions(method);
                return body.Count(code => (code.opcode == OpCodes.Callvirt || code.opcode == OpCodes.Call) && Equals(code.operand, onUpdate)) == 1;
            })
            .ToArray();
        if (candidates.Length != 1) throw new InvalidOperationException("SDModKit per-mod OnUpdate dispatcher changed.");
        return candidates[0];
    }

    internal static void InstallSaves(HarmonyLib.Harmony harmony) => Profile(harmony, typeof(SaveFileWriter), nameof(SaveFileWriter.WriteGameFile), Metric.NativeSave);

    internal static void InstallTransport(HarmonyLib.Harmony harmony) => harmony.Patch(AccessTools.Method(typeof(FishySteamworks.CommonSocket), "Send"), prefix: new HarmonyMethod(typeof(Diagnostics), nameof(BeginSend)) { priority = Priority.First }, finalizer: new HarmonyMethod(typeof(Diagnostics), nameof(EndSend)) { priority = Priority.Last });

    private static void Profile(HarmonyLib.Harmony harmony, Type type, string name, Metric metric)
    {
        var method = AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name);
        Targets[method] = metric;
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(Diagnostics), nameof(Begin)) { priority = Priority.First }, finalizer: new HarmonyMethod(typeof(Diagnostics), nameof(End)) { priority = Priority.Last });
    }

    private static void Begin(MethodBase __originalMethod, out Stamp __state)
    {
        var capture = _capture;
        __state = capture == null ? default : new Stamp(capture, Targets[__originalMethod]);
    }

    private static Exception? End(Exception? __exception, Stamp __state)
    {
        __state.Capture?.Timing(__state.Metric, Milliseconds(Stopwatch.GetTimestamp() - __state.Started), __exception != null);
        return __exception;
    }

    private static void BeginMod(object __0, out ModStamp __state)
    {
        var capture = _capture;
        __state = capture == null || __0 == null || _sdModId == null ? default : new ModStamp(capture, _sdModId(__0));
    }

    private static Exception? EndMod(Exception? __exception, ModStamp __state)
    {
        if (__state.Capture != null && __state.ModId != null)
            __state.Capture.ModTiming(__state.ModId, Milliseconds(Stopwatch.GetTimestamp() - __state.Started), __exception != null);
        return __exception;
    }

    private static void BeforeToolRefsBuild(out ToolScanStamp __state)
    {
        var capture = _capture;
        __state = capture == null || _nextBuildSearch == null ? default : new ToolScanStamp(capture, _nextBuildSearch());
    }

    private static void AfterToolRefsBuild(ToolScanStamp __state)
    {
        if (__state.Capture != null && _nextBuildSearch != null)
            __state.Capture.RecordToolRefs(isRecycler: false, scanned: _nextBuildSearch() != __state.NextSearchBefore);
    }

    private static void BeforeToolRefsRecycler(out ToolScanStamp __state)
    {
        var capture = _capture;
        __state = capture == null || _nextRecyclerSearch == null ? default : new ToolScanStamp(capture, _nextRecyclerSearch());
    }

    private static void AfterToolRefsRecycler(ToolScanStamp __state)
    {
        if (__state.Capture != null && _nextRecyclerSearch != null)
            __state.Capture.RecordToolRefs(isRecycler: true, scanned: _nextRecyclerSearch() != __state.NextSearchBefore);
    }

    internal static void BeforeSetPartState(ushort id, IStatefulPartState state, Dictionary<ushort, StatefulPart> ____statefulParts)
    {
        var capture = _capture;
        if (capture == null || state == null || ____statefulParts == null) return;
        var unchanged = ____statefulParts.TryGetValue(id, out var part) && part?.State != null && part.State.Equals(state);
        capture.RecordStateUpdate(state.GetType().Name, unchanged);
    }

    internal static void BeforeServerQueuePartUpdate(StatefulPartStateChangeEvent partState)
    {
        var capture = _capture;
        if (capture == null || partState.PreviousState == null || partState.NewState == null) return;
        if (partState.PreviousState.Equals(partState.NewState)) return;
        var identicalBytes = AreSerializedStatesEqual(partState.PreviousState, partState.NewState);
        capture.RecordNetworkQueue(partState.NewState.GetType().Name, identicalBytes);
    }

    internal static bool AreSerializedStatesEqual(IStatefulPartState previousState, IStatefulPartState newState)
    {
        if (ReferenceEquals(previousState, newState)) return true;
        if (previousState == null || newState == null || previousState.GetType() != newState.GetType()) return false;
        if (!_writersInitialized && GenericWriter<float>.Write == null)
        {
            foreach (var asm in new[] { typeof(Writer).Assembly, typeof(StatefulPartSerialization).Assembly })
            {
                var gen = asm.GetType("FishNet.Serializing.Generated.GeneratedWriters___Internal");
                AccessTools.Method(gen, "InitializeOnce")?.Invoke(null, null);
            }
            _writersInitialized = true;
        }
        var writerA = _writerA ?? (_writerA = new Writer());
        var writerB = _writerB ?? (_writerB = new Writer());
        writerA.Clear();
        writerB.Clear();
        StatefulPartSerialization.WriteState(writerA, previousState);
        StatefulPartSerialization.WriteState(writerB, newState);
        if (writerA.Length != writerB.Length) return false;
        var bufA = writerA.GetBuffer();
        var bufB = writerB.GetBuffer();
        for (var i = 0; i < writerA.Length; i++)
            if (bufA[i] != bufB[i]) return false;
        return true;
    }

    private static void BeforeBuildActiveSpacesCache(PlanetCollidersServer ____planetColliders, SpacesServer ____spaces)
    {
        var capture = _capture;
        if (capture == null || ____planetColliders == null || ____spaces == null) return;
        foreach (var collider in ____planetColliders.Colliders)
        {
            var spawned = collider.SpawnedObject;
            var targetSpace = ____spaces.GetSpace(collider.CalculatePreferredSpaceId());
            capture.RecordTransformWrite(TransformTarget.TerrainCollider, ReferenceEquals(spawned.Space, targetSpace));
        }
    }

    private static void BeforeServerInteriorLoading(TrackedShipServer ship, ActiveSpacesServerTracker ____activeSpaces)
    {
        var capture = _capture;
        if (capture == null || ship == null || ____activeSpaces == null) return;
        if (!____activeSpaces.IsSpaceActive(ship.RelativeSpaceId) || !ship.IsInteriorColliderObjectCreated) return;
        var transform = ship.InteriorColliders.transform;
        capture.RecordTransformWrite(TransformTarget.ServerInteriorCollider, ExactEquals(transform.position, Vector3.zero) && ExactEquals(transform.rotation, Quaternion.identity));
    }

    private static void BeforeClientInteriorLoading(uint localSpaceId, TrackedShipClient ship)
    {
        var capture = _capture;
        if (capture == null || ship == null || localSpaceId != ship.RelativeSpaceId || !ship.IsInteriorColliderObjectCreated) return;
        var transform = ship.InteriorColliders.transform;
        capture.RecordTransformWrite(TransformTarget.ClientInteriorCollider, ExactEquals(transform.position, Vector3.zero) && ExactEquals(transform.rotation, Quaternion.identity));
    }

    private static void BeforeSetPhysicsStateClient(ShipRigidbody __instance, ShipPhysicsCalculationProperties props)
    {
        var capture = _capture;
        if (capture == null || __instance == null) return;
        var transform = __instance.transform;
        var state = props.PhysicsState;
        capture.RecordTransformWrite(TransformTarget.ClientShipCollider, ExactEquals(transform.position, state.Position) && ExactEquals(transform.rotation, state.Rotation));
    }

    private static void BeforeUpdateParentTransform(TrackedShipClient __instance, out PoseStamp __state)
    {
        var capture = _capture;
        if (capture == null || __instance == null) { __state = default; return; }
        var transform = __instance.VisualObject.transform;
        __state = new PoseStamp(capture, transform.position, transform.rotation);
    }

    private static void AfterUpdateParentTransform(TrackedShipClient __instance, PoseStamp __state)
    {
        if (__state.Capture == null || __instance == null) return;
        var transform = __instance.VisualObject.transform;
        __state.Capture.RecordTransformWrite(TransformTarget.ClientShipVisualRoot, ExactEquals(transform.position, __state.Position) && ExactEquals(transform.rotation, __state.Rotation));
    }

    private static void BeforeHotspotsUpdateOrigin(PlanetHotspotsEnvironmentVisuals __instance)
    {
        var capture = _capture;
        if (capture != null && __instance != null) capture.RecordHotspotObjects(HotspotCountOf(__instance));
    }

    private static void BeforeStaticUpdateOrigin(PlanetStaticEnvironmentVisuals __instance)
    {
        var capture = _capture;
        if (capture != null && __instance != null) capture.RecordStaticObjects(StaticSpawnedOf(__instance).Count);
    }

    private static void AfterUpdateNearestAtmosphericPlanet(Planet? ____nearestAtmosphericPlanet)
    {
        var capture = _capture;
        if (capture == null) return;
        var hasPlanet = ____nearestAtmosphericPlanet != null;
        var hasRing = hasPlanet && ____nearestAtmosphericPlanet!.GetComponent<PlanetRing>() != null;
        capture.RecordAtmosphericPlanet(hasPlanet, hasRing);
    }

    internal static bool ExactEquals(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
    internal static bool ExactEquals(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

    private static void Population(ShipsServerTracker ____ships)
    {
        var capture = _capture;
        if (capture == null) return;
        var now = Stopwatch.GetTimestamp();
        if (now - _lastPopulation < Stopwatch.Frequency) return;
        _lastPopulation = now;
        var ships = 0;
        var parts = 0;
        foreach (var ship in ____ships.Ships) { ships++; parts += ship.PartCount; }
        capture.Population(ships, parts);
    }

    private static void Cables(IReadOnlyDictionary<uint, CableVisual> loadedCables) => _capture?.Cables(loadedCables.Count);
    private static void Snapshot(WorldObjectStateSnapshot[] __result) => _capture?.Snapshot(__result.Length);
    private static void BeginSend(ArraySegment<byte> segment, byte channelId, out SendStamp __state)
    {
        var capture = _capture;
        __state = capture == null ? default : new SendStamp(capture, segment, channelId);
    }

    private static Exception? EndSend(EResult __result, Exception? __exception, SendStamp __state)
    {
        __state.Capture?.Send(__state.Channel, __state.Count, __state.Offset, __state.Backing, (int)__result, __exception != null);
        return __exception;
    }

}
