using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Items.Model;
using Ships;
using Ships.Cables;
using Ships.Cables.Fluids;
using Ships.Cables.VisualObjects;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Model.State;
using Ships.Services;
using UnityEngine;

namespace StellarPerformance;

internal static class CableVisualUpdates
{
    static CableVisualUpdates() { }

    internal sealed class FluidMaterialState
    {
        internal Material? AssignedSharedMaterial;
        internal Material? InstanceMaterial;
        internal bool HasColor, HasDensity, HasPos;
        internal Color LastColor;
        internal float LastDensity, LastPos;
    }

    internal sealed class SignalMaterialState
    {
        internal Material? InstanceMaterial;
        internal bool Initialized;
        internal Color EmissionColor;
        internal Color BaseColor;
    }

    internal sealed class PowerMaterialState
    {
        internal Material? InstanceMaterial;
        internal bool Initialized;
        internal Color EmissiveColor;
    }

    [ThreadStatic] internal static bool DeferDensityToFlow;
    private static readonly ConditionalWeakTable<FluidCableVisual, FluidMaterialState> FluidVisuals = new ConditionalWeakTable<FluidCableVisual, FluidMaterialState>();
    private static readonly ConditionalWeakTable<SignalCableVisual, SignalMaterialState> SignalVisuals = new ConditionalWeakTable<SignalCableVisual, SignalMaterialState>();
    private static readonly ConditionalWeakTable<PowerCableVisual, PowerMaterialState> PowerVisuals = new ConditionalWeakTable<PowerCableVisual, PowerMaterialState>();

    private static readonly Func<FluidCableVisual, MeshRenderer> ScrollingCordOf = NativeAccess.Read<FluidCableVisual, MeshRenderer>("scrollingCord");
    private static readonly Func<FluidCableVisual, float> CurrentLiquidPosOf = NativeAccess.Read<FluidCableVisual, float>("_currentLiquidPos");
    private static readonly Action<FluidCableVisual, float> SetCurrentLiquidPos = NativeAccess.Write<FluidCableVisual, float>("_currentLiquidPos");
    private static readonly Func<FluidCableVisual, float> ContentDensityOf = NativeAccess.Read<FluidCableVisual, float>("_contentDensity");
    private static readonly Action<FluidCableVisual, float> SetContentDensity = NativeAccess.Write<FluidCableVisual, float>("_contentDensity");
    private static readonly Func<FluidCableVisual, float> CurrentFlowOf = NativeAccess.Read<FluidCableVisual, float>("_currentFlow");
    private static readonly Action<FluidCableVisual, float> SetCurrentFlow = NativeAccess.Write<FluidCableVisual, float>("_currentFlow");
    private static readonly Func<FluidCableVisual, float> DisplayedDensityOf = NativeAccess.Read<FluidCableVisual, float>("_displayedDensity");
    private static readonly Action<FluidCableVisual, float> SetDisplayedDensity = NativeAccess.Write<FluidCableVisual, float>("_displayedDensity");
    private static readonly Func<FluidCableVisual, Material> CurrentMatOf = NativeAccess.Read<FluidCableVisual, Material>("_currentMat");
    private static readonly Action<FluidCableVisual, Material> SetCurrentMat = NativeAccess.Write<FluidCableVisual, Material>("_currentMat");

    private static readonly Func<SignalCableVisual, MeshRenderer> SignalRendererOf = NativeAccess.Read<SignalCableVisual, MeshRenderer>("signalRenderer");
    private static readonly Func<SignalCableVisual, Color> FullSignalEmissionOf = NativeAccess.Read<SignalCableVisual, Color>("fullSignalEmissionColor");
    private static readonly Func<SignalCableVisual, Color> NeutralColorOf = NativeAccess.Read<SignalCableVisual, Color>("neutralColor");
    private static readonly Func<SignalCableVisual, Color> NegativeSignalColorOf = NativeAccess.Read<SignalCableVisual, Color>("negativeSignalColor");

    private static readonly Func<PowerCableVisual, MeshRenderer> PowerRendererOf = NativeAccess.Read<PowerCableVisual, MeshRenderer>("powerRenderer");
    private static readonly Func<PowerCableVisual, Color> FullGlowOf = NativeAccess.Read<PowerCableVisual, Color>("fullGlow");
    private static readonly Func<PowerCableVisual, Color> NeutralGlowOf = NativeAccess.Read<PowerCableVisual, Color>("neutralGlow");

    private static bool _shaderIdsResolved;
    private static int _liquidDensityId, _liquidPosId, _colorId, _emissionColorId, _emissiveColorId;

