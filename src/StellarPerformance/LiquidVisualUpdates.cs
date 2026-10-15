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

internal static class LiquidVisualUpdates
{
    static LiquidVisualUpdates() { }

    internal sealed class ExhaustState
    {
        internal Material? Material;
        internal bool HasTime;
        internal float LastTime;
        internal bool HasColor;
        internal Color LastColor;
    }

    internal sealed class TankState
    {
        internal Material? Material;
        internal bool HasDensity, HasPos;
        internal float LastShaderDensity, LastPos;
    }

    internal sealed class MachineLiquidState
    {
        internal Material? Material;
        internal bool HasDensity, HasPos;
        internal float LastDensity, LastPos;
    }

    private static readonly ConditionalWeakTable<WarpExhaustVisuals, ExhaustState> Exhausts = new ConditionalWeakTable<WarpExhaustVisuals, ExhaustState>();
    private static readonly ConditionalWeakTable<FluidTankVisuals, TankState> Tanks = new ConditionalWeakTable<FluidTankVisuals, TankState>();
    private static readonly ConditionalWeakTable<object, MachineLiquidState> Machines = new ConditionalWeakTable<object, MachineLiquidState>();

    private static readonly Action<WarpExhaustVisuals> UpdateExhaustTime = NativeAccess.Call<WarpExhaustVisuals>("UpdateAnimationTime");
    private static readonly Func<WarpExhaustVisuals, float> ExhaustTimeOf = NativeAccess.Read<WarpExhaustVisuals, float>("_currentAnimationTime");
    private static readonly Func<WarpExhaustVisuals, AnimationCurve> ExhaustCurveOf = NativeAccess.Read<WarpExhaustVisuals, AnimationCurve>("intensityCurve");
    private static readonly Func<WarpExhaustVisuals, Color> ExhaustIdleColorOf = NativeAccess.Read<WarpExhaustVisuals, Color>("idleColor");
    private static readonly Func<WarpExhaustVisuals, Color> ExhaustWarpColorOf = NativeAccess.Read<WarpExhaustVisuals, Color>("warpColor");
    private static readonly Func<WarpExhaustVisuals, MeshRenderer> ExhaustRendererOf = NativeAccess.Read<WarpExhaustVisuals, MeshRenderer>("glowyBitRenderer");

    private static readonly Func<FluidTankVisuals, float> TankTargetDensityOf = NativeAccess.Read<FluidTankVisuals, float>("_targetDensity");
    private static readonly Func<FluidTankVisuals, float> TankAnimatedDensityOf = NativeAccess.Read<FluidTankVisuals, float>("_animatedDensity");
    private static readonly Action<FluidTankVisuals, float> SetTankAnimatedDensity = NativeAccess.Write<FluidTankVisuals, float>("_animatedDensity");
    private static readonly Func<FluidTankVisuals, float> TankVelocityOf = NativeAccess.Read<FluidTankVisuals, float>("_currentVelocity");
    private static readonly Action<FluidTankVisuals, float> SetTankVelocity = NativeAccess.Write<FluidTankVisuals, float>("_currentVelocity");
    private static readonly Func<FluidTankVisuals, float> TankPosOf = NativeAccess.Read<FluidTankVisuals, float>("_currentPos");
    private static readonly Action<FluidTankVisuals, float> SetTankPos = NativeAccess.Write<FluidTankVisuals, float>("_currentPos");
    private static readonly Func<FluidTankVisuals, MeshRenderer> TankWindowRendererOf = NativeAccess.Read<FluidTankVisuals, MeshRenderer>("windowRenderer");

