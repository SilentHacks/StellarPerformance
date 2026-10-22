using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Rendering.Features;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class PresetTests
{
    [Fact]
    public void BalancedRetainsEveryExistingRoomAndPresentationDefault()
    {
        var p = PresetPolicy.Named("Balanced");
        Assert.True(p.Rooms); Assert.Equal(PresetPolicy.OutlinesNative, p.Outlines);
        Assert.Equal(0, p.ReadoutHz); Assert.Equal(0, p.LabelPixels);
        Assert.Equal(0, p.ShadowDistance); Assert.Equal(0, p.Cascades); Assert.Equal(1, p.ExhaustDensity);
        Assert.Equal(0, p.LabelDistance); Assert.Equal(0, p.ShadowResolution); Assert.Equal(0, p.ShadowQuality);
        Assert.Equal(1, p.VegetationRange); Assert.Equal(0, p.CableHz); Assert.Equal(0, p.TerrainFrames);
        var f = PresetPolicy.Named("Faithful"); Assert.False(f.Rooms);
        Assert.True(PresetSelector.SameSettings(p, new PresetPolicy("Balanced", true, 0, 0, 0, 0, 0, 1)));
        Assert.True(PresetSelector.SameSettings(f, new PresetPolicy("Faithful", false, 0, 0, 0, 0, 0, 1)));
    }
    [Theory]
    [InlineData("Performance", PresetPolicy.OutlinesSilhouette, 0, 0, 150, 2048, 0, .75f, 1f, 30, 6)]
    [InlineData("Aggressive", PresetPolicy.OutlinesOff, 0, 0, 100, 2048, 0, .5f, .5f, 15, 15)]
    public void HigherLevelsUseMeasuredCosmeticLevers(string name, int outlines, int hz, int pixels, int distance, int resolution, int quality, float vegetation, float exhaust, int cables, int terrain)
    {
        var p = PresetPolicy.Named(name);
        Assert.True(p.Rooms); Assert.Equal(outlines, p.Outlines); Assert.Equal(hz, p.ReadoutHz);
        Assert.Equal(pixels, p.LabelPixels); Assert.Equal(distance, p.ShadowDistance); Assert.Equal(2, p.Cascades);
        Assert.Equal(resolution, p.ShadowResolution); Assert.Equal(quality, p.ShadowQuality);
        Assert.Equal(vegetation, p.VegetationRange); Assert.Equal(exhaust, p.ExhaustDensity);
        Assert.Equal(cables, p.CableHz); Assert.Equal(terrain, p.TerrainFrames);
    }
    [Theory]
    [InlineData("Performance")]
    [InlineData("Aggressive")]
    public void NamedLevelsNeverRemoveWholeCategoriesOfScenery(string name)
    {
        var p = PresetPolicy.Named(name);
        // Earlier tiers hid every label beyond 4-12 m, drew 10% of vegetation and no shadows.
        Assert.Equal(0, p.LabelDistance);
        Assert.Equal(0, p.ShadowQuality); Assert.True(p.ShadowDistance >= 100); Assert.True(p.ShadowResolution >= 2048);
        Assert.True(p.VegetationRange >= .5f); Assert.True(p.ExhaustDensity >= .5f);
        Assert.True(p.CableHz >= 15); Assert.Equal(0, p.ReadoutHz); Assert.Equal(0, p.LabelPixels);
    }
    [Fact]
    public void InvalidCustomValuesCannotCreateUnboundedRatesOrNonfiniteGraphics()
    {
        var p = new PresetPolicy("Custom", true, 7, -5, 100, float.NaN, 3, float.PositiveInfinity, float.NaN, 300, 88, 0, 2, 99);
        Assert.Equal(PresetPolicy.OutlinesNative, p.Outlines);
        Assert.Equal(1, p.ReadoutHz); Assert.Equal(32, p.LabelPixels); Assert.Equal(0, p.ShadowDistance);
        Assert.Equal(0, p.Cascades); Assert.Equal(1, p.ExhaustDensity);
        Assert.Equal(0, p.LabelDistance); Assert.Equal(256, p.ShadowResolution); Assert.Equal(0, p.ShadowQuality);
        Assert.Equal(.1f, p.VegetationRange); Assert.Equal(5, p.CableHz); Assert.Equal(30, p.TerrainFrames);
        Assert.Equal(0, new PresetPolicy("Custom", true, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, -3, 1).CableHz);
        Assert.Equal(0, new PresetPolicy("Custom", true, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1).TerrainFrames);
        Assert.Throws<ArgumentException>(() => PresetPolicy.Named("Unknown"));
    }

    [Fact]
    public void CosmeticRefreshIsNativeAtZeroAndSteadyOtherwise()
    {
        var box = new VisualCadence.Box();
        for (var i = 0; i < 10; i++) Assert.True(VisualCadence.Due(box, i * .016f, 0));
        box = new VisualCadence.Box();
        var due = Enumerable.Range(0, 600).Count(i => VisualCadence.Due(box, i / 60f, 15));
        Assert.InRange(due, 148, 152); // 15 Hz across ten seconds at 60 FPS.
        Assert.False(VisualCadence.Due(box, 600 / 60f - .001f, 15));
        Assert.True(VisualCadence.Due(box, 1f, 15)); // A clock reset resynchronises instead of stalling.
        Assert.True(VisualCadence.Due(box, 100f, 15)); // A long stall refreshes immediately.
    }

    [Fact]
    public void TerrainReuseRequiresAStillObserverAndBoundedAge()
    {
        var state = new VisualCadence.Traversal { Position = new Vector3(10, 0, 0), Frame = 100, Epoch = 3 };
        Assert.True(VisualCadence.ShouldSkip(state, new Vector3(10.1f, 0, 0), 105, 0, 15, 3));
        Assert.False(VisualCadence.ShouldSkip(state, new Vector3(10.1f, 0, 0), 105, 0, 0, 3)); // Native policy.
        Assert.False(VisualCadence.ShouldSkip(state, new Vector3(10.1f, 0, 0), 115, 0, 15, 3)); // Age limit.
        Assert.False(VisualCadence.ShouldSkip(state, new Vector3(10.3f, 0, 0), 105, 0, 15, 3)); // Moved.
        Assert.False(VisualCadence.ShouldSkip(state, new Vector3(10.1f, 0, 0), 105, 0, 15, 4)); // Settings changed.
        Assert.False(VisualCadence.ShouldSkip(state, new Vector3(float.NaN, 0, 0), 105, 0, 15, 3));
        Assert.False(VisualCadence.ShouldSkip(state, new Vector3(10.1f, 0, 0), 99, 0, 15, 3));
        Assert.True(VisualCadence.ShouldSkip(state, new Vector3(11, 0, 0), 105, 1000, 15, 3)); // 2 m at 1 km altitude.
        Assert.False(VisualCadence.ShouldSkip(new VisualCadence.Traversal(), Vector3.zero, 105, 0, 15, 3));
    }

    [Fact]
    public void TerrainTranspilerGuardsExactlyTheVisualTraversalPair()
    {
        var source = VisualCadence.TerrainTarget;
        var original = ManagedMethod.Instructions(source);
        var changed = VisualCadence.Transpile(original).ToList();
        Assert.Equal(original.Count + 1, changed.Count);
        Assert.DoesNotContain(changed, c => c.Calls(VisualCadence.NativeBefore) || c.Calls(VisualCadence.NativeTraverse));
        Assert.Single(changed, c => c.operand is MethodInfo m && m.Name == "GuardedBefore");
        Assert.Single(changed, c => c.operand is MethodInfo m && m.Name == "GuardedTraverse");
        Assert.NotNull(ManagedMethod.Clone(source, (codes, _) => VisualCadence.Transpile(codes)));
    }

    [Fact]
    public void SignalCableCapStillAnimatesGlowFromLiveCableValues()
    {
        var calls = ManagedMethod.Instructions(AccessTools.Method(typeof(VisualCadence), nameof(VisualCadence.BeforeSignalCables)))
            .Select(i => i.operand).OfType<MethodInfo>().Select(m => m.Name).ToArray();
        Assert.Contains("UpdateGlow", calls); Assert.Contains("TryGetSignalCableValue", calls);
        Assert.DoesNotContain("UpdateGroupSignalValue", calls);
    }

    [Fact]
    public void AtmosphereAndCloudPassesDeclareDepthOnlyWhileContoursAreReduced()
    {
        // These are the exact private fields the guarded module binds.
        Assert.NotNull(NativeAccess.Read<FullscreenAtmosphereFeature, FullscreenAtmospherePass>("_atmospherePass"));
        Assert.NotNull(NativeAccess.Read<FullscreenCloudsFeature, FullscreenCloudsRenderPass>("_cloudsRenderPass"));
        Assert.NotNull(NativeAccess.Read<FullscreenCloudsFeature, FullscreenCloudsDepthMaskPass>("_depthMaskPass"));
        Assert.NotNull(NativeAccess.Read<FullscreenCloudsFeature, FullscreenCloudsCopyBackPass>("_copyBackPass"));
        var input = AccessTools.Property(typeof(ScriptableRenderPass), "input");
        var pass = (ScriptableRenderPass)RuntimeHelpers.GetUninitializedObject(typeof(FullscreenAtmospherePass));
        try
        {
            SceneOutlines.Required = true; SceneOutlines.Declare(pass);
            Assert.Equal(ScriptableRenderPassInput.Depth, input.GetValue(pass));
            SceneOutlines.Required = false; SceneOutlines.Declare(pass);
            Assert.Equal(ScriptableRenderPassInput.None, input.GetValue(pass));
            SceneOutlines.Declare(null);
        }
        finally { SceneOutlines.Required = false; }
    }

    [Theory]
    [InlineData(typeof(FullscreenAtmospherePass))]
    [InlineData(typeof(FullscreenCloudsRenderPass))]
    [InlineData(typeof(FullscreenCloudsDepthMaskPass))]
    [InlineData(typeof(FullscreenCloudsCopyBackPass))]
    public void NativeRendererCopiesDepthBeforeSkyEffectsWithoutRestoringNormalsPrepass(Type passType)
    {
        // Exercise the inspected URP scheduler, including the defective late-copy
        // control, without constructing a renderer or installing runtime detours.
        var inputs = ManagedMethod.Clone(AccessTools.Method(typeof(UniversalRenderer), "GetRenderPassInputs"), (codes, _) => codes);
        var schedule = ManagedMethod.Clone(AccessTools.Method(typeof(UniversalRenderer), "CalculateDepthCopySchedule"), (codes, _) => codes);
        var renderer = RuntimeHelpers.GetUninitializedObject(typeof(UniversalRenderer));
        var copyMode = AccessTools.Field(typeof(UniversalRenderer), "m_CopyDepthMode");
        copyMode.SetValue(renderer, Enum.Parse(copyMode.FieldType, "AfterOpaques"));
        var pass = (ScriptableRenderPass)RuntimeHelpers.GetUninitializedObject(passType);
        pass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        try
        {
            foreach (var reduced in new[] { false, true, false, true, false })
            {
                SceneOutlines.Required = reduced;
                SceneOutlines.Declare(pass);
                var summary = inputs.DynamicInvoke(false, false, false, false, new List<ScriptableRenderPass> { pass }, null)!;
                object Read(string field) => AccessTools.Field(summary.GetType(), field).GetValue(summary)!;
                Assert.Equal(reduced, (bool)Read("requiresDepthTexture"));
                Assert.False((bool)Read("requiresNormalsTexture"));
                Assert.False((bool)Read("requiresDepthPrepass"));
                var earliest = (RenderPassEvent)Read("requiresDepthTextureEarliestEvent");
                Assert.Equal(reduced ? RenderPassEvent.AfterRenderingOpaques : RenderPassEvent.BeforeRenderingPostProcessing, earliest);
                Assert.Equal(reduced ? "AfterOpaques" : "AfterSkybox", schedule.DynamicInvoke(renderer, earliest, false)!.ToString());
            }
        }
        finally { SceneOutlines.Required = false; SceneOutlines.Declare(pass); }
    }

    [Fact]
    public void ContoursChangeOnlyThroughTheDepthDeclaringModule()
    {
        // Any path that turns contours off must also declare depth, or the sky overdraws geometry.
        var discover = ManagedMethod.Instructions(AccessTools.Method(typeof(SceneOutlines), nameof(SceneOutlines.Discover)));
        var required = discover.FindIndex(i => i.operand is FieldInfo f && f.Name == nameof(SceneOutlines.Required));
        var disable = discover.FindIndex(i => i.operand is MethodInfo m && m.Name == "SetActive");
        Assert.InRange(required, 0, disable);
        foreach (var type in new[] { typeof(VisualTuning), typeof(Presets), typeof(VisualDetail), typeof(CanvasCulling) })
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Where(m => m.GetMethodBody() != null))
                Assert.DoesNotContain(ManagedMethod.Instructions(method), i => i.operand is MethodInfo m && m.Name == "SetActive" && typeof(ScriptableRendererFeature).IsAssignableFrom(m.DeclaringType));
    }
}