    private static void EnsureShaderIds()
    {
        if (_shaderIdsResolved) return;
        _liquidDensityId = Shader.PropertyToID("_LiquidDensity");
        _liquidPosId = Shader.PropertyToID("_LiquidPos");
        _colorId = Shader.PropertyToID("_Color");
        _emissionColorId = Shader.PropertyToID("_EmissionColor");
        _emissiveColorId = Shader.PropertyToID("_EmissiveColor");
        _shaderIdsResolved = true;
    }

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        foreach (var target in new[] { AccessTools.Method(typeof(FluidCableVisual), nameof(FluidCableVisual.UpdateContent)), AccessTools.Method(typeof(FluidCableVisual), nameof(FluidCableVisual.UpdateFlow)), AccessTools.Method(typeof(SignalCableVisual), nameof(SignalCableVisual.UpdateGlow)), AccessTools.Method(typeof(PowerCableVisual), nameof(PowerCableVisual.UpdateGlow)) })
            PatchSafety.RequireOriginalBody(target, harmony.Id);
        harmony.Patch(AccessTools.Method(typeof(FluidCableVisual), nameof(FluidCableVisual.UpdateContent)), prefix: new HarmonyMethod(typeof(CableVisualUpdates), nameof(BeforeFluidUpdateContent)));
        harmony.Patch(AccessTools.Method(typeof(FluidCableVisual), nameof(FluidCableVisual.UpdateFlow)), prefix: new HarmonyMethod(typeof(CableVisualUpdates), nameof(BeforeFluidUpdateFlow)));
        harmony.Patch(AccessTools.Method(typeof(SignalCableVisual), nameof(SignalCableVisual.UpdateGlow)), prefix: new HarmonyMethod(typeof(SignalCableTopology), nameof(SignalCableTopology.BeforeSignalUpdateGlow)));
        harmony.Patch(AccessTools.Method(typeof(PowerCableVisual), nameof(PowerCableVisual.UpdateGlow)), prefix: new HarmonyMethod(typeof(SignalCableTopology), nameof(SignalCableTopology.BeforePowerUpdateGlow)));
    }

    internal static bool BeforeFluidUpdateContent(FluidCableVisual __instance, Material mat, float density, Color color)
    {
        EnsureShaderIds();
        var state = FluidVisuals.GetValue(__instance, _ => new FluidMaterialState());
        var currentMat = CurrentMatOf(__instance);
        var cord = ScrollingCordOf(__instance);
        if (currentMat != mat)
        {
            cord.material = mat;
            SetCurrentMat(__instance, mat);
            state.AssignedSharedMaterial = mat;
            state.InstanceMaterial = cord.material;
            state.HasColor = false;
            state.HasDensity = false;
            state.HasPos = false;
        }
        else if (state.InstanceMaterial == null)
        {
            state.InstanceMaterial = cord.material;
        }
        SetContentDensity(__instance, density);
        var instanceMat = state.InstanceMaterial!;
        if (!DeferDensityToFlow && (!state.HasDensity || state.LastDensity != density))
        {
            instanceMat.SetFloat(_liquidDensityId, density);
            state.LastDensity = density;
            state.HasDensity = true;
        }
        if (!state.HasColor || state.LastColor != color)
        {
            instanceMat.SetColor(_colorId, color);
            state.LastColor = color;
            state.HasColor = true;
        }
        return false;
    }

    internal static bool BeforeFluidUpdateFlow(FluidCableVisual __instance, float flow)
    {
        EnsureShaderIds();
        var state = FluidVisuals.GetValue(__instance, _ => new FluidMaterialState());
        if (state.InstanceMaterial == null) state.InstanceMaterial = ScrollingCordOf(__instance).material;
        var dt = Time.deltaTime;
        StepFluidFlow(flow, dt, ContentDensityOf(__instance), CurrentFlowOf(__instance), CurrentLiquidPosOf(__instance), DisplayedDensityOf(__instance),
            out var nextFlow, out var nextLiquidPos, out var nextDisplayedDensity);
        SetCurrentFlow(__instance, nextFlow);
        SetCurrentLiquidPos(__instance, nextLiquidPos);
        SetDisplayedDensity(__instance, nextDisplayedDensity);
        var instanceMat = state.InstanceMaterial!;
        if (!state.HasDensity || state.LastDensity != nextDisplayedDensity)
        {
            instanceMat.SetFloat(_liquidDensityId, nextDisplayedDensity);
            state.LastDensity = nextDisplayedDensity;
            state.HasDensity = true;
        }
        var shaderPos = 0f - nextLiquidPos;
        if (!state.HasPos || state.LastPos != shaderPos)
        {
            instanceMat.SetFloat(_liquidPosId, shaderPos);
            state.LastPos = shaderPos;
            state.HasPos = true;
        }
        return false;
    }

    internal static void StepFluidFlow(float flow, float deltaTime, float contentDensity, float currentFlow, float currentLiquidPos, float displayedDensity,
        out float nextFlow, out float nextLiquidPos, out float nextDisplayedDensity)
    {
        flow = Mathf.Clamp(flow, -0.25f, 0.25f);
        nextFlow = Mathf.Lerp(currentFlow, flow, deltaTime * 10f);
        var targetDensity = Mathf.Max(Mathf.Clamp01(Mathf.Abs(nextFlow) / 0.25f * 32f), contentDensity);
        nextLiquidPos = currentLiquidPos + nextFlow;
        nextDisplayedDensity = Mathf.Lerp(displayedDensity, targetDensity, Mathf.Clamp01(deltaTime * 4f));
    }

    internal static int EmissionColorPropertyId { get { EnsureShaderIds(); return _emissionColorId; } }
    internal static int ColorPropertyId { get { EnsureShaderIds(); return _colorId; } }
    internal static int EmissiveColorPropertyId { get { EnsureShaderIds(); return _emissiveColorId; } }
    internal static ConditionalWeakTable<SignalCableVisual, SignalMaterialState> SignalStates => SignalVisuals;
    internal static ConditionalWeakTable<PowerCableVisual, PowerMaterialState> PowerStates => PowerVisuals;
    internal static MeshRenderer GetSignalRenderer(SignalCableVisual visual) => SignalRendererOf(visual);
    internal static Color GetFullSignalEmission(SignalCableVisual visual) => FullSignalEmissionOf(visual);
    internal static Color GetNeutralColor(SignalCableVisual visual) => NeutralColorOf(visual);
    internal static Color GetNegativeSignalColor(SignalCableVisual visual) => NegativeSignalColorOf(visual);
    internal static MeshRenderer GetPowerRenderer(PowerCableVisual visual) => PowerRendererOf(visual);
    internal static Color GetFullGlow(PowerCableVisual visual) => FullGlowOf(visual);
    internal static Color GetNeutralGlow(PowerCableVisual visual) => NeutralGlowOf(visual);
}

