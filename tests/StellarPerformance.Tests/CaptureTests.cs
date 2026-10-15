using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class CaptureTests
{
    private static long UnsupportedZeroCounter() => 0;
    private static Func<long>? _fakeCounter;
    private static readonly Func<long?> ReadCounter = (Func<long?>)ManagedMethod.Clone(AccessTools.Method(typeof(Diagnostics), "ReadAllocationCounter"), (codes, _) => codes.Select(original =>
    {
        var code = new CodeInstruction(original);
        if (code.operand is FieldInfo field && field.DeclaringType == typeof(Diagnostics) && field.Name == "_allocatedBytes") code.operand = AccessTools.Field(typeof(CaptureTests), nameof(_fakeCounter));
        return code;
    }));
    private static MethodInfo CounterMethod(Type type, string name, BindingFlags flags) => AccessTools.Method(typeof(CaptureTests), nameof(UnsupportedZeroCounter));

    [Fact]
    public void AllocationCounterThatSilentlyReturnsZeroIsReportedUnavailable()
    {
        var resolve = (Func<Func<long>?>)ManagedMethod.Clone(AccessTools.Method(typeof(Diagnostics), "ResolveAllocationCounter"), (codes, _) => codes.Select(original =>
        {
            var code = new CodeInstruction(original);
            if (code.operand is MethodInfo method && method.DeclaringType == typeof(Type) && method.Name == nameof(Type.GetMethod))
            {
                code.opcode = OpCodes.Call;
                code.operand = AccessTools.Method(typeof(CaptureTests), nameof(CounterMethod));
            }
            return code;
        }));
        Assert.Null(resolve());
    }

    [Fact]
    public void AllocationCounterPositiveControlAcceptsTheWorkingRuntimeCounter()
    {
        Func<long> counter = GC.GetAllocatedBytesForCurrentThread;
        Assert.Same(counter, Diagnostics.ValidateAllocationCounter(counter));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 99)]
    [InlineData(-1, 10000)]
    [InlineData(100, 4195)]
    public void AllocationCounterRejectsInvalidOrTooSmallPositiveControls(long before, long after)
    {
        var calls = 0;
        Assert.Null(Diagnostics.ValidateAllocationCounter(() => calls++ == 0 ? before : after));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsupportedAllocationCountersDoNotBreakDiagnostics(bool notImplemented)
    {
        Assert.Null(Diagnostics.ValidateAllocationCounter(() => throw (notImplemented ? new NotImplementedException() : new NotSupportedException())));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AllocationCounterFailuresAfterValidationRemainUnavailable(int failure)
    {
        _fakeCounter = () => failure == 0 ? -1 : throw (failure == 1 ? new NotImplementedException() : new NotSupportedException());
        Assert.Null(ReadCounter());
        Assert.Null(_fakeCounter);
        Assert.Null(ReadCounter());
    }

    [Fact]
    public void GcCountersAreSnapshottedBeforeReportFormattingAllocations()
    {
        var codes = ManagedMethod.Instructions(AccessTools.Method(typeof(Diagnostics), nameof(Diagnostics.Stop)));
        var lastGc = codes.FindLastIndex(code => code.Calls(AccessTools.Method(typeof(GC), nameof(GC.CollectionCount))));
        var close = codes.FindIndex(code => code.Calls(AccessTools.Method(typeof(CaptureData), nameof(CaptureData.Close))));
        Assert.True(lastGc >= 0 && lastGc < close);
    }

    [Theory]
    [InlineData("Frame", "Frame")]
    [InlineData("lean", "Lean")]
    [InlineData("FULL", "Full")]
    [InlineData("unknown", "Lean")]
    [InlineData("19", "Lean")]
    [InlineData(null, "Lean")]
    public void DiagnosticModesRejectUnknownValuesAndDefaultToLean(string? value, string expected)
    {
        Assert.Equal(expected, Diagnostics.ParseMode(value).ToString());
    }

    [Theory]
    [InlineData("Frame")]
    [InlineData("Lean")]
    public void MissingDetailedCountersAreUnavailableNotZero(string mode)
    {
        var capture = new CaptureData("lean", Diagnostics.ParseMode(mode));
        capture.Frame(20);
        var report = capture.Close();
        Assert.Contains("Detailed state, network-equality, transform, environment and tool counters: unavailable", report);
        Assert.DoesNotContain("Part state updates: total=0", report);
        Assert.DoesNotContain("Queued network part updates: total=0", report);
        Assert.Contains("Average FPS=50.00", report);
    }

    [Fact]
    public void RenderingCountersAreCountsNotMillisecondsAndRejectInvalidSamples()
    {
        var capture = new CaptureData("counts");
        capture.RenderCount("Draw Calls Count", -1);
        capture.RenderCount("Draw Calls Count", 10);
        capture.RenderCount("Draw Calls Count", 20);
        var report = capture.Close();
        Assert.Contains("Render counter[Draw Calls Count]: samples=2, mean=15.00, max=20", report);
        Assert.DoesNotContain("mean=15.000 ms", report);
    }

    [Fact]
    public void QuantilesUseNearestRankAndSamplesAreBounded()
    {
        var samples = new TimingSamples(100);
        for (var i = 1; i <= 100; i++) samples.Add(i);
        Assert.Equal(95, TimingSamples.Percentile(samples.Sorted(), .95));
        Assert.Equal(99, TimingSamples.Percentile(samples.Sorted(), .99));
        samples.Add(101);
        Assert.Equal(101, samples.Count);
        Assert.Equal(100, samples.Stored);
        Assert.Equal(101, samples.Maximum);
    }

    [Fact]
    public void ALongCaptureDoesNotDiscardAllLaterTimingSamples()
    {
        var samples = new TimingSamples(1000);
        for (var index = 0; index < 1000; index++) samples.Add(1);
        for (var index = 0; index < 99000; index++) samples.Add(100);
        Assert.Equal(100000, samples.Count);
        Assert.InRange(samples.Sorted().Count(value => value == 100), 950, 1000);
    }

    [Fact]
    public void InvalidIntervalsDoNotPoisonTheCapture()
    {
        var samples = new TimingSamples(10);
        foreach (var value in new[] { -1, double.NaN, double.PositiveInfinity }) samples.Add(value);
        samples.Add(10, true);
        Assert.Equal(1, samples.Count);
        Assert.Equal(1, samples.Errors);
        Assert.Equal(10, samples.Total);
    }

    [Theory]
    [InlineData(double.NaN, 30)]
    [InlineData(double.PositiveInfinity, 30)]
    [InlineData(double.NegativeInfinity, 30)]
    [InlineData(-100, 5)]
    [InlineData(60, 60)]
    [InlineData(1000, 120)]
    public void CaptureDurationsAreAlwaysFiniteAndBounded(double value, double expected)
    {
        Assert.Equal(expected, CaptureData.CaptureSeconds(value));
    }

    [Fact]
    public void WireSizeIncludesTheTrailerAndResultBucketsPreservePermanentFailures()
    {
        Assert.Equal(524288, Steamworks.Constants.k_cbMaxSteamNetworkingSocketsMessageSizeSend);
        Assert.Equal(25, (int)Steamworks.EResult.k_EResultLimitExceeded);
        var capture = new CaptureData("sizes");
        capture.Frame(0);
        capture.Send(0, 524287, 0, 524289, 1, false);
        capture.Send(0, 524288, 0, 524290, 8, false);
        capture.Population(2, 3000);
        capture.Cables(12);
        var report = capture.Close();
        Assert.Contains("attempted sends exceeding Steam's 512KiB single-message limit=1", report);
        Assert.Contains("b5={1:1 ", report);
        Assert.Contains("b6={8:1 ", report);
        Assert.Contains("ships=2, parts=3000", report);
        Assert.DoesNotContain("Average FPS=", report);
    }

    [Fact]
    public void ReportDefinesOnePercentLowsAndCountsRejectedSends()
    {
        var capture = new CaptureData("test");
        foreach (var _ in Enumerable.Range(0, 100)) capture.Frame(10);
        capture.Send(0, 524288, 17, 1048576, 25, false);
        capture.Log("Space id 887 does not exist", true);
        var report = capture.Close();
        Assert.Contains("Average FPS=100.00", report);
        Assert.Contains("1% low=100.00", report);
        Assert.Contains("failures=1, limit-exceeded=1", report);
        Assert.Contains("space messages=1", report);
        Assert.DoesNotContain("887", report);
        capture.Frame(1000);
        Assert.Equal(report, capture.Close());
    }
}
