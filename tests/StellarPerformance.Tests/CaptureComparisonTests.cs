using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class CaptureComparisonTests
{
    private static string Report(double frameMs, double fluidMs, int ships = 20, bool save = false, string allocation = "Main-thread allocated bytes=0", double? signalMs = null, string? version = null)
    {
        var capture = new CaptureData("modules=profile-server,profile-cables,profile-transport,profile-save");
        for (var i = 0; i < 1000; i++) { capture.Frame(frameMs); capture.Timing(Metric.FluidCables, fluidMs, false); }
        if (signalMs.HasValue) for (var i = 0; i < 1500; i++) capture.Timing(Metric.SignalEvaluation, signalMs.Value, false);
        capture.Timing(Metric.ServerTick, 4, false);
        capture.Timing(Metric.FluidGraphBuild, 2, false);
        if (save) capture.Timing(Metric.NativeSave, 91, false);
        capture.Population(ships, 15312);
        capture.Cables(256);
        return capture.Close().Replace("Stellar Performance " + PerformancePlugin.Version + " diagnostic capture:", "Stellar Performance " + (version ?? PerformancePlugin.Version) + " diagnostic capture:") + allocation + "; GC collections delta=8/8/8\n";
    }

    private static (int ExitCode, string Output, string Error) Run(string baseline, string candidate, string? baselineIndex = null, bool sharedWriter = false)
    {
        var directory = Path.Combine(Path.GetTempPath(), "StellarCaptureTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var baselinePath = Path.Combine(directory, "baseline.log");
            var candidatePath = Path.Combine(directory, "candidate.log");
            File.WriteAllText(baselinePath, baseline);
            File.WriteAllText(candidatePath, candidate);
            using var writer = sharedWriter ? new FileStream(candidatePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite) : null;
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(AppContext.BaseDirectory, "ComparePerformanceCaptures.ps1"), "-BaselineLog", baselinePath, "-CandidateLog", candidatePath, "-AsJson" }) start.ArgumentList.Add(argument);
            if (baselineIndex != null) { start.ArgumentList.Add("-BaselineCapture"); start.ArgumentList.Add(baselineIndex); }
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000)) { process.Kill(); throw new TimeoutException("Offline capture comparison did not exit."); }
            return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ComparisonCalculatesSignalWorkPerTickAndFrameAndKeepsMissingDataUnavailable()
    {
        var result = Run(Report(25, 5, signalMs: 1.64), Report(25, 5, signalMs: .55));
        Assert.True(result.ExitCode == 0, result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(1.64, json.RootElement.GetProperty("BaselineSummary").GetProperty("SignalMsPerTick").GetDouble());
        Assert.Equal(2.46, json.RootElement.GetProperty("BaselineSummary").GetProperty("SignalMsPerFrame").GetDouble(), 6);
        Assert.Equal(.825, json.RootElement.GetProperty("CandidateSummary").GetProperty("SignalMsPerFrame").GetDouble(), 6);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("BaselineSummary").GetProperty("ShipPartsMsPerTick").ValueKind);
    }

    [Fact]
    public void ComparisonSelectsWarmCapturesAndFlagsSaveAndUnvalidatedOldCounters()
    {
        var baseline = Report(40, 5.8, version: "0.1.1") + Report(25, 5, version: "0.1.1");
        var candidate = Report(25, 4.9, version: "0.1.1") + Report(25, 5, save: true, version: "0.1.1");
        var result = Run(baseline, candidate, "2");
        Assert.True(result.ExitCode == 0, result.Error);
        using var json = JsonDocument.Parse(result.Output);
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("Baseline").GetArrayLength());
        Assert.Equal(2, root.GetProperty("Baseline")[0].GetProperty("Capture").GetInt32());
        Assert.Equal("0.1.1", root.GetProperty("Baseline")[0].GetProperty("Version").GetString());
        Assert.Equal(40, root.GetProperty("BaselineSummary").GetProperty("AverageFps").GetDouble());
        Assert.Equal(0, root.GetProperty("Deltas").GetProperty("AverageFps").GetProperty("Percent").GetDouble());
        Assert.Equal(4.95, root.GetProperty("CandidateSummary").GetProperty("FluidMsPerFrame").GetDouble(), 6);
        Assert.Equal(91, root.GetProperty("Candidate")[1].GetProperty("SaveMaxMs").GetDouble());
        Assert.Equal("unvalidated", root.GetProperty("Candidate")[0].GetProperty("AllocationStatus").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("Candidate")[0].GetProperty("AllocatedBytes").ValueKind);
        Assert.Equal("8/8/8", root.GetProperty("Candidate")[0].GetProperty("GcGenerations").GetString());
        Assert.Contains("Save work occurred", root.GetProperty("Flags").ToString());
        Assert.Contains("old zero-byte readings", root.GetProperty("Flags").ToString());
    }

    [Fact]
    public void ComparisonHandlesValidatedUnavailableIncompleteAndMismatchedCaptures()
    {
        var baseline = Report(25, 5, allocation: "Main-thread allocated bytes=100000 (validated with 4096-byte positive control)");
        var candidate = Report(25, 5, ships: 21, allocation: "Main-thread allocation counter unavailable; no allocation estimate reported.") + "Stellar Performance 0.1.2 diagnostic capture: modules=profile-cables\nFrame intervals: no samples\n";
        var result = Run(baseline, candidate);
        Assert.True(result.ExitCode == 0, result.Error);
        using var json = JsonDocument.Parse(result.Output);
        var root = json.RootElement;
        Assert.Equal("validated", root.GetProperty("Baseline")[0].GetProperty("AllocationStatus").GetString());
        Assert.Equal(100000, root.GetProperty("Baseline")[0].GetProperty("AllocatedBytes").GetInt64());
        Assert.Equal("unavailable", root.GetProperty("Candidate")[0].GetProperty("AllocationStatus").GetString());
        Assert.Equal(1, root.GetProperty("Candidate").GetArrayLength());
        Assert.Equal(.002, root.GetProperty("Candidate")[0].GetProperty("FluidGraphMsPerFrame").GetDouble(), 6);
        Assert.Contains("incomplete", root.GetProperty("Flags").ToString());
        Assert.Contains("Workload maxima differ", root.GetProperty("Flags").ToString());
    }

    [Fact]
    public void CompletedCapturesCanBeReadWhileTheGameStyleLogWriterRemainsOpen()
    {
        var result = Run(Report(25, 5), Report(25, 4), sharedWriter: true);
        Assert.True(result.ExitCode == 0, result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(4, json.RootElement.GetProperty("CandidateSummary").GetProperty("FluidMsPerFrame").GetDouble());
    }

    [Fact]
    public void FrameOnlyComparisonsKeepUnmeasuredSubsystemsNullAndFlagInstrumentationChanges()
    {
        var capture = new CaptureData("modules=; diagnostic-mode=Frame", DiagnosticMode.Frame);
        for (var i = 0; i < 1000; i++) capture.Frame(20);
        var frame = capture.Close() + "Main-thread allocation counter unavailable; no allocation estimate reported.; GC collections delta=1/1/1\n";
        var lean = Report(20, 1).Replace("diagnostic capture: modules=", "diagnostic capture: diagnostic-mode=Lean; modules=");
        var result = Run(frame, lean);
        Assert.True(result.ExitCode == 0, result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("BaselineSummary").GetProperty("FluidMsPerFrame").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("BaselineSummary").GetProperty("ServerTickMs").ValueKind);
        Assert.Contains("Diagnostic modes differ", json.RootElement.GetProperty("Flags").ToString());
        Assert.Contains("Frame mode", json.RootElement.GetProperty("Flags").ToString());
        var reversed = Run(lean, frame);
        Assert.True(reversed.ExitCode == 0, reversed.Error);
        using var reversedJson = JsonDocument.Parse(reversed.Output);
        var delta = reversedJson.RootElement.GetProperty("Deltas").GetProperty("FluidMsPerFrame");
        Assert.Equal(JsonValueKind.Null, delta.GetProperty("Percent").ValueKind);
        Assert.Equal(JsonValueKind.Null, delta.GetProperty("Difference").ValueKind);
    }

    [Fact]
    public void InvalidCaptureIndicesFailInsteadOfSilentlyComparingAnotherRun()
    {
        var result = Run(Report(25, 5), Report(25, 5), "99");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("outside 1..1", result.Error);
    }
}