internal static class SignalCableTopology
{
    internal static bool BeforeSignalUpdateGlow(SignalCableVisual __instance, float value)
    {
        var state = CableVisualUpdates.SignalStates.GetValue(__instance, _ => new CableVisualUpdates.SignalMaterialState());
        var mat = state.InstanceMaterial ?? (state.InstanceMaterial = CableVisualUpdates.GetSignalRenderer(__instance).material);
        var emissionId = CableVisualUpdates.EmissionColorPropertyId;
        var colorId = CableVisualUpdates.ColorPropertyId;
        if (!state.Initialized)
        {
            state.EmissionColor = mat.GetColor(emissionId);
            state.BaseColor = mat.GetColor(colorId);
            state.Initialized = true;
        }
        StepSignalGlow(value, Time.deltaTime, state.EmissionColor, state.BaseColor,
            CableVisualUpdates.GetFullSignalEmission(__instance), CableVisualUpdates.GetNeutralColor(__instance), CableVisualUpdates.GetNegativeSignalColor(__instance),
            out var nextEmission, out var nextColor);
        if (nextEmission != state.EmissionColor)
        {
            mat.SetColor(emissionId, nextEmission);
            state.EmissionColor = nextEmission;
        }
        if (nextColor != state.BaseColor)
        {
            mat.SetColor(colorId, nextColor);
            state.BaseColor = nextColor;
        }
        return false;
    }

    internal static void StepSignalGlow(float value, float deltaTime, Color currentEmission, Color currentColor, Color fullEmission, Color neutralColor, Color negativeColor,
        out Color nextEmission, out Color nextColor)
    {
        var t = Mathf.Clamp01(value);
        var t2 = 1f - Mathf.Clamp01(0f - value);
        var targetEmission = Color.Lerp(Color.black, fullEmission, t);
        var targetColor = Color.Lerp(negativeColor, neutralColor, t2);
        var lerpFactor = 12f * deltaTime;
        nextEmission = Color.Lerp(currentEmission, targetEmission, lerpFactor);
        nextColor = Color.Lerp(currentColor, targetColor, lerpFactor);
    }

    internal static bool BeforePowerUpdateGlow(PowerCableVisual __instance, float watts)
    {
        var state = CableVisualUpdates.PowerStates.GetValue(__instance, _ => new CableVisualUpdates.PowerMaterialState());
        var mat = state.InstanceMaterial ?? (state.InstanceMaterial = CableVisualUpdates.GetPowerRenderer(__instance).material);
        var t = Mathf.Clamp01(watts / 2000f);
        var next = Color.Lerp(CableVisualUpdates.GetNeutralGlow(__instance), CableVisualUpdates.GetFullGlow(__instance), t);
        if (!state.Initialized || state.EmissiveColor != next)
        {
            mat.SetColor(CableVisualUpdates.EmissiveColorPropertyId, next);
            state.EmissiveColor = next;
            state.Initialized = true;
        }
        return false;
    }
}