    private static readonly Func<EthanolDistillerVisuals, float> DistillerTargetVelOf = NativeAccess.Read<EthanolDistillerVisuals, float>("_liquidScrollTargetVelocity");
    private static readonly Func<EthanolDistillerVisuals, float> DistillerTargetDensityOf = NativeAccess.Read<EthanolDistillerVisuals, float>("_targetDensity");
    private static readonly Func<EthanolDistillerVisuals, float> DistillerDensityOf = NativeAccess.Read<EthanolDistillerVisuals, float>("_density");
    private static readonly Action<EthanolDistillerVisuals, float> SetDistillerDensity = NativeAccess.Write<EthanolDistillerVisuals, float>("_density");
    private static readonly Func<EthanolDistillerVisuals, float> DistillerVelOf = NativeAccess.Read<EthanolDistillerVisuals, float>("_liquidScrollVelocity");
    private static readonly Action<EthanolDistillerVisuals, float> SetDistillerVel = NativeAccess.Write<EthanolDistillerVisuals, float>("_liquidScrollVelocity");
    private static readonly Func<EthanolDistillerVisuals, float> DistillerPosOf = NativeAccess.Read<EthanolDistillerVisuals, float>("_position");
    private static readonly Action<EthanolDistillerVisuals, float> SetDistillerPos = NativeAccess.Write<EthanolDistillerVisuals, float>("_position");
    private static readonly Func<EthanolDistillerVisuals, MeshRenderer> DistillerRendererOf = NativeAccess.Read<EthanolDistillerVisuals, MeshRenderer>("glassContainerRenderer");

    private static readonly Func<ElectrolyzerVisuals, float> ElectrolyzerTargetVelOf = NativeAccess.Read<ElectrolyzerVisuals, float>("_liquidScrollTargetVelocity");
    private static readonly Func<ElectrolyzerVisuals, float> ElectrolyzerTargetDensityOf = NativeAccess.Read<ElectrolyzerVisuals, float>("_targetDensity");
    private static readonly Func<ElectrolyzerVisuals, float> ElectrolyzerDensityOf = NativeAccess.Read<ElectrolyzerVisuals, float>("_density");
    private static readonly Action<ElectrolyzerVisuals, float> SetElectrolyzerDensity = NativeAccess.Write<ElectrolyzerVisuals, float>("_density");
    private static readonly Func<ElectrolyzerVisuals, float> ElectrolyzerVelOf = NativeAccess.Read<ElectrolyzerVisuals, float>("_liquidScrollVelocity");
    private static readonly Action<ElectrolyzerVisuals, float> SetElectrolyzerVel = NativeAccess.Write<ElectrolyzerVisuals, float>("_liquidScrollVelocity");
    private static readonly Func<ElectrolyzerVisuals, float> ElectrolyzerPosOf = NativeAccess.Read<ElectrolyzerVisuals, float>("_position");
    private static readonly Action<ElectrolyzerVisuals, float> SetElectrolyzerPos = NativeAccess.Write<ElectrolyzerVisuals, float>("_position");
    private static readonly Func<ElectrolyzerVisuals, MeshRenderer> ElectrolyzerRendererOf = NativeAccess.Read<ElectrolyzerVisuals, MeshRenderer>("glassContainerRenderer");

    private static bool _shaderIdsResolved;
    private static int _liquidDensityId, _liquidPosId, _emissionColorId;

