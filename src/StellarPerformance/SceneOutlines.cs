using System;
using System.Collections.Generic;
using HarmonyLib;
using MK.EdgeDetection;
using MK.EdgeDetection.PostProcessing.Generic;
using MK.EdgeDetection.UniversalRendererFeatures;
using Rendering.Features;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace StellarPerformance;

// Main-view contour modes. Native contours request depth-normals, so URP runs a full
// geometry prepass before opaques. The native atmosphere and cloud passes sample that
// depth texture after opaques without declaring it; without the prepass URP copies depth
// after the skybox, too late, and sky is painted over geometry above the horizon.
// Silhouette/off modes therefore declare the passes' real depth dependency, which moves
// URP's (cheap) depth copy ahead of them. Build-tool and tutorial outlines stay native.
internal static class SceneOutlines
{
    static SceneOutlines() { }
    private const int Limit = 16;
    internal static bool Available;
    internal static bool Required;
    private static int Mode;
    private static Func<FullscreenAtmosphereFeature, FullscreenAtmospherePass> Atmosphere = null!;
    private static Func<FullscreenCloudsFeature, FullscreenCloudsRenderPass> Clouds = null!;
    private static Func<FullscreenCloudsFeature, FullscreenCloudsDepthMaskPass> Mask = null!;
    private static Func<FullscreenCloudsFeature, FullscreenCloudsCopyBackPass> CopyBack = null!;
    private sealed class Native
    {
        internal readonly bool Active;
        internal readonly BitmaskProperty<InputData> Input;
        internal Native(bool active, BitmaskProperty<InputData> input) { Active = active; Input = input; }
    }
    private static readonly Dictionary<MKEdgeDetectionRendererFeature, Native> Owned = new Dictionary<MKEdgeDetectionRendererFeature, Native>();

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        Atmosphere = NativeAccess.Read<FullscreenAtmosphereFeature, FullscreenAtmospherePass>("_atmospherePass");
        Clouds = NativeAccess.Read<FullscreenCloudsFeature, FullscreenCloudsRenderPass>("_cloudsRenderPass");
        Mask = NativeAccess.Read<FullscreenCloudsFeature, FullscreenCloudsDepthMaskPass>("_depthMaskPass");
        CopyBack = NativeAccess.Read<FullscreenCloudsFeature, FullscreenCloudsCopyBackPass>("_copyBackPass");
        harmony.Patch(AccessTools.Method(typeof(FullscreenAtmosphereFeature), nameof(FullscreenAtmosphereFeature.AddRenderPasses)), postfix: new HarmonyMethod(typeof(SceneOutlines), nameof(AfterAtmosphere)));
        harmony.Patch(AccessTools.Method(typeof(FullscreenCloudsFeature), nameof(FullscreenCloudsFeature.AddRenderPasses)), postfix: new HarmonyMethod(typeof(SceneOutlines), nameof(AfterClouds)));
        Available = true;
    }
    private static void AfterAtmosphere(FullscreenAtmosphereFeature __instance) => Declare(Atmosphere(__instance));
    private static void AfterClouds(FullscreenCloudsFeature __instance)
    {
        Declare(Clouds(__instance)); Declare(Mask(__instance)); Declare(CopyBack(__instance));
    }
    // The native passes declare no inputs, so None restores the original contract.
    internal static void Declare(ScriptableRenderPass? pass) => pass?.ConfigureInput(Required ? ScriptableRenderPassInput.Depth : ScriptableRenderPassInput.None);

    internal static void Change(int mode)
    {
        Restore();
        Mode = mode;
        Discover();
    }
    // Renderer data are pipeline assets; repeated discovery only adopts new instances.
    internal static void Discover()
    {
        if (!Available || Mode == PresetPolicy.OutlinesNative) return;
        foreach (var data in UnityEngine.Resources.FindObjectsOfTypeAll<ScriptableRendererData>())
        {
            if (data == null || data.name != "NearShaders") continue;
            foreach (var candidate in data.rendererFeatures)
            {
                if (!(candidate is MKEdgeDetectionRendererFeature feature) || Owned.ContainsKey(feature) || Owned.Count >= Limit) continue;
                var input = feature.inputData.value;
                var silhouette = input & ~InputData.Normal;
                if (Mode == PresetPolicy.OutlinesSilhouette && (silhouette & InputData.Depth) == 0) continue;
                // Declare depth before the contour change reaches the next camera render.
                Required = true;
                Owned.Add(feature, new Native(feature.isActive, feature.inputData));
                if (Mode == PresetPolicy.OutlinesOff) feature.SetActive(false);
                else feature.inputData = new BitmaskProperty<InputData>(silhouette);
            }
        }
    }
    internal static void Restore()
    {
        foreach (var pair in Owned)
        {
            if (pair.Key == null) continue;
            pair.Key.inputData = pair.Value.Input;
            pair.Key.SetActive(pair.Value.Active);
        }
        Owned.Clear(); Required = false;
    }
}
