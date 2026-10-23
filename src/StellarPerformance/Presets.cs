using System;
using MelonLoader;

namespace StellarPerformance;

// One immutable policy is published on the main thread between complete updates.
internal sealed class PresetPolicy
{
    internal const int OutlinesNative = 0, OutlinesSilhouette = 1, OutlinesOff = 2;
    internal readonly string Name;
    internal readonly bool Rooms;
    internal readonly int Outlines;
    internal readonly int ReadoutHz, LabelPixels, Cascades;
    internal readonly float ShadowDistance, ExhaustDensity;
    internal readonly float LabelDistance, VegetationRange;
    internal readonly int ShadowResolution, ShadowQuality;
    internal readonly int CableHz, TerrainFrames;
    internal PresetPolicy(string name, bool rooms, int outlines, int readoutHz, int labelPixels, float shadowDistance, int cascades, float exhaustDensity,
        float labelDistance = 0, int shadowResolution = 0, int shadowQuality = 0, float vegetationRange = 1, int cableHz = 0, int terrainFrames = 0)
    {
        Name = name; Rooms = rooms;
        Outlines = outlines == OutlinesSilhouette || outlines == OutlinesOff ? outlines : OutlinesNative;
        ReadoutHz = readoutHz == 0 ? 0 : Math.Max(1, Math.Min(60, readoutHz));
        LabelPixels = Math.Max(0, Math.Min(32, labelPixels));
        ShadowDistance = float.IsNaN(shadowDistance) || float.IsInfinity(shadowDistance) ? 0 : Math.Max(0, Math.Min(5000, shadowDistance));
        Cascades = cascades == 1 || cascades == 2 || cascades == 4 ? cascades : 0;
        ExhaustDensity = float.IsNaN(exhaustDensity) || float.IsInfinity(exhaustDensity) ? 1 : Math.Max(.1f, Math.Min(1, exhaustDensity));
        LabelDistance = float.IsNaN(labelDistance) || float.IsInfinity(labelDistance) || labelDistance <= 0 ? 0 : Math.Max(4, Math.Min(2000, labelDistance));
        ShadowResolution = shadowResolution <= 0 ? 0 : shadowResolution >= 4096 ? 4096 : shadowResolution >= 2048 ? 2048 : shadowResolution >= 1024 ? 1024 : shadowResolution >= 512 ? 512 : 256;
        ShadowQuality = shadowQuality == 1 || shadowQuality == 2 ? shadowQuality : 0;
        VegetationRange = float.IsNaN(vegetationRange) || float.IsInfinity(vegetationRange) ? 1 : Math.Max(.1f, Math.Min(1, vegetationRange));
        CableHz = cableHz <= 0 ? 0 : Math.Max(5, Math.Min(60, cableHz));
        TerrainFrames = terrainFrames <= 1 ? 0 : Math.Min(30, terrainFrames);
    }
    internal static PresetPolicy Named(string name)
    {
        switch (name)
        {
            case "Faithful": return new PresetPolicy(name, false, OutlinesNative, 0, 0, 0, 0, 1);
            case "Balanced": return new PresetPolicy(name, true, OutlinesNative, 0, 0, 0, 0, 1);
            // Measured 2026-10-22: cable and terrain caps carry most of the CPU gain; contours,
            // vegetation and shadows reduce GPU work. 1024 hard shadows streaked lit walls and
            // readout caps showed no gain, so neither is used by a named level.
            case "Performance": return new PresetPolicy(name, true, OutlinesSilhouette, 0, 0, 150, 2, 1, 0, 2048, 0, .75f, 30, 6);
            case "Aggressive": return new PresetPolicy(name, true, OutlinesOff, 0, 0, 100, 2, .5f, 0, 2048, 0, .5f, 15, 15);
            default: throw new ArgumentException("Unknown preset: " + name);
        }
    }
    internal static string Description(string name)
    {
        switch (name)
        {
            case "Faithful": return "Native room reconstruction, gas arithmetic and presentation. Keep your selected CPU optimisations.";
            case "Balanced": return "Recommended. Native presentation and live values, with your selected CPU optimisations. Incremental rooms can introduce small gas rounding differences.";
            case "Performance": return "Silhouette outlines, 150 m shadows, 75% vegetation range. Cable glow/flow refreshes at 30 Hz and terrain detail is reused while still. Simulation stays native.";
            case "Aggressive": return "No scene contours, 100 m shadows, 50% vegetation range, half exhaust. Cable effects refresh at 15 Hz and terrain detail is reused longer. Simulation stays native.";
            default: return "Your room and presentation settings. Focused controls and interaction feedback stay native. Simulation and genuine network updates stay live.";
        }
    }
}

internal static class Presets
{
    internal static PresetPolicy Current { get; private set; } = PresetPolicy.Named("Balanced");
    internal static string Status = "";
    internal static bool Pending => _pending != null;
    internal static bool RoomsAvailable;
    internal static Action<string>? Log;
    private static PresetPolicy? _pending;
    private static bool _persist;
    private static MelonPreferences_Category _category = null!;
    private static MelonPreferences_Entry<string> _name = null!;
    private static MelonPreferences_Entry<bool> _rooms = null!;
    private static MelonPreferences_Entry<int> _outlines = null!;
    private static MelonPreferences_Entry<int> _hz = null!, _pixels = null!, _cascades = null!;
    private static MelonPreferences_Entry<float> _distance = null!, _exhaust = null!;
    private static MelonPreferences_Entry<float> _labelDistance = null!, _vegetation = null!;
    private static MelonPreferences_Entry<int> _shadowResolution = null!, _shadowQuality = null!;
    private static MelonPreferences_Entry<int> _cableHz = null!, _terrainFrames = null!;

