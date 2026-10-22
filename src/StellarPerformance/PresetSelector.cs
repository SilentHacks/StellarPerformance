using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Services;
using FishNet;
using Players.Interface.Services;
using Players.Services;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace StellarPerformance;

internal static class PresetSelector
{
    private static readonly string[] Names = { "Faithful", "Balanced", "Performance", "Aggressive" };
    private static readonly string[] Captions = { "Native room maths", "Default", "Lighter effects", "Lowest cost visuals" };
    private static readonly Color Background = new(.055f, .085f, .11f, .99f), Surface = new(.10f, .14f, .18f);
    private static readonly Color Border = new(.19f, .25f, .30f), Accent = new(.35f, .82f, .91f), Muted = new(.78f, .83f, .87f);
    private static readonly Dictionary<string, string> Inputs = new();
    internal static bool Visible { get; private set; }
    private static bool Enabled, OwnPause;
    private static int LastToggleFrame = -1;
    private static GameObject? Blocker;
    private static GameObject? PreviousSelection;
    private static string Selection = "Balanced";
    private static string InputProblem = "";
    private static PresetPolicy Custom = PresetPolicy.Named("Balanced");
    private static GUIStyle Title = null!, Label = null!, Small = null!, CardTitle = null!, Button = null!, Field = null!, Track = null!, Thumb = null!;

