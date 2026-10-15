using System;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Core.Services;
using HarmonyLib;
using Players.Interface.Services;
using Players.Utils;
using Ships.Parts.Electrolyzer;
using Ships.Parts.EthanolDistiller;
using Ships.Parts.FluidPump;
using Ships.Parts.FuelTank;
using Ships.Parts.WarpExhaust;
using Ships.Parts.WoodPowerGenerator;
using Ships.Parts.Thruster;
using Ships.Parts.FluidValve;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.State;
using UnityEngine;

namespace StellarPerformance;

internal static class PartVisualUpdates
{
    static PartVisualUpdates() { }

    internal sealed class PumpState
    {
        internal bool PositionedOnce;
    }

    internal sealed class Look3DState
    {
        internal IPlayerUpProvider? UpProvider;
        internal bool Initialized;
        internal Vector3 LastOriginPos, LastCamPos, LastUp;
        internal Quaternion LastRotation;
    }

    internal sealed class LookFlatState
    {
        internal bool Initialized;
        internal Quaternion LastSurfaceRot, LastCamRot;
        internal byte LastSide;
    }

    private static readonly ConditionalWeakTable<FluidPumpVisuals, PumpState> Pumps = new ConditionalWeakTable<FluidPumpVisuals, PumpState>();
    private static readonly ConditionalWeakTable<LookAtPlayer, Look3DState> Look3D = new ConditionalWeakTable<LookAtPlayer, Look3DState>();
    private static readonly ConditionalWeakTable<object, LookFlatState> LookFlat = new ConditionalWeakTable<object, LookFlatState>();

    private static readonly Func<PowerGeneratorVisuals, float> CogRotationSpeedOf = NativeAccess.Read<PowerGeneratorVisuals, float>("_rotationSpeed");
    private static readonly Func<FluidPumpVisuals, float> PumpRateOf = NativeAccess.Read<FluidPumpVisuals, float>("_currentRate");

    private static readonly Func<LookAtPlayer, Transform> LookOriginOf = NativeAccess.Read<LookAtPlayer, Transform>("originLocation");
    private static readonly Func<LookAtPlayer, Transform> LookCamOf = NativeAccess.Read<LookAtPlayer, Transform>("_cameraTransform");

    private static readonly Func<LookAtPlayerFlat2Sides, Transform> Flat2SurfaceOf = NativeAccess.Read<LookAtPlayerFlat2Sides, Transform>("surface");
    private static readonly Func<LookAtPlayerFlat2Sides, Vector3> Flat2InitUpOf = NativeAccess.Read<LookAtPlayerFlat2Sides, Vector3>("_canvasInitialLocalUp");
    private static readonly Func<LookAtPlayerFlat2Sides, Transform> Flat2CamOf = NativeAccess.Read<LookAtPlayerFlat2Sides, Transform>("_cameraTransform");