    internal static void Initialize(MelonPreferences_Category category, MelonPreferences_Entry<bool> rooms, bool legacyCustom)
    {
        _category = category; _rooms = rooms;
        _name = category.CreateEntry("Preset", legacyCustom ? "Custom" : "Balanced", description: "Faithful, Balanced, Performance, Aggressive or Custom. Change live through the Performance panel (F8).");
        _outlines = category.CreateEntry("OutlineMode", 0, description: "Main-view scene contours: 0 native, 1 silhouettes only (no depth-normals prepass), 2 off. Hover/selection and build-tool outlines are retained.");
        _hz = category.CreateEntry("ReadoutHz", 0, description: "Unfocused passive signal/oxygen text refresh: 0 native, otherwise 1-60 Hz. Focused displays stay native.");
        _pixels = category.CreateEntry("MinimumLabelPixels", 0, description: "Hide inspected passive labels below this projected text height beyond 20 m. 0 keeps native visibility.");
        _distance = category.CreateEntry("PresetShadowDistance", 0f, description: "Optional shadow distance ceiling in metres; 0 retains the game's setting. Never increases native distance.");
        _cascades = category.CreateEntry("PresetShadowCascades", 0, description: "Optional main-light cascade ceiling: 0 native, 1, 2 or 4. Never increases native quality.");
        _exhaust = category.CreateEntry("ExhaustDensity", 1f, description: "Inspected thruster-flame emission multiplier, 0.1-1. No thrust, fuel, sound, speed or particle-size changes.");
        _labelDistance = category.CreateEntry("PartLabelDistance", 0f, description: "Inspected part-canvas distance ceiling in metres. 0 native; 4-2000 otherwise. Focused controls stay visible.");
        _shadowResolution = category.CreateEntry("PresetShadowResolution", 0, description: "Shadow-map resolution ceiling: 0 native, 256, 512, 1024, 2048 or 4096.");
        _shadowQuality = category.CreateEntry("PresetShadowQuality", 0, description: "0 native shadow filtering, 1 hard shadows, 2 shadows off.");
        _vegetation = category.CreateEntry("VegetationVisualRange", 1f, description: "Visual vegetation search-range multiplier, 0.1-1. Does not reduce native collision, resources or sound ranges.");
        _cableHz = category.CreateEntry("CableEffectHz", 0, description: "Cosmetic cable glow/flow targets: 0 native, otherwise 5-60 Hz. Animation still advances every frame; circuits and fluids are unaffected.");
        _terrainFrames = category.CreateEntry("TerrainDetailFrames", 0, description: "Reuse the visual terrain/vegetation detail search while the camera is nearly still, for up to this many frames: 0 native, 2-30.");
        var custom = new PresetPolicy("Custom", rooms.Value, _outlines.Value, _hz.Value, _pixels.Value, _distance.Value, _cascades.Value, _exhaust.Value,
            _labelDistance.Value, _shadowResolution.Value, _shadowQuality.Value, _vegetation.Value, _cableHz.Value, _terrainFrames.Value);
        PresetPolicy initial;
        try { initial = _name.Value == "Custom" ? custom : PresetPolicy.Named(_name.Value); }
        catch (ArgumentException) { initial = custom; }
        Current = initial;
    }
    internal static void Request(string name, bool persist = true) => Request(PresetPolicy.Named(name), persist);
    internal static void Request(PresetPolicy policy, bool persist = true)
    {
        _pending = policy; _persist = persist; Status = "Applying " + policy.Name + "…";
    }
    internal static void ApplyPending()
    {
        if (_pending == null || (RoomsAvailable && IncrementalRooms.Busy)) return;
        var next = _pending; _pending = null;
        var previous = Current;
        try
        {
            ApplyPresentation(next);
            if (RoomsAvailable) IncrementalRooms.Enabled = next.Rooms;
            Current = next;
        }
        catch (Exception error)
        {
            ApplyPresentation(previous);
            Status = "Change failed; previous preset retained: " + error.GetBaseException().Message;
            Log?.Invoke(Status); return;
        }
        Status = next.Name + " active. No restart required.";
        if (_persist)
        {
            // Persistence is separate from the applied policy: a disk error must
            // not silently undo the user's already-visible runtime change.
            try
            {
                _name.Value = next.Name; _rooms.Value = next.Rooms; _outlines.Value = next.Outlines;
                _hz.Value = next.ReadoutHz; _pixels.Value = next.LabelPixels; _distance.Value = next.ShadowDistance;
                _cascades.Value = next.Cascades; _exhaust.Value = next.ExhaustDensity;
                _labelDistance.Value = next.LabelDistance; _shadowResolution.Value = next.ShadowResolution; _shadowQuality.Value = next.ShadowQuality;
                _vegetation.Value = next.VegetationRange; _cableHz.Value = next.CableHz; _terrainFrames.Value = next.TerrainFrames;
                _category.SaveToFile(false);
            }
            catch (Exception error) { Status += " Could not save preferences: " + error.GetBaseException().Message; }
        }
        Log?.Invoke(Status);
    }
    internal static void ApplyPresentation(PresetPolicy policy)
    {
        if (VisualTuning.Available) VisualTuning.Change(policy);
        if (SceneOutlines.Available) SceneOutlines.Change(policy.Outlines);
        CanvasCulling.LabelDistance = policy.LabelDistance;
        if (VisualDetail.Available) VisualDetail.Change(policy);
        VisualCadence.Change(policy);
    }
}
