using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Managers.Client;
using Managers.Server;
using Managers.Server.Services;
using Managers.Server.Updaters;
using Planets.EnvironmentGeneration.Tracking;
using Planets.Services;
using Players.Services;
using Ships;
using Ships.Physics;
using Ships.Physics.Model;
using Ships.Services;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorldObjects.Services;
using WorldTracking.Interface.Model;
using WorldTracking.Interface.Model.Spaces;
using WorldTracking.Interface.Model.Spaces.Common;
using WorldTracking.Interface.Services;
using WorldTracking.Services;
using WorldTracking.Spaces;

namespace StellarPerformance;

internal static class PhysicsSceneCache
{
    static PhysicsSceneCache() { }

    internal readonly struct ObjectEntry
    {
        internal readonly GameObject PhysicsObject;
        internal readonly Scene CurrentScene;
        internal readonly float Weight;

        internal ObjectEntry(GameObject physicsObject, Scene currentScene, float weight)
        {
            PhysicsObject = physicsObject;
            CurrentScene = currentScene;
            Weight = weight;
        }
    }

    internal readonly struct SpaceOrderEntry
    {
        internal readonly uint SpaceId;
        internal readonly int OrderIndex;
        internal readonly float PreferredScore;
        internal readonly object CommonSpace;
        internal readonly List<ObjectEntry> Entries;

        internal SpaceOrderEntry(uint spaceId, int orderIndex, float preferredScore, object commonSpace, List<ObjectEntry> entries)
        {
            SpaceId = spaceId;
            OrderIndex = orderIndex;
            PreferredScore = preferredScore;
            CommonSpace = commonSpace;
            Entries = entries;
        }
    }

    internal sealed class ServerSceneScratch
    {
        internal readonly Dictionary<uint, object> SpacePool = new Dictionary<uint, object>();
        internal readonly Dictionary<uint, List<ObjectEntry>> EntriesBySpace = new Dictionary<uint, List<ObjectEntry>>();
        internal readonly HashSet<uint> ActiveSpaceIds = new HashSet<uint>();
        private readonly List<uint> _inactiveSpaceIds = new List<uint>();

        internal void PruneInactive()
        {
            _inactiveSpaceIds.Clear();
            foreach (var id in SpacePool.Keys) if (!ActiveSpaceIds.Contains(id)) _inactiveSpaceIds.Add(id);
            foreach (var id in _inactiveSpaceIds)
            {
                SpacePool.Remove(id);
                if (EntriesBySpace.TryGetValue(id, out var entries)) entries.Clear();
                EntriesBySpace.Remove(id);
            }
            _inactiveSpaceIds.Clear();
        }

        internal readonly HashSet<Scene> AvailableSceneSet = new HashSet<Scene>();
        internal readonly HashSet<Scene> NamedScenes = new HashSet<Scene>();
        internal readonly List<SpaceOrderEntry> OrderedSpaces = new List<SpaceOrderEntry>();
        internal readonly List<object> UnassignedScenes = new List<object>();
    }

    private static readonly Type CommonSpaceType = typeof(PhysicsScenesServerUpdater).Assembly.GetType("Managers.Server.Updaters.ObjectsInCommonSpace", true)!;
    private static readonly Type TrackedSceneType = typeof(PhysicsScenesServerUpdater).Assembly.GetType("Managers.Server.Updaters.TrackedScene", true)!;
    private static readonly Type ObjectPrefType = typeof(PhysicsScenesServerUpdater).Assembly.GetType("Managers.Server.Updaters.ObjectScenePreference", true)!;
    private static readonly Type SceneScoreType = typeof(PhysicsScenesServerUpdater).Assembly.GetType("Managers.Server.Updaters.SceneScore", true)!;

