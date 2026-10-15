using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MelonLoader;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class EntryPointTests
{
    private static readonly Keyboard Device = (Keyboard)RuntimeHelpers.GetUninitializedObject(typeof(Keyboard));
    private static readonly KeyControl FunctionKey = (KeyControl)RuntimeHelpers.GetUninitializedObject(typeof(KeyControl));
    private static readonly ButtonControl Control = (ButtonControl)RuntimeHelpers.GetUninitializedObject(typeof(ButtonControl));
    private static readonly ButtonControl Shift = (ButtonControl)RuntimeHelpers.GetUninitializedObject(typeof(ButtonControl));
    private static bool _keyboard, _f8, _control, _shift, _active;
    private static int _keyboardReads, _updates, _starts, _stops, _messages, _reports;
    private static string? _lastReport;

    private static Keyboard? CurrentKeyboard() { _keyboardReads++; return _keyboard ? Device : null; }
    private static KeyControl F8(Keyboard device) => FunctionKey;
    private static ButtonControl Ctrl(Keyboard device) => Control;
    private static ButtonControl ShiftKey(Keyboard device) => Shift;
    private static bool PressedThisFrame(ButtonControl control) => _f8;
    private static bool IsPressed(ButtonControl control) => ReferenceEquals(control, Control) ? _control : _shift;
    private static bool Active() => _active;
    private static float Seconds(MelonPreferences_Entry<float> entry) => 30;
    private static void Start(double seconds, string label) { _starts++; _active = true; }
    private static string Stop() { _stops++; _active = false; return "stopped"; }
    private static string? Update() { _updates++; return null; }
    private static void Message(MelonLogger.Instance logger, string message) => _messages++;
    private static void Report(PerformancePlugin plugin, string? message) { if (message != null) { _reports++; _lastReport = message; } }

    private static readonly Action<PerformancePlugin> RunUpdate = (Action<PerformancePlugin>)ManagedMethod.Clone(
        typeof(PerformancePlugin).GetMethod(nameof(PerformancePlugin.OnUpdate))!, StubExternalCalls);

    private static IEnumerable<CodeInstruction> StubExternalCalls(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var replacements = new Dictionary<MethodInfo, MethodInfo>
        {
            [AccessTools.PropertyGetter(typeof(Keyboard), nameof(Keyboard.current))] = Method(nameof(CurrentKeyboard)),
            [AccessTools.PropertyGetter(typeof(Keyboard), nameof(Keyboard.f8Key))] = Method(nameof(F8)),
            [AccessTools.PropertyGetter(typeof(Keyboard), nameof(Keyboard.ctrlKey))] = Method(nameof(Ctrl)),
            [AccessTools.PropertyGetter(typeof(Keyboard), nameof(Keyboard.shiftKey))] = Method(nameof(ShiftKey)),
            [AccessTools.PropertyGetter(typeof(ButtonControl), nameof(ButtonControl.wasPressedThisFrame))] = Method(nameof(PressedThisFrame)),
            [AccessTools.PropertyGetter(typeof(ButtonControl), nameof(ButtonControl.isPressed))] = Method(nameof(IsPressed)),
            [AccessTools.PropertyGetter(typeof(Diagnostics), nameof(Diagnostics.Active))] = Method(nameof(Active)),
            [AccessTools.PropertyGetter(typeof(MelonPreferences_Entry<float>), nameof(MelonPreferences_Entry<float>.Value))] = Method(nameof(Seconds)),
            [AccessTools.Method(typeof(Diagnostics), nameof(Diagnostics.Start))] = Method(nameof(Start)),
            [AccessTools.Method(typeof(Diagnostics), nameof(Diagnostics.Stop))] = Method(nameof(Stop)),
            [AccessTools.Method(typeof(Diagnostics), nameof(Diagnostics.Update))] = Method(nameof(Update)),
            [AccessTools.Method(typeof(MelonLogger.Instance), nameof(MelonLogger.Instance.Msg), new[] { typeof(string) })] = Method(nameof(Message)),
            [AccessTools.Method(typeof(PerformancePlugin), "Report")] = Method(nameof(Report))
        };
        foreach (var original in instructions)
        {
            var code = new CodeInstruction(original);
            if (code.operand is MethodInfo called && replacements.TryGetValue(called, out var replacement))
            {
                code.opcode = OpCodes.Call;
                code.operand = replacement;
            }
            yield return code;
        }
    }

    private static MethodInfo Method(string name) => typeof(EntryPointTests).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    private static PerformancePlugin Reset(bool diagnostics, bool keyboard = true, bool f8 = false, bool control = false, bool shift = false, bool active = false)
    {
        _keyboard = keyboard; _f8 = f8; _control = control; _shift = shift; _active = active;
        _keyboardReads = _updates = _starts = _stops = _messages = _reports = 0;
        _lastReport = null;
        var plugin = (PerformancePlugin)RuntimeHelpers.GetUninitializedObject(typeof(PerformancePlugin));
        AccessTools.Field(typeof(PerformancePlugin), "_diagnostics").SetValue(plugin, diagnostics);
        AccessTools.Field(typeof(PerformancePlugin), "_installed").SetValue(plugin, new List<string>());
        return plugin;
    }

    [Fact]
    public void DisabledDiagnosticsNeverPollDevicesOrAdvanceCapture()
    {
        RunUpdate(Reset(false));
        Assert.Equal(0, _keyboardReads);
        Assert.Equal(0, _updates);
    }

    [Fact]
    public void MissingKeyboardStillAdvancesAnActiveCapture()
    {
        RunUpdate(Reset(true, keyboard: false, active: true));
        Assert.Equal(1, _keyboardReads);
        Assert.Equal(1, _updates);
        Assert.Equal(0, _starts);
        Assert.Equal(0, _stops);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void IncompleteChordDoesNotStartCapture(bool f8, bool control, bool shift)
    {
        RunUpdate(Reset(true, f8: f8, control: control, shift: shift));
        Assert.Equal(0, _starts);
        Assert.Equal(0, _stops);
        Assert.Equal(1, _updates);
    }

    [Fact]
    public void CompleteChordStartsOnceAndDoesNotRecordATinyFirstFrame()
    {
        var plugin = Reset(true, f8: true, control: true, shift: true);
        RunUpdate(plugin);
        Assert.Equal(1, _starts);
        Assert.Equal(1, _messages);
        Assert.Equal(0, _updates);
        _f8 = false;
        RunUpdate(plugin);
        Assert.Equal(1, _starts);
        Assert.Equal(1, _updates);
    }

    [Fact]
    public void CompleteChordStopsAndReportsAnExistingCapture()
    {
        RunUpdate(Reset(true, f8: true, control: true, shift: true, active: true));
        Assert.Equal(0, _starts);
        Assert.Equal(1, _stops);
        Assert.Equal("stopped", _lastReport);
        Assert.Equal(1, _reports);
        Assert.Equal(1, _updates);
    }
}