    internal static void Initialize()
    {
        Enabled = Compatibility.Validate(new[] { "Players", "UI", "UI.Interface", "Unity.InputSystem", "UnityEngine.IMGUIModule" }, out var reason);
        Presets.Log?.Invoke(Enabled ? "Preset selector ready: F8, or Performance in a menu." : "Preset selector unavailable: " + reason);
    }
    private static IPlayerPauseStateProxy? Pause => ServiceLocator.GetService<IPlayerPauseStateProxy>();
    internal static void Update()
    {
        if (!Enabled) return;
        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
            Toggle();
        if (Visible && SceneManager.GetActiveScene().name == "MainGame" && Pause?.IsPaused != true) Close();
    }
    private static void Toggle()
    {
        if (LastToggleFrame == Time.frameCount) return;
        LastToggleFrame = Time.frameCount;
        if (Visible) Close(); else Open();
    }
    internal static void Open()
    {
        if (!Enabled || Visible) return;
        if (SceneManager.GetActiveScene().name == "MainGame" && Pause?.IsPaused != true)
        {
            var toggler = UnityEngine.Object.FindFirstObjectByType<PlayerPauseToggler>();
            if (toggler == null) return;
            toggler.Pause(); OwnPause = true;
        }
        Selection = Presets.Current.Name; Custom = Presets.Current;
        Inputs.Clear(); InputProblem = "";
        if (EventSystem.current != null) { PreviousSelection = EventSystem.current.currentSelectedGameObject; EventSystem.current.SetSelectedGameObject(null); }
        Blocker = new GameObject("Stellar Performance selector", typeof(Canvas), typeof(GraphicRaycaster));
        var canvas = Blocker.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = short.MaxValue;
        var shade = new GameObject("Modal input shield", typeof(RectTransform), typeof(Image));
        shade.transform.SetParent(Blocker.transform, false);
        var rect = shade.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        shade.GetComponent<Image>().color = new Color(0, 0, 0, .65f);
        Visible = true;
    }
    internal static void Close()
    {
        Visible = false;
        if (Blocker != null) { Blocker.SetActive(false); UnityEngine.Object.Destroy(Blocker); Blocker = null; }
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null && PreviousSelection != null && PreviousSelection.activeInHierarchy)
            EventSystem.current.SetSelectedGameObject(PreviousSelection);
        PreviousSelection = null;
        var owned = OwnPause; OwnPause = false;
        if (owned && Pause?.IsPaused == true) UnityEngine.Object.FindFirstObjectByType<PlayerPauseToggler>()?.Unpause();
    }
    internal static void Draw()
    {
        if (!Enabled) return;
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F8)
        { Toggle(); Event.current.Use(); }
        if (Visible && Event.current.type == EventType.KeyDown)
        {
            var key = Event.current.keyCode;
            var editing = GUI.GetNameOfFocusedControl().StartsWith("sp.value.", StringComparison.Ordinal);
            if (!editing && key >= KeyCode.Alpha1 && key <= KeyCode.Alpha4) { Select(Names[key - KeyCode.Alpha1]); Event.current.Use(); }
            else if (!editing && key == KeyCode.Alpha5) { Select("Custom"); Event.current.Use(); }
            else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            { Apply(); Event.current.Use(); }
            else if (key == KeyCode.Escape) { Close(); Event.current.Use(); }
        }
        var oldMatrix = GUI.matrix; var oldDepth = GUI.depth; var oldColor = GUI.color; var oldEnabled = GUI.enabled;
        var scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1)); GUI.depth = -10000;
        var width = Screen.width / scale; var height = Screen.height / scale;
        try
        {
            if (!Visible)
            {
                if (Cursor.visible)
                {
                    EnsureStyles();
                    if (DrawButton(new Rect(width - 220, 18, 200, 38), "Performance · F8", "open", false)) Open();
                }
                return;
            }
            EnsureStyles();
            var area = new Rect((width - 820) / 2, (height - 672) / 2, 820, 672);
            Fill(new Rect(area.x + 5, area.y + 8, area.width, area.height), new Color(0, 0, 0, .3f));
            Fill(area, Border); Fill(new Rect(area.x + 1, area.y + 1, area.width - 2, area.height - 2), Background);
            GUI.BeginGroup(area);
            try { DrawPanel(); }
            finally { GUI.EndGroup(); }
            if (Event.current.type == EventType.Repaint && !string.IsNullOrEmpty(GUI.tooltip))
            {
                var content = new GUIContent(GUI.tooltip);
                var tooltipHeight = Small.CalcHeight(content, 310) + 20;
                var mouse = Event.current.mousePosition;
                var tooltip = new Rect(Mathf.Clamp(mouse.x + 16, 8, width - 338), Mathf.Clamp(mouse.y + 22, 8, height - tooltipHeight - 8), 330, tooltipHeight);
                Fill(tooltip, Border); Fill(new Rect(tooltip.x + 1, tooltip.y + 1, tooltip.width - 2, tooltip.height - 2), Background);
                GUI.Label(new Rect(tooltip.x + 10, tooltip.y + 10, 310, tooltipHeight - 20), content, Small);
            }
        }
        finally { GUI.enabled = oldEnabled; GUI.matrix = oldMatrix; GUI.depth = oldDepth; GUI.color = oldColor; }
    }

    private static void DrawPanel()
    {
        InputProblem = "";
        GUI.Label(new Rect(26, 22, 600, 34), "Stellar Performance", Title);
        if (DrawButton(new Rect(756, 20, 38, 34), "×", "close", false, "Close · Esc")) Close();
        for (var i = 0; i < Names.Length; i++)
        {
            var card = new Rect(26 + i * 194, 66, 186, 64);
            var selected = Selection == Names[i];
            if (DrawButton(card, "", "preset." + i, selected, (i + 1) + " · " + PresetPolicy.Description(Names[i]))) Select(Names[i]);
            if (selected) Fill(new Rect(card.x, card.y, card.width, 3), Accent);
            Text(new Rect(card.x + 13, card.y + 12, 164, 24), Names[i], CardTitle, selected ? Accent : Color.white);
            Text(new Rect(card.x + 13, card.y + 40, 164, 20), Captions[i], Small, Muted);
        }
        if (Selection == "Custom") Text(new Rect(618, 136, 176, 22), "Custom", Small, Accent);
        var p = Selection == "Custom" ? Custom : PresetPolicy.Named(Selection);
        var previousEnabled = GUI.enabled;
        var client = InstanceFinder.IsClientStarted && !InstanceFinder.IsServerStarted;
        float Row(int index) => 160 + index * 34;
        RowLabel(Row(0), "Room calculation", "Incremental rooms can introduce small gas rounding differences. In multiplayer the host determines room calculations; this preference is used when you host.");
        if (client) Text(new Rect(192, Row(0) + 5, 84, 24), "Host", Small, Muted);
        else if (!Presets.RoomsAvailable) Text(new Rect(192, Row(0) + 5, 84, 24), "Unavailable", Small, Muted);
        GUI.enabled = previousEnabled && Presets.RoomsAvailable;
        var rooms = Choice(Row(0), p.Rooms ? 1 : 0, "rooms", "Native", "Incremental") == 1;
        GUI.enabled = previousEnabled;
        if (!VisualTuning.Available) Text(new Rect(590, 136, 204, 18), "Visual controls unavailable", Small, Muted);
        GUI.enabled = previousEnabled && SceneOutlines.Available;
        RowLabel(Row(1), "Scene outlines", "Main-view decorative contours. Silhouettes keep object outlines but skip the crease pass and its geometry prepass. Hover, selection and build-tool outlines are retained.");
        var outlines = Choice(Row(1), p.Outlines, "outlines", "Native", "Silhouettes", "Off");
        GUI.enabled = previousEnabled && VisualTuning.Available;
        RowLabel(Row(2), "Shadows", "Game retains native filtering. Hard removes soft-shadow filtering. Off disables shadow drawing.");
        var quality = Choice(Row(2), p.ShadowQuality, "shadow-quality", "Game", "Hard", "Off");
        GUI.enabled &= quality != 2;
        RowLabel(Row(3), "Shadow distance", "Maximum shadow distance. Never increases the game's current setting. Game restores the original setting.");
        var distance = Number(Row(3), p.ShadowDistance, 0, 5000, "distance", "Game", "m");
        RowLabel(Row(4), "Shadow cascades", "Maximum cascade count. Never increases the game's current setting.");
        var cascadeIndex = p.Cascades == 0 ? 0 : p.Cascades == 1 ? 1 : p.Cascades == 2 ? 2 : 3;
        var cascades = Choice(Row(4), cascadeIndex, "cascades", "Game", "1", "2", "4");
        cascades = cascades == 3 ? 4 : cascades;
        RowLabel(Row(5), "Shadow resolution", "Shadow-map resolution ceiling. Lower values produce less detailed shadows. Game restores native resolution.");
        var resolutions = new[] { 0, 256, 512, 1024, 2048, 4096 };
        var resolution = resolutions[Choice(Row(5), Array.IndexOf(resolutions, p.ShadowResolution), "shadow-resolution", "Game", "256", "512", "1024", "2048", "4096")];
        GUI.enabled = previousEnabled && VisualDetail.Available;
        RowLabel(Row(6), "Vegetation range", "Visual vegetation search range. Native resource placement, collision and sound ranges are retained. Nearby coverage stays at least 20 m where native range permits.");
        var vegetation = Percent(Row(6), p.VegetationRange, 10, "vegetation");
        GUI.enabled = previousEnabled && VisualTuning.Available;
        RowLabel(Row(7), "Exhaust density", "Thruster-flame emission only. Thrust, fuel, sound, particle size and speed are unchanged.");
        var exhaust = Percent(Row(7), p.ExhaustDensity, 10, "exhaust");
        RowLabel(Row(8), "Readout refresh", "Passive signal and oxygen text refresh rate. Focused readouts stay native; the latest value is always shown at the next refresh.");
        var hz = (int)Number(Row(8), p.ReadoutHz, 0, 60, "hz", "Native", "Hz");
        GUI.enabled = previousEnabled && VisualCadence.Available;
        RowLabel(Row(9), "Cable effects", "Signal glow and fluid flow targets. Animation still runs every frame; circuits, fluids and simulation are unaffected.");
        var cableHz = (int)Number(Row(9), p.CableHz, 0, 60, "cables", "Native", "Hz");
        RowLabel(Row(10), "Terrain detail reuse", "While the camera is nearly still, reuse the visual terrain and vegetation detail search for up to this many frames. Any movement refreshes it.");
        var terrain = (int)Number(Row(10), p.TerrainFrames, 0, 30, "terrain", "Native", "fr");
        GUI.enabled = previousEnabled && VisualTuning.Available;
        RowLabel(Row(11), "Hide small labels", "Hide passive labels beyond 20 m whose text would be smaller than this on screen. Nearby and focused labels remain visible.");
        var pixels = (int)Number(Row(11), p.LabelPixels, 0, 32, "pixels", "Off", "px");
        GUI.enabled = previousEnabled && CanvasCulling.Enabled;
        RowLabel(Row(12), "Part label distance", "Hide inspected part labels and buttons beyond this distance. Focused controls remain visible; picking and simulation stay live.");
        var labels = Number(Row(12), p.LabelDistance, 0, 2000, "labels", "Native", "m");
        GUI.enabled = previousEnabled;
        var edited = new PresetPolicy("Custom", rooms, outlines, hz, pixels, distance, cascades, exhaust, labels, resolution, quality, vegetation, cableHz, terrain);
        if (!SameSettings(p, edited)) { Selection = "Custom"; Custom = edited; }
        Fill(new Rect(26, 606, 768, 1), Border);
        var status = Presets.Pending ? "Applying…" : "Active · " + Presets.Current.Name;
        var problem = Presets.Status.StartsWith("Change failed", StringComparison.Ordinal) || Presets.Status.Contains("Could not save preferences:");
        if (problem) status = Presets.Status.Replace(" active. No restart required.", ".");
        if (InputProblem.Length > 0) { status = InputProblem; problem = true; }
        Text(new Rect(26, 617, 570, 42), status, Small, problem ? new Color(1, .68f, .43f) : Muted);
        GUI.enabled = previousEnabled && !Presets.Pending && Dirty && InputProblem.Length == 0;
        if (DrawButton(new Rect(650, 618, 144, 36), "Apply", "apply", true, "Enter")) Apply();
        GUI.enabled = previousEnabled;
    }

    private static bool Dirty => Selection != Presets.Current.Name || !SameSettings(Selection == "Custom" ? Custom : PresetPolicy.Named(Selection), Presets.Current);
    internal static bool SameSettings(PresetPolicy a, PresetPolicy b) => a.Rooms == b.Rooms && a.Outlines == b.Outlines && a.ReadoutHz == b.ReadoutHz && a.LabelPixels == b.LabelPixels && a.ShadowDistance == b.ShadowDistance && a.Cascades == b.Cascades && a.ExhaustDensity == b.ExhaustDensity && a.LabelDistance == b.LabelDistance && a.ShadowResolution == b.ShadowResolution && a.ShadowQuality == b.ShadowQuality && a.VegetationRange == b.VegetationRange && a.CableHz == b.CableHz && a.TerrainFrames == b.TerrainFrames;
    private static void Apply()
    {
        if (!Dirty || Presets.Pending || InputProblem.Length > 0) return;
        GUI.FocusControl(null);
        if (Selection == "Custom") Presets.Request(Custom); else Presets.Request(Selection);
    }
    private static void Select(string name)
    {
        if (name == "Custom" && Selection != "Custom")
        {
            var p = PresetPolicy.Named(Selection);
            Custom = new PresetPolicy("Custom", p.Rooms, p.Outlines, p.ReadoutHz, p.LabelPixels, p.ShadowDistance, p.Cascades, p.ExhaustDensity, p.LabelDistance, p.ShadowResolution, p.ShadowQuality, p.VegetationRange, p.CableHz, p.TerrainFrames);
        }
        Selection = name;
        Inputs.Clear(); InputProblem = ""; GUI.FocusControl(null);
    }
    private static void RowLabel(float y, string label, string hint) => GUI.Label(new Rect(26, y, 242, 30), new GUIContent(label, hint), Label);
    private static int Choice(float y, int selected, string id, params string[] options)
    {
        var width = (504 - 6 * (options.Length - 1)) / (float)options.Length;
        for (var i = 0; i < options.Length; i++)
            if (DrawButton(new Rect(290 + i * (width + 6), y, width, 30), options[i], id + i, i == selected)) selected = i;
        return selected;
    }
    private static float Number(float y, float value, float min, float max, string id, string? zero, string unit)
    {
        var sliderX = 290f;
        if (zero != null)
        {
            if (DrawButton(new Rect(sliderX, y, 86, 30), zero, id + ".native", value == 0)) { value = 0; Inputs.Remove(id); GUI.FocusControl(null); }
            sliderX += 102;
        }
        GUI.SetNextControlName("sp.slider." + id);
        // A cubic distance scale keeps useful short ranges easy to reach.
        var distanceControl = id == "distance" || id == "labels";
        var position = distanceControl ? Mathf.Pow(value / max, 1f / 3) : value;
        var moved = GUI.HorizontalSlider(new Rect(sliderX, y + 10, 670 - sliderX, 18), position, min, distanceControl ? 1 : max, Track, Thumb);
        if (moved != position) { value = distanceControl ? Mathf.Round(Mathf.Pow(moved, 3) * max) : Mathf.Round(moved); if (id == "labels" && value > 0) value = Mathf.Max(4, value); Inputs.Remove(id); }
        var control = "sp.value." + id;
        var focused = GUI.GetNameOfFocusedControl() == control;
        if (!focused || !Inputs.ContainsKey(id) || Inputs[id] == "—") Inputs[id] = value == 0 && !focused ? "—" : value.ToString("0.##", CultureInfo.InvariantCulture);
        Fill(new Rect(690, y, 76, 30), focused ? Accent : Surface);
        Fill(new Rect(691, y + 1, 74, 28), Surface);
        GUI.SetNextControlName(control);
        var input = GUI.TextField(new Rect(691, y + 1, 74, 28), Inputs[id], 8, Field);
        var valid = float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && !float.IsNaN(parsed) && !float.IsInfinity(parsed) && parsed >= min && parsed <= max && (distanceControl || parsed == Mathf.Round(parsed)) && (id != "labels" || parsed == 0 || parsed >= 4);
        if (input != Inputs[id])
        {
            Inputs[id] = input;
            if (valid) value = parsed;
        }
        if (focused && !valid) InputProblem = id == "labels" ? "Use Native, or 4–2000 m." : "Enter " + min + "–" + max + " " + unit + ".";
        if (value != 0 || focused) Text(new Rect(772, y + 5, 26, 24), unit, Small, Muted);
        return value;
    }
    private static float Percent(float y, float value, int min, string id)
    {
        var percent = Number(y, value * 100, min, 100, id, null, "%");
        return percent == value * 100 ? value : percent / 100;
    }
    private static bool DrawButton(Rect rect, string text, string id, bool selected, string hint = "")
    {
        var focused = GUI.GetNameOfFocusedControl() == "sp.button." + id;
        var hover = GUI.enabled && rect.Contains(Event.current.mousePosition);
        Fill(rect, GUI.enabled && (selected || focused) ? Accent : Border);
        Fill(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), selected ? new Color(.12f, .25f, .30f) : hover ? new Color(.16f, .21f, .26f) : Surface);
        GUI.SetNextControlName("sp.button." + id);
        return GUI.Button(rect, new GUIContent(text, hint), Button);
    }
    private static void Fill(Rect rect, Color color)
    {
        var previous = GUI.color;
        GUI.color = GUI.enabled ? color : new Color(color.r, color.g, color.b, color.a * .4f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }
    private static void Text(Rect rect, string text, GUIStyle style, Color color)
    {
        var previous = GUI.color; GUI.color = color; GUI.Label(rect, text, style); GUI.color = previous;
    }
    private static void EnsureStyles()
    {
        if (Title != null) return;
        Title = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
        Label = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
        Small = new GUIStyle(Label) { fontSize = 12, wordWrap = true };
        CardTitle = new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold };
        Button = new GUIStyle(GUI.skin.button) { fontSize = 14 };
        Field = new GUIStyle(GUI.skin.textField) { fontSize = 14, alignment = TextAnchor.MiddleRight, padding = new RectOffset(8, 8, 0, 0) };
        foreach (var style in new[] { Title, Label, Small, CardTitle, Button, Field })
        {
            foreach (var state in new[] { style.normal, style.hover, style.active, style.focused, style.onNormal, style.onHover, style.onActive, style.onFocused })
            { state.background = null; state.textColor = Color.white; }
        }
        Track = new GUIStyle(GUI.skin.horizontalSlider) { fixedHeight = 4, margin = new RectOffset(), padding = new RectOffset() };
        Track.normal.background = Texture2D.whiteTexture;
        Thumb = new GUIStyle(GUI.skin.horizontalSliderThumb) { fixedWidth = 12, fixedHeight = 16 };
    }
}