    private static readonly Func<PhysicsScenesServerUpdater, IList> PhysicsScenesOf = BindAs<PhysicsScenesServerUpdater, IList>("_physicsScenes");
    private static readonly Func<PhysicsScenesServerUpdater, IDictionary> ActiveSpacesPrefsOf = BindAs<PhysicsScenesServerUpdater, IDictionary>("_activeSpacesPreferences");
    private static readonly Func<PhysicsScenesServerUpdater, SpacesServer> SpacesOf = NativeAccess.Read<PhysicsScenesServerUpdater, SpacesServer>("_spaces");
    private static readonly Func<PhysicsScenesServerUpdater, ActiveSpacesServerTracker> ActiveSpacesOf = NativeAccess.Read<PhysicsScenesServerUpdater, ActiveSpacesServerTracker>("_activeSpaces");
    private static readonly Func<PhysicsScenesServerUpdater, PlanetCollidersServer> PlanetCollidersOf = NativeAccess.Read<PhysicsScenesServerUpdater, PlanetCollidersServer>("_planetColliders");
    private static readonly Func<PhysicsScenesServerUpdater, ShipsServerTracker> ShipsOf = NativeAccess.Read<PhysicsScenesServerUpdater, ShipsServerTracker>("_ships");
    private static readonly Func<PhysicsScenesServerUpdater, IWorldTransposeServer> TransposeOf = NativeAccess.Read<PhysicsScenesServerUpdater, IWorldTransposeServer>("_transpose");
    private static readonly Func<PhysicsScenesServerUpdater, PlayersServerTracker> PlayersOf = NativeAccess.Read<PhysicsScenesServerUpdater, PlayersServerTracker>("_players");
    private static readonly Func<PhysicsScenesServerUpdater, WorldObjectsServerTracker> WorldObjectsOf = NativeAccess.Read<PhysicsScenesServerUpdater, WorldObjectsServerTracker>("_worldObjects");
    private static readonly CreateSceneParameters SceneParams = (CreateSceneParameters)AccessTools.Field(typeof(PhysicsScenesServerUpdater), "SceneParams").GetValue(null)!;

    private static readonly Func<uint, object> CreateCommonSpace = BindCreateCommonSpace();
    private static readonly Action<object> ClearCommonSpace = BindClearCommonSpace();
    private static readonly Action<object, GameObject, float> AddObjectPreference = BindAddObjectPreference();
    private static readonly Func<object, Dictionary<Scene, float>> ScoresDictOf = BindCommonSpaceScores();
    private static readonly Action<object, Scene, float> AddTopSceneScore = BindAddTopSceneScore();
    private static readonly Action<object> SortTopScenes = BindSortTopScenes();
    private static readonly Func<object, float> PreferredSceneScoreOf = BindPreferredSceneScore();
    private static readonly Func<object, List<object>, object?> FindPreferredTrackedScene = BindFindPreferredTrackedScene();

    private static readonly Func<Scene, object> CreateTrackedScene = BindCreateTrackedScene();
    private static readonly Func<object, Scene> TrackedSceneOf = BindTrackedSceneGetter();
    private static readonly Func<object, uint> TrackedSpaceIdOf = BindTrackedSpaceIdGetter();
    private static readonly Action<object, uint> SetTrackedCurrentSpace = BindSetTrackedCurrentSpace();

    private static readonly ConditionalWeakTable<PhysicsScenesServerUpdater, ServerSceneScratch> ScratchByUpdater = new ConditionalWeakTable<PhysicsScenesServerUpdater, ServerSceneScratch>();

    private static Func<TOwner, TAs> BindAs<TOwner, TAs>(string name)
    {
        var owner = Expression.Parameter(typeof(TOwner), "owner");
        var field = AccessTools.Field(typeof(TOwner), name) ?? throw new MissingFieldException(typeof(TOwner).FullName, name);
        return Expression.Lambda<Func<TOwner, TAs>>(Expression.Convert(Expression.Field(owner, field), typeof(TAs)), owner).Compile();
    }

    private static Func<uint, object> BindCreateCommonSpace()
    {
        var ctor = CommonSpaceType.GetConstructor(new[] { typeof(uint) }) ?? throw new MissingMethodException("ObjectsInCommonSpace..ctor(uint)");
        var spaceId = Expression.Parameter(typeof(uint), "spaceId");
        return Expression.Lambda<Func<uint, object>>(Expression.Convert(Expression.New(ctor, spaceId), typeof(object)), spaceId).Compile();
    }