    private static void EnsureShaderIds()
    {
        if (_shaderIdsResolved) return;
        _liquidDensityId = Shader.PropertyToID("_LiquidDensity");
        _liquidPosId = Shader.PropertyToID("_LiquidPos");
        _emissionColorId = Shader.PropertyToID("_EmissionColor");
        _shaderIdsResolved = true;
    }

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        foreach (var target in new[] { AccessTools.Method(typeof(WarpExhaustVisuals), "Update"), AccessTools.Method(typeof(FluidTankVisuals), "Update"), AccessTools.Method(typeof(EthanolDistillerVisuals), "Update"), AccessTools.Method(typeof(ElectrolyzerVisuals), "Update") })
            PatchSafety.RequireOriginalBody(target, harmony.Id);
        harmony.Patch(AccessTools.Method(typeof(WarpExhaustVisuals), nameof(WarpExhaustVisuals.SetContext)) ?? throw new MissingMethodException("WarpExhaustVisuals.SetContext"),
            postfix: new HarmonyMethod(typeof(LiquidVisualUpdates), nameof(AfterWarpExhaustSetContext)));
        harmony.Patch(AccessTools.Method(typeof(WarpExhaustVisuals), "Update") ?? throw new MissingMethodException("WarpExhaustVisuals.Update"),
            prefix: new HarmonyMethod(typeof(LiquidVisualUpdates), nameof(BeforeWarpExhaustUpdate)));
        harmony.Patch(AccessTools.Method(typeof(FluidTankVisuals), "UpdateState") ?? throw new MissingMethodException("FluidTankVisuals.UpdateState"),
            postfix: new HarmonyMethod(typeof(LiquidVisualUpdates), nameof(AfterFluidTankUpdateState)));
        harmony.Patch(AccessTools.Method(typeof(FluidTankVisuals), "Update") ?? throw new MissingMethodException("FluidTankVisuals.Update"),
            prefix: new HarmonyMethod(typeof(LiquidVisualUpdates), nameof(BeforeFluidTankUpdate)));
        harmony.Patch(AccessTools.Method(typeof(EthanolDistillerVisuals), "Update") ?? throw new MissingMethodException("EthanolDistillerVisuals.Update"),
            prefix: new HarmonyMethod(typeof(LiquidVisualUpdates), nameof(BeforeDistillerUpdate)));
        harmony.Patch(AccessTools.Method(typeof(ElectrolyzerVisuals), "Update") ?? throw new MissingMethodException("ElectrolyzerVisuals.Update"),
            prefix: new HarmonyMethod(typeof(LiquidVisualUpdates), nameof(BeforeElectrolyzerUpdate)));
    }

    internal static void AfterWarpExhaustSetContext(WarpExhaustVisuals __instance)
    {
        if (Exhausts.TryGetValue(__instance, out var state))
        {
            state.HasTime = false;
            state.HasColor = false;
            state.Material = null;
        }
    }

    internal static bool BeforeWarpExhaustUpdate(WarpExhaustVisuals __instance)
    {
        UpdateExhaustTime(__instance);
        var time = ExhaustTimeOf(__instance);
        var state = Exhausts.GetValue(__instance, _ => new ExhaustState());
        if (state.HasTime && state.LastTime == time) return false;
        state.LastTime = time;
        state.HasTime = true;
        var t = ExhaustCurveOf(__instance).Evaluate(time / 3.665f);
        var color = Color.Lerp(ExhaustIdleColorOf(__instance), ExhaustWarpColorOf(__instance), t);
        if (!state.HasColor || state.LastColor != color)
        {
            EnsureShaderIds();
            var mat = state.Material ?? (state.Material = ExhaustRendererOf(__instance).material);
            mat.SetColor(_emissionColorId, color);
            state.LastColor = color;
            state.HasColor = true;
        }
        return false;
    }

    internal static void AfterFluidTankUpdateState(FluidTankVisuals __instance)
    {
        if (Tanks.TryGetValue(__instance, out var state))
        {
            state.Material = null;
            state.HasDensity = false;
            state.HasPos = false;
        }
    }

    internal static bool BeforeFluidTankUpdate(FluidTankVisuals __instance)
    {
        EnsureShaderIds();
        var state = Tanks.GetValue(__instance, _ => new TankState());
        var targetDensity = TankTargetDensityOf(__instance);
        var animatedDensity = TankAnimatedDensityOf(__instance);
        var currentVelocity = TankVelocityOf(__instance);
        var currentPos = TankPosOf(__instance);
        StepFluidTank(targetDensity, animatedDensity, currentVelocity, currentPos, Time.deltaTime,
            out var nextAnimatedDensity, out var nextVelocity, out var nextPos, out var shaderDensity);
        SetTankAnimatedDensity(__instance, nextAnimatedDensity);
        SetTankVelocity(__instance, nextVelocity);
        SetTankPos(__instance, nextPos);
        var mat = state.Material ?? (state.Material = TankWindowRendererOf(__instance).material);
        if (!state.HasDensity || state.LastShaderDensity != shaderDensity)
        {
            mat.SetFloat(_liquidDensityId, shaderDensity);
            state.LastShaderDensity = shaderDensity;
            state.HasDensity = true;
        }
        if (shaderDensity > 0f && (!state.HasPos || state.LastPos != nextPos))
        {
            mat.SetFloat(_liquidPosId, nextPos);
            state.LastPos = nextPos;
            state.HasPos = true;
        }
        return false;
    }

    internal static void StepFluidTank(float targetDensity, float animatedDensity, float currentVelocity, float currentPos, float deltaTime,
        out float nextAnimatedDensity, out float nextVelocity, out float nextPos, out float shaderDensity)
    {
        var diff = targetDensity - animatedDensity;
        var absDiff = Mathf.Abs(diff);
        float targetVelocity;
        if (absDiff > 1E-06f)
        {
            nextAnimatedDensity = animatedDensity + diff * Mathf.Min(deltaTime * 10f, 1f);
            targetVelocity = Mathf.Sign(diff) * Mathf.Max(absDiff * 60f, 0.05f);
        }
        else
        {
            nextAnimatedDensity = targetDensity;
            targetVelocity = Mathf.Sign(currentVelocity) * 0.05f;
        }
        nextVelocity = Mathf.Lerp(currentVelocity, targetVelocity, deltaTime * 2f);
        nextPos = currentPos - nextVelocity * deltaTime;
        shaderDensity = nextAnimatedDensity <= 0f ? 0f : Mathf.Pow(nextAnimatedDensity, 1.2f);
    }

    internal static bool BeforeDistillerUpdate(EthanolDistillerVisuals __instance)
    {
        EnsureShaderIds();
        var state = Machines.GetValue(__instance, _ => new MachineLiquidState());
        StepMachineLiquid(DistillerVelOf(__instance), DistillerTargetVelOf(__instance), DistillerDensityOf(__instance), DistillerTargetDensityOf(__instance),
            DistillerPosOf(__instance), Time.deltaTime, subtractPos: true, out var nextVel, out var nextDensity, out var nextPos);
        SetDistillerVel(__instance, nextVel);
        SetDistillerDensity(__instance, nextDensity);
        SetDistillerPos(__instance, nextPos);
        ApplyMachineShader(state, DistillerRendererOf(__instance), nextDensity, nextPos);
        return false;
    }

    internal static bool BeforeElectrolyzerUpdate(ElectrolyzerVisuals __instance)
    {
        EnsureShaderIds();
        var state = Machines.GetValue(__instance, _ => new MachineLiquidState());
        StepMachineLiquid(ElectrolyzerVelOf(__instance), ElectrolyzerTargetVelOf(__instance), ElectrolyzerDensityOf(__instance), ElectrolyzerTargetDensityOf(__instance),
            ElectrolyzerPosOf(__instance), Time.deltaTime, subtractPos: false, out var nextVel, out var nextDensity, out var nextPos);
        SetElectrolyzerVel(__instance, nextVel);
        SetElectrolyzerDensity(__instance, nextDensity);
        SetElectrolyzerPos(__instance, nextPos);
        ApplyMachineShader(state, ElectrolyzerRendererOf(__instance), nextDensity, nextPos);
        return false;
    }

    internal static void StepMachineLiquid(float currentVel, float targetVel, float currentDensity, float targetDensity, float currentPos, float deltaTime, bool subtractPos,
        out float nextVel, out float nextDensity, out float nextPos)
    {
        var t = deltaTime * 2f;
        nextVel = Mathf.Abs(targetVel - currentVel) <= 1E-06f ? targetVel : Mathf.Lerp(currentVel, targetVel, t);
        nextDensity = Mathf.Abs(targetDensity - currentDensity) <= 1E-06f ? targetDensity : Mathf.Lerp(currentDensity, targetDensity, t);
        nextPos = subtractPos ? currentPos - nextVel * deltaTime : currentPos + nextVel * deltaTime;
    }

    private static void ApplyMachineShader(MachineLiquidState state, MeshRenderer renderer, float density, float pos)
    {
        var mat = state.Material ?? (state.Material = renderer.material);
        if (!state.HasDensity || state.LastDensity != density)
        {
            mat.SetFloat(_liquidDensityId, density);
            state.LastDensity = density;
            state.HasDensity = true;
        }
        if (density > 0f && (!state.HasPos || state.LastPos != pos))
        {
            mat.SetFloat(_liquidPosId, pos);
            state.LastPos = pos;
            state.HasPos = true;
        }
    }

}