    private static readonly Func<LookAtPlayerFlat4Sides, Transform> Flat4SurfaceOf = NativeAccess.Read<LookAtPlayerFlat4Sides, Transform>("surface");
    private static readonly Func<LookAtPlayerFlat4Sides, Transform> Flat4CamOf = NativeAccess.Read<LookAtPlayerFlat4Sides, Transform>("_cameraTransform");

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        foreach (var target in new[] { AccessTools.Method(typeof(ThrusterVisual), nameof(ThrusterVisual.ReactToNetworkStateChange)), AccessTools.Method(typeof(FluidValveVisual), nameof(FluidValveVisual.ReactToNetworkStateChange)), AccessTools.Method(typeof(PowerGeneratorVisuals), "Update"), AccessTools.Method(typeof(FluidPumpVisuals), "Update"), AccessTools.Method(typeof(LookAtPlayer), "UpdateRotation"), AccessTools.Method(typeof(LookAtPlayerFlat2Sides), "UpdateRotation"), AccessTools.Method(typeof(LookAtPlayerFlat4Sides), "UpdateRotation") })
            PatchSafety.RequireOriginalBody(target, harmony.Id);
        harmony.Patch(AccessTools.Method(typeof(ThrusterVisual), nameof(ThrusterVisual.ReactToNetworkStateChange)),
            prefix: new HarmonyMethod(typeof(PartVisualUpdates), nameof(BeforeThrusterStateChange)));
        harmony.Patch(AccessTools.Method(typeof(FluidValveVisual), nameof(FluidValveVisual.ReactToNetworkStateChange)),
            prefix: new HarmonyMethod(typeof(PartVisualUpdates), nameof(BeforeValveStateChange)));
        harmony.Patch(AccessTools.Method(typeof(PowerGeneratorVisuals), "Update") ?? throw new MissingMethodException("PowerGeneratorVisuals.Update"),
            prefix: new HarmonyMethod(typeof(PartVisualUpdates), nameof(BeforePowerGeneratorUpdate)));
        harmony.Patch(AccessTools.Method(typeof(FluidPumpVisuals), "Update") ?? throw new MissingMethodException("FluidPumpVisuals.Update"),
            prefix: new HarmonyMethod(typeof(PartVisualUpdates), nameof(BeforeFluidPumpUpdate)));
        LiquidVisualUpdates.Install(harmony);
        harmony.Patch(AccessTools.Method(typeof(LookAtPlayer), "UpdateRotation") ?? throw new MissingMethodException("LookAtPlayer.UpdateRotation"),
            prefix: new HarmonyMethod(typeof(PartVisualUpdates), nameof(BeforeLookAtPlayer)));
        harmony.Patch(AccessTools.Method(typeof(LookAtPlayerFlat2Sides), "UpdateRotation") ?? throw new MissingMethodException("LookAtPlayerFlat2Sides.UpdateRotation"),
            prefix: new HarmonyMethod(typeof(PartVisualUpdates), nameof(BeforeLookAtPlayerFlat2)));
        harmony.Patch(AccessTools.Method(typeof(LookAtPlayerFlat4Sides), "UpdateRotation") ?? throw new MissingMethodException("LookAtPlayerFlat4Sides.UpdateRotation"),
            prefix: new HarmonyMethod(typeof(PartVisualUpdates), nameof(BeforeLookAtPlayerFlat4)));
    }

    internal static bool BeforePowerGeneratorUpdate(PowerGeneratorVisuals __instance) => CogRotationSpeedOf(__instance) != 0f;

    // Idle thrusters otherwise perform particle-module writes and an FMOD stop call on
    // every unchanged state event. Keep all active sound/animation updates native.
    internal static bool BeforeThrusterStateChange(StatefulPartStateChangeEvent change) =>
        !(change.PreviousState?.GetType() == typeof(ThrusterState) && change.NewState?.GetType() == typeof(ThrusterState) &&
          ((ThrusterState)change.PreviousState).ThrusterStrength == 0f && ((ThrusterState)change.NewState).ThrusterStrength == 0f);

    internal static bool BeforeValveStateChange(StatefulPartStateChangeEvent change) =>
        !(change.PreviousState?.GetType() == typeof(FluidValveState) && change.NewState?.GetType() == typeof(FluidValveState) &&
          NativeStateReuse.Same(((FluidValveState)change.PreviousState).ValveOpenedRate, ((FluidValveState)change.NewState).ValveOpenedRate));

    internal static bool BeforeFluidPumpUpdate(FluidPumpVisuals __instance)
    {
        var state = Pumps.GetValue(__instance, _ => new PumpState());
        if (PumpRateOf(__instance) == 0f && state.PositionedOnce) return false;
        state.PositionedOnce = true;
        return true;
    }

    internal static bool BeforeLookAtPlayer(LookAtPlayer __instance)
    {
        var origin = LookOriginOf(__instance);
        var cam = LookCamOf(__instance);
        if (origin == null || cam == null) return true;
        var state = Look3D.GetValue(__instance, _ => new Look3DState());
        var upProvider = state.UpProvider ?? (state.UpProvider = ServiceLocator.GetService<IPlayerUpProvider>());
        if (upProvider == null) return true;
        var originPos = origin.position;
        var camPos = cam.position;
        var up = upProvider.WorldPlayerUp;
        if (state.Initialized && originPos == state.LastOriginPos && camPos == state.LastCamPos && up == state.LastUp)
            return false;
        state.LastOriginPos = originPos;
        state.LastCamPos = camPos;
        state.LastUp = up;
        var forward = originPos - camPos;
        var rotation = Quaternion.LookRotation(forward, up);
        if (!state.Initialized || rotation != state.LastRotation)
        {
            __instance.transform.rotation = rotation;
            state.LastRotation = rotation;
            state.Initialized = true;
        }
        return false;
    }

    internal static bool BeforeLookAtPlayerFlat2(LookAtPlayerFlat2Sides __instance)
    {
        var surface = Flat2SurfaceOf(__instance);
        var cam = Flat2CamOf(__instance);
        if (surface == null || cam == null) return true;
        var state = LookFlat.GetValue(__instance, _ => new LookFlatState());
        var surfaceRot = surface.rotation;
        var camRot = cam.rotation;
        if (state.Initialized && surfaceRot == state.LastSurfaceRot && camRot == state.LastCamRot)
            return false;
        state.LastCamRot = camRot;
        var initUp = Flat2InitUpOf(__instance);
        var side = ChooseFlat2Side(surfaceRot, camRot, initUp);
        if (state.Initialized && surfaceRot == state.LastSurfaceRot && side == state.LastSide)
            return false;
        state.LastSurfaceRot = surfaceRot;
        state.LastSide = side;
        state.Initialized = true;
        var forward = -(surfaceRot * Vector3.up);
        var localUp = side == 0 ? initUp : -initUp;
        __instance.transform.rotation = Quaternion.LookRotation(forward, surfaceRot * localUp);
        return false;
    }

    private static Quaternion Conjugate(Quaternion q) => new Quaternion(0f - q.x, 0f - q.y, 0f - q.z, q.w);

    internal static byte ChooseFlat2Side(Quaternion surfaceRot, Quaternion camRot, Vector3 canvasInitialLocalUp)
    {
        var surfaceUp = surfaceRot * Vector3.up;
        var camUp = camRot * Vector3.up;
        var normalized = Vector3.ProjectOnPlane(camUp, surfaceUp).normalized;
        return Vector3.Dot(Conjugate(surfaceRot) * normalized, canvasInitialLocalUp) > 0f ? (byte)0 : (byte)1;
    }

    internal static bool BeforeLookAtPlayerFlat4(LookAtPlayerFlat4Sides __instance)
    {
        var surface = Flat4SurfaceOf(__instance);
        var cam = Flat4CamOf(__instance);
        if (surface == null || cam == null) return true;
        var state = LookFlat.GetValue(__instance, _ => new LookFlatState());
        var surfaceRot = surface.rotation;
        var camRot = cam.rotation;
        if (state.Initialized && surfaceRot == state.LastSurfaceRot && camRot == state.LastCamRot)
            return false;
        state.LastCamRot = camRot;
        var side = ChooseFlat4Side(surfaceRot, camRot, out var localUp);
        if (state.Initialized && surfaceRot == state.LastSurfaceRot && side == state.LastSide)
            return false;
        state.LastSurfaceRot = surfaceRot;
        state.LastSide = side;
        state.Initialized = true;
        var forward = -(surfaceRot * Vector3.up);
        __instance.transform.rotation = Quaternion.LookRotation(forward, surfaceRot * localUp);
        return false;
    }

    internal static byte ChooseFlat4Side(Quaternion surfaceRot, Quaternion camRot, out Vector3 localUp)
    {
        var surfaceUp = surfaceRot * Vector3.up;
        var camUp = camRot * Vector3.up;
        var normalized = Vector3.ProjectOnPlane(camUp, surfaceUp).normalized;
        var lhs = Conjugate(surfaceRot) * normalized;
        var dotForward = Vector3.Dot(lhs, Vector3.forward);
        var dotRight = Vector3.Dot(lhs, Vector3.right);
        if (Mathf.Abs(dotForward) > Mathf.Abs(dotRight))
        {
            if (dotForward > 0f) { localUp = Vector3.forward; return 0; }
            localUp = Vector3.back;
            return 1;
        }
        if (dotRight > 0f) { localUp = Vector3.right; return 2; }
        localUp = Vector3.left;
        return 3;
    }
}