    private static Action<object> BindClearCommonSpace()
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var typed = Expression.Convert(instance, CommonSpaceType);
        var prefsField = AccessTools.Field(CommonSpaceType, "ObjectsPreference") ?? throw new MissingFieldException("ObjectsInCommonSpace.ObjectsPreference");
        var scoresField = AccessTools.Field(CommonSpaceType, "_scenesScores") ?? throw new MissingFieldException("ObjectsInCommonSpace._scenesScores");
        var topField = AccessTools.Field(CommonSpaceType, "_topScenes") ?? throw new MissingFieldException("ObjectsInCommonSpace._topScenes");
        var body = Expression.Block(
            Expression.Call(Expression.Field(typed, prefsField), AccessTools.Method(prefsField.FieldType, "Clear")),
            Expression.Call(Expression.Field(typed, scoresField), AccessTools.Method(scoresField.FieldType, "Clear")),
            Expression.Call(Expression.Field(typed, topField), AccessTools.Method(topField.FieldType, "Clear")));
        return Expression.Lambda<Action<object>>(body, instance).Compile();
    }

    private static Action<object, GameObject, float> BindAddObjectPreference()
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var go = Expression.Parameter(typeof(GameObject), "go");
        var weight = Expression.Parameter(typeof(float), "weight");
        var prefsField = AccessTools.Field(CommonSpaceType, "ObjectsPreference") ?? throw new MissingFieldException("ObjectsInCommonSpace.ObjectsPreference");
        var objField = AccessTools.Field(ObjectPrefType, "PhysicsObject") ?? throw new MissingFieldException("ObjectScenePreference.PhysicsObject");
        var weightField = AccessTools.Field(ObjectPrefType, "PreferenceWeight") ?? throw new MissingFieldException("ObjectScenePreference.PreferenceWeight");
        var prefVar = Expression.Variable(ObjectPrefType, "pref");
        var body = Expression.Block(
            new[] { prefVar },
            Expression.Assign(prefVar, Expression.Default(ObjectPrefType)),
            Expression.Assign(Expression.Field(prefVar, objField), go),
            Expression.Assign(Expression.Field(prefVar, weightField), weight),
            Expression.Call(Expression.Field(Expression.Convert(instance, CommonSpaceType), prefsField), AccessTools.Method(prefsField.FieldType, "Add", new[] { ObjectPrefType }), prefVar));
        return Expression.Lambda<Action<object, GameObject, float>>(body, instance, go, weight).Compile();
    }

    private static Func<object, Dictionary<Scene, float>> BindCommonSpaceScores()
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var scoresField = AccessTools.Field(CommonSpaceType, "_scenesScores") ?? throw new MissingFieldException("ObjectsInCommonSpace._scenesScores");
        return Expression.Lambda<Func<object, Dictionary<Scene, float>>>(Expression.Field(Expression.Convert(instance, CommonSpaceType), scoresField), instance).Compile();
    }

    private static Action<object, Scene, float> BindAddTopSceneScore()
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var scene = Expression.Parameter(typeof(Scene), "scene");
        var score = Expression.Parameter(typeof(float), "score");
        var topField = AccessTools.Field(CommonSpaceType, "_topScenes") ?? throw new MissingFieldException("ObjectsInCommonSpace._topScenes");
        var ctor = SceneScoreType.GetConstructor(new[] { typeof(Scene), typeof(float) }) ?? throw new MissingMethodException("SceneScore..ctor");
        var addMethod = AccessTools.Method(topField.FieldType, "Add", new[] { SceneScoreType }) ?? throw new MissingMethodException("List<SceneScore>.Add");
        var body = Expression.Call(Expression.Field(Expression.Convert(instance, CommonSpaceType), topField), addMethod, Expression.New(ctor, scene, score));
        return Expression.Lambda<Action<object, Scene, float>>(body, instance, scene, score).Compile();
    }

    private static Action<object> BindSortTopScenes()
    {
        var topField = AccessTools.Field(CommonSpaceType, "_topScenes") ?? throw new MissingFieldException("ObjectsInCommonSpace._topScenes");
        var scoreField = AccessTools.Field(SceneScoreType, "Score") ?? throw new MissingFieldException("SceneScore.Score");
        var comparisonType = typeof(Comparison<>).MakeGenericType(SceneScoreType);
        var a = Expression.Parameter(SceneScoreType, "a");
        var b = Expression.Parameter(SceneScoreType, "b");
        var compareTo = AccessTools.Method(typeof(float), nameof(float.CompareTo), new[] { typeof(float) })!;
        var comparison = Expression.Lambda(comparisonType, Expression.Call(Expression.Field(b, scoreField), compareTo, Expression.Field(a, scoreField)), a, b).Compile();
        var sortMethod = AccessTools.Method(topField.FieldType, "Sort", new[] { comparisonType }) ?? throw new MissingMethodException("List<SceneScore>.Sort");
        var instance = Expression.Parameter(typeof(object), "instance");
        return Expression.Lambda<Action<object>>(Expression.Call(Expression.Field(Expression.Convert(instance, CommonSpaceType), topField), sortMethod, Expression.Constant(comparison, comparisonType)), instance).Compile();
    }

    private static Func<object, float> BindPreferredSceneScore()
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var getter = AccessTools.PropertyGetter(CommonSpaceType, "PreferredSceneScore") ?? throw new MissingMethodException("ObjectsInCommonSpace.get_PreferredSceneScore");
        return Expression.Lambda<Func<object, float>>(Expression.Call(Expression.Convert(instance, CommonSpaceType), getter), instance).Compile();
    }

    private static Func<object, List<object>, object?> BindFindPreferredTrackedScene()
    {
        var topField = AccessTools.Field(CommonSpaceType, "_topScenes") ?? throw new MissingFieldException("ObjectsInCommonSpace._topScenes");
        var scoreSceneField = AccessTools.Field(SceneScoreType, "Scene") ?? throw new MissingFieldException("SceneScore.Scene");
        var instance = Expression.Parameter(typeof(object), "instance");
        var topList = Expression.Field(Expression.Convert(instance, CommonSpaceType), topField);
        var getTopList = Expression.Lambda<Func<object, IList>>(Expression.Convert(topList, typeof(IList)), instance).Compile();
        var scoreObj = Expression.Parameter(typeof(object), "scoreObj");
        var getScoreScene = Expression.Lambda<Func<object, Scene>>(Expression.Field(Expression.Unbox(scoreObj, SceneScoreType), scoreSceneField), scoreObj).Compile();
        return (commonSpace, unassigned) =>
        {
            var top = getTopList(commonSpace);
            for (var i = 0; i < top.Count; i++)
            {
                var targetScene = getScoreScene(top[i]!);
                for (var j = 0; j < unassigned.Count; j++)
                {
                    var candidate = unassigned[j];
                    if (TrackedSceneOf(candidate) == targetScene) return candidate;
                }
            }
            return null;
        };
    }

    private static Func<Scene, object> BindCreateTrackedScene()
    {
        var ctor = TrackedSceneType.GetConstructor(new[] { typeof(Scene) }) ?? throw new MissingMethodException("TrackedScene..ctor(Scene)");
        var scene = Expression.Parameter(typeof(Scene), "scene");
        return Expression.Lambda<Func<Scene, object>>(Expression.Convert(Expression.New(ctor, scene), typeof(object)), scene).Compile();
    }

    private static Func<object, Scene> BindTrackedSceneGetter()
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var getter = AccessTools.PropertyGetter(TrackedSceneType, "Scene") ?? throw new MissingMethodException("TrackedScene.get_Scene");
        return Expression.Lambda<Func<object, Scene>>(Expression.Call(Expression.Convert(instance, TrackedSceneType), getter), instance).Compile();
    }

    private static Func<object, uint> BindTrackedSpaceIdGetter()
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var field = AccessTools.Field(TrackedSceneType, "CurrentSpaceId") ?? throw new MissingFieldException("TrackedScene.CurrentSpaceId");
        return Expression.Lambda<Func<object, uint>>(Expression.Field(Expression.Convert(instance, TrackedSceneType), field), instance).Compile();
    }

    private static Action<object, uint> BindSetTrackedCurrentSpace()
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var spaceId = Expression.Parameter(typeof(uint), "spaceId");
        var method = AccessTools.Method(TrackedSceneType, "SetCurrentSpace", new[] { typeof(uint) }) ?? throw new MissingMethodException("TrackedScene.SetCurrentSpace");
        return Expression.Lambda<Action<object, uint>>(Expression.Call(Expression.Convert(instance, TrackedSceneType), method, spaceId), instance, spaceId).Compile();
    }

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        foreach (var target in new[] { AccessTools.Method(typeof(PhysicsScenesServerUpdater), "BuildActiveSpacesCache"), AccessTools.Method(typeof(PhysicsScenesServerUpdater), "AssignObjectsToScenes") })
            PatchSafety.RequireOriginalBody(target, harmony.Id);
        var buildCache = AccessTools.Method(typeof(PhysicsScenesServerUpdater), "BuildActiveSpacesCache", Type.EmptyTypes) ?? throw new MissingMethodException("PhysicsScenesServerUpdater.BuildActiveSpacesCache");
        var assignScenes = AccessTools.Method(typeof(PhysicsScenesServerUpdater), "AssignObjectsToScenes", Type.EmptyTypes) ?? throw new MissingMethodException("PhysicsScenesServerUpdater.AssignObjectsToScenes");
        harmony.Patch(buildCache, prefix: new HarmonyMethod(typeof(PhysicsSceneCache), nameof(BeforeBuildActiveSpacesCache)));
        harmony.Patch(assignScenes, prefix: new HarmonyMethod(typeof(PhysicsSceneCache), nameof(BeforeAssignObjectsToScenes)));
    }

    private static void RecordSpaceObject(ServerSceneScratch scratch, IDictionary activeSpacesPrefs, uint spaceId, GameObject physicsObject, float weight)
    {
        var commonSpace = activeSpacesPrefs[spaceId]!;
        AddObjectPreference(commonSpace, physicsObject, weight);
        var entries = scratch.EntriesBySpace[spaceId];
        entries.Add(new ObjectEntry(physicsObject, physicsObject.scene, weight));
    }

    internal static bool BeforeBuildActiveSpacesCache(PhysicsScenesServerUpdater __instance)
    {
        var scratch = ScratchByUpdater.GetValue(__instance, _ => new ServerSceneScratch());
        var activeSpacesPrefs = ActiveSpacesPrefsOf(__instance);
        var activeSpaces = ActiveSpacesOf(__instance);
        var players = PlayersOf(__instance);
        var ships = ShipsOf(__instance);
        var spaces = SpacesOf(__instance);
        var worldObjects = WorldObjectsOf(__instance);
        var planetColliders = PlanetCollidersOf(__instance);
        var transpose = TransposeOf(__instance);
        var physicsScenes = PhysicsScenesOf(__instance);

        activeSpacesPrefs.Clear();
        scratch.OrderedSpaces.Clear();

        scratch.ActiveSpaceIds.Clear();
        foreach (var activeSpaceId in activeSpaces.GetActiveSpacesIds())
        {
            scratch.ActiveSpaceIds.Add(activeSpaceId);
            if (!scratch.SpacePool.TryGetValue(activeSpaceId, out var commonSpace))
                scratch.SpacePool[activeSpaceId] = commonSpace = CreateCommonSpace(activeSpaceId);
            else
                ClearCommonSpace(commonSpace);

            if (!scratch.EntriesBySpace.TryGetValue(activeSpaceId, out var entries))
                scratch.EntriesBySpace[activeSpaceId] = entries = new List<ObjectEntry>(16);
            else
                entries.Clear();

            activeSpacesPrefs.Add(activeSpaceId, commonSpace);
        }

        scratch.PruneInactive();

        foreach (var activePlayer in players.ActivePlayers)
        {
            var go = activePlayer.ServerRigidbody.gameObject;
            RecordSpaceObject(scratch, activeSpacesPrefs, activePlayer.SpaceId, go, 1f);
        }

        foreach (var ship in ships.Ships)
        {
            var space = spaces.GetSpace(ship.SpaceId);
            if (ship.IsRigidbodyLoaded && !(space is ShipRelativeSpace))
                RecordSpaceObject(scratch, activeSpacesPrefs, ship.SpaceId, ship.RigidbodyObject, ship.PartCount);
            if (ship.IsInteriorColliderObjectCreated)
            {
                var relativeSpaceId = ship.RelativeSpaceId;
                if (activeSpacesPrefs.Contains(relativeSpaceId))
                    RecordSpaceObject(scratch, activeSpacesPrefs, relativeSpaceId, ship.InteriorColliders, ship.PartCount);
                else
                    Debug.Log($"Could not add object {ship.Id} to space {relativeSpaceId}");
            }
        }

        foreach (var worldObject in worldObjects.Objects)
        {
            if (worldObject.IsRigidbodyLoaded)
                RecordSpaceObject(scratch, activeSpacesPrefs, worldObject.SpaceId, worldObject.LoadedRigidbody.gameObject, 1f);
        }

        foreach (var collider in planetColliders.Colliders)
        {
            var spawnedObject = collider.SpawnedObject;
            var preferredSpaceId = collider.CalculatePreferredSpaceId();
            var targetSpace = spaces.GetSpace(preferredSpaceId);
            if (PhysicsSceneTransforms.ShouldTransposeCollider(spawnedObject.Space, targetSpace))
            {
                var position = transpose.Position(spawnedObject.Space, targetSpace, spawnedObject.Position);
                spawnedObject.Position = position;
                spawnedObject.Space = targetSpace;
            }
            spawnedObject.SpaceId = preferredSpaceId;
            RecordSpaceObject(scratch, activeSpacesPrefs, preferredSpaceId, spawnedObject.gameObject, 2f);
        }

        scratch.AvailableSceneSet.Clear();
        for (var i = 0; i < physicsScenes.Count; i++)
            scratch.AvailableSceneSet.Add(TrackedSceneOf(physicsScenes[i]!));

        var orderIndex = 0;
        foreach (DictionaryEntry pair in activeSpacesPrefs)
        {
            var spaceId = (uint)pair.Key;
            var commonSpace = pair.Value!;
            var entries = scratch.EntriesBySpace[spaceId];
            var scores = ScoresDictOf(commonSpace);
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (!scratch.AvailableSceneSet.Contains(entry.CurrentScene)) continue;
                if (scores.TryGetValue(entry.CurrentScene, out var existing))
                    scores[entry.CurrentScene] = existing + entry.Weight;
                else
                    scores[entry.CurrentScene] = entry.Weight;
            }
            foreach (var scorePair in scores)
                AddTopSceneScore(commonSpace, scorePair.Key, scorePair.Value);
            if (scores.Count > 1)
                SortTopScenes(commonSpace);
            scratch.OrderedSpaces.Add(new SpaceOrderEntry(spaceId, orderIndex++, PreferredSceneScoreOf(commonSpace), commonSpace, entries));
        }

        return false;
    }

    internal static void SortSpacesDescendingStable(List<SpaceOrderEntry> spaces)
    {
        spaces.Sort(static (a, b) =>
        {
            var cmp = b.PreferredScore.CompareTo(a.PreferredScore);
            return cmp != 0 ? cmp : a.OrderIndex.CompareTo(b.OrderIndex);
        });
    }

    internal static bool BeforeAssignObjectsToScenes(PhysicsScenesServerUpdater __instance)
    {
        var scratch = ScratchByUpdater.GetValue(__instance, _ => new ServerSceneScratch());
        var physicsScenes = PhysicsScenesOf(__instance);
        scratch.UnassignedScenes.Clear();
        for (var i = 0; i < physicsScenes.Count; i++)
            scratch.UnassignedScenes.Add(physicsScenes[i]!);

        SortSpacesDescendingStable(scratch.OrderedSpaces);

        for (var i = 0; i < scratch.OrderedSpaces.Count; i++)
        {
            var item = scratch.OrderedSpaces[i];
            var trackedScene = FindPreferredTrackedScene(item.CommonSpace, scratch.UnassignedScenes);
            if (trackedScene != null)
            {
                scratch.UnassignedScenes.Remove(trackedScene);
            }
            else
            {
                var createdScene = SceneManager.CreateScene("New scene tmp name " + item.SpaceId, SceneParams);
                scratch.NamedScenes.Remove(createdScene);
                trackedScene = CreateTrackedScene(createdScene);
                physicsScenes.Add(trackedScene);
            }

            var targetScene = TrackedSceneOf(trackedScene);
            if (!scratch.NamedScenes.Contains(targetScene) || TrackedSpaceIdOf(trackedScene) != item.SpaceId)
            {
                SetTrackedCurrentSpace(trackedScene, item.SpaceId);
                scratch.NamedScenes.Add(targetScene);
            }

            var entries = item.Entries;
            for (var j = 0; j < entries.Count; j++)
            {
                var entry = entries[j];
                if (entry.CurrentScene != targetScene)
                    SceneManager.MoveGameObjectToScene(entry.PhysicsObject, targetScene);
            }
        }

        for (var i = 0; i < scratch.UnassignedScenes.Count; i++)
        {
            var unused = scratch.UnassignedScenes[i];
            var unusedScene = TrackedSceneOf(unused);
            scratch.NamedScenes.Remove(unusedScene);
            SceneManager.UnloadSceneAsync(unusedScene);
            physicsScenes.Remove(unused);
        }
        scratch.UnassignedScenes.Clear();
        return false;
    }
}
