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

internal static class PhysicsSceneTransforms
{
    static PhysicsSceneTransforms() { }

    private static readonly MethodInfo NativeSetPosition = AccessTools.PropertySetter(typeof(Transform), nameof(Transform.position)) ?? throw new MissingMethodException("Transform.set_position");
    private static readonly MethodInfo NativeSetRotation = AccessTools.PropertySetter(typeof(Transform), nameof(Transform.rotation)) ?? throw new MissingMethodException("Transform.set_rotation");
    private static readonly MethodInfo GuardedSetPosition = AccessTools.Method(typeof(PhysicsSceneTransforms), nameof(SetPositionIfChanged)) ?? throw new MissingMethodException("PhysicsSceneTransforms.SetPositionIfChanged");
    private static readonly MethodInfo GuardedSetRotation = AccessTools.Method(typeof(PhysicsSceneTransforms), nameof(SetRotationIfChanged)) ?? throw new MissingMethodException("PhysicsSceneTransforms.SetRotationIfChanged");

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var serverInterior = AccessTools.Method(typeof(WorldPhysicsServerUpdater), "UpdateShipInteriorLoading", new[] { typeof(TrackedShipServer) }) ?? throw new MissingMethodException("WorldPhysicsServerUpdater.UpdateShipInteriorLoading");
        var clientInterior = AccessTools.Method(typeof(WorldPhysicsClientUpdater), "UpdateShipInteriorLoading", new[] { typeof(uint), typeof(TrackedShipClient) }) ?? throw new MissingMethodException("WorldPhysicsClientUpdater.UpdateShipInteriorLoading");
        var clientRigidbody = AccessTools.Method(typeof(ShipRigidbody), nameof(ShipRigidbody.SetPhysicsStateClient), new[] { typeof(ShipPhysicsCalculationProperties) }) ?? throw new MissingMethodException("ShipRigidbody.SetPhysicsStateClient");
        foreach (var target in new[] { serverInterior, clientInterior, clientRigidbody })
            harmony.Patch(target, transpiler: new HarmonyMethod(typeof(PhysicsSceneTransforms), nameof(TranspileExactTransformWrites)));

        PhysicsSceneCache.Install(harmony);
    }

    internal static bool ExactEquals(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
    internal static bool ExactEquals(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

    internal static void SetPositionIfChanged(Transform transform, Vector3 position)
    {
        if (!ExactEquals(transform.position, position)) transform.position = position;
    }

    internal static void SetRotationIfChanged(Transform transform, Quaternion rotation)
    {
        if (!ExactEquals(transform.rotation, rotation)) transform.rotation = rotation;
    }

    internal static IEnumerable<CodeInstruction> TranspileExactTransformWrites(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.Select(code => new CodeInstruction(code)).ToList();
        var positions = 0;
        var rotations = 0;
        foreach (var code in codes)
        {
            if (code.Calls(NativeSetPosition))
            {
                code.opcode = OpCodes.Call;
                code.operand = GuardedSetPosition;
                positions++;
            }
            else if (code.Calls(NativeSetRotation))
            {
                code.opcode = OpCodes.Call;
                code.operand = GuardedSetRotation;
                rotations++;
            }
        }
        if (positions != 1 || rotations != 1)
            throw new InvalidOperationException("Expected exactly one Transform.position and one Transform.rotation write.");
        return codes;
    }

    internal static bool ShouldTransposeCollider(ISpace? currentSpace, ISpace? targetSpace) => !ReferenceEquals(currentSpace, targetSpace);

}
