using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ships.Cables;
using Ships.Cables.Power;
using Ships.Interface.Model.Parts.Common;
using Xunit;

namespace StellarPerformance.Tests;

[Collection("Diagnostic counters")]
public sealed class PowerTransferScanTests
{
    private static T Bare<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Set(object owner, string field, object value) => AccessTools.Field(owner.GetType(), field).SetValue(owner, value);

    [Fact]
    public void ProjectionPreservesStaleIdsAndOrderThroughSameCountMutations()
    {
        PowerTransferScan.Clear();
        try
        {
            var source = new List<uint> { 9, 2, 9, 77, 2, 9, 3, 77 };
            var original = source.ToArray();
            var first = PowerTransferScan.Project(source);
            Assert.Equal(new uint[] { 9, 2, 77, 3 }, first);
            Assert.Equal(original, source);
            Assert.Same(first, PowerTransferScan.Project(source));
            var random = new Random(231026);
            for (var step = 0; step < 3000; step++)
            {
                if (step % 13 == 0) { source.Clear(); source.AddRange(new uint[] { 9, 8, 9, 7, 8, 7 }); }
                else if (step % 3 == 0) { source.RemoveAt(random.Next(source.Count)); source.Add((uint)random.Next(12)); }
                else if (step % 3 == 1) source[random.Next(source.Count)] = (uint)random.Next(12);
                else source.Reverse();
                Assert.Equal(source.Distinct(), PowerTransferScan.Project(source));
            }
            source.Clear(); Assert.Same(source, PowerTransferScan.Project(source));
        }
        finally { PowerTransferScan.Clear(); }
    }

    [Fact]
    public void UnknownObserversRetainNativeSourceIncludingAfterCachedProjection()
    {
        PowerTransferScan.Clear();
        try
        {
            var cables = Bare<CablesServerTracker>(); var source = new List<uint> { 1, 1, 2, 1 };
            Assert.Equal(new uint[] { 1, 2 }, PowerTransferScan.Read(source, cables));
            Action<CablePowerData> unknown = _ => { };
            cables.OnCablePowerUpdated = unknown;
            Assert.Same(source, PowerTransferScan.Read(source, cables));
            var receiver = new Observer(); Action<CablePowerData> inspected = receiver.Receive;
            Assert.True(PowerTransferScan.ObserversMatch(inspected, inspected.Method));
            Assert.True(PowerTransferScan.ObserversMatch(inspected + inspected, inspected.Method));
            Assert.False(PowerTransferScan.ObserversMatch(inspected + unknown, inspected.Method));
            Assert.False(PowerTransferScan.ObserversMatch(inspected, null));
            cables.OnCablePowerUpdated = null;
            Assert.Equal(new uint[] { 1, 2 }, PowerTransferScan.Read(source, cables));
        }
        finally { PowerTransferScan.Clear(); }
    }

    private sealed class Observer { internal void Receive(CablePowerData value) { } }

    private sealed class Fixture
    {
        internal readonly PowerServerTracker Power = Bare<PowerServerTracker>();
        internal readonly CablesServerTracker Cables = Bare<CablesServerTracker>();
        internal readonly Dictionary<uint, PowerGroupServerState> Groups = new Dictionary<uint, PowerGroupServerState>();
        internal readonly Dictionary<uint, CablePowerState> Watts = new Dictionary<uint, CablePowerState>();
        internal Fixture()
        {
            Set(Power, "_powerState", Groups); Set(Power, "_cablesServerTracker", Cables); Set(Cables, "_powerCableStates", Watts);
            // Both groups deliberately retain stale cable 99. Preserve its
            // cross-group write order and native final value.
            foreach (var id in new uint[] { 4, 7 })
            {
                var group = new PowerGroupServerState(id); group.CableIds.AddRange(new uint[] { id, 99, id, 99, id }); Groups.Add(id, group);
            }
        }
    }

    [Fact]
    public void EmittedNativeTransferLoopPreservesMathAndCrossGroupCableWrites()
    {
        PowerTransferScan.Clear();
        try
        {
            var method = AccessTools.Method(typeof(PowerServerTracker), nameof(PowerServerTracker.UpdatePowerTransfers));
            var native = (Action<PowerServerTracker, float>)ManagedMethod.Clone(method, (codes, _) => codes);
            var projected = (Action<PowerServerTracker, float>)ManagedMethod.Clone(method, (codes, _) => PowerTransferScan.Rewrite(codes));
            var a = new Fixture(); var b = new Fixture(); var random = new Random(231026);
            var special = new[] { 0f, -0f, float.NaN, float.PositiveInfinity, float.NegativeInfinity };
            for (var step = 0; step < 2000; step++)
            {
                foreach (var id in a.Groups.Keys)
                {
                    var needed = step % 17 == 0 ? special[step % special.Length] : (float)random.NextDouble() * 100f;
                    var capacity = step % 19 == 0 ? special[(step / 3) % special.Length] : (float)random.NextDouble() * 100f;
                    a.Groups[id].TotalPowerNeeded = b.Groups[id].TotalPowerNeeded = needed;
                    a.Groups[id].TotalPowerProductionCapacity = b.Groups[id].TotalPowerProductionCapacity = capacity;
                    if (step % 11 == 0) { a.Groups[id].CableIds.Add(id); b.Groups[id].CableIds.Add(id); }
                    if (step % 23 == 0) { a.Groups[id].CableIds[1] = b.Groups[id].CableIds[1] = (uint)random.Next(90, 100); }
                }
                var dt = step % 13 == 0 ? 0f : .02f;
                native(a.Power, dt); projected(b.Power, dt);
                foreach (var id in a.Groups.Keys)
                {
                    var x = a.Groups[id]; var y = b.Groups[id];
                    Assert.Equal(BitConverter.SingleToInt32Bits(x.TotalPowerTransfer), BitConverter.SingleToInt32Bits(y.TotalPowerTransfer));
                    Assert.Equal(BitConverter.SingleToInt32Bits(x.NormalizedConsumerRate), BitConverter.SingleToInt32Bits(y.NormalizedConsumerRate));
                    Assert.Equal(BitConverter.SingleToInt32Bits(x.NormalizedGeneratorRate), BitConverter.SingleToInt32Bits(y.NormalizedGeneratorRate));
                    Assert.Equal(x.CableIds, y.CableIds);
                }
                Assert.Equal(a.Watts.Keys, b.Watts.Keys);
                foreach (var pair in a.Watts) Assert.Equal(BitConverter.SingleToInt32Bits(pair.Value.Watts), BitConverter.SingleToInt32Bits(b.Watts[pair.Key].Watts));
            }
        }
        finally { PowerTransferScan.Clear(); }
    }

    [Fact]
    public void EmittedLoopFallsBackForReentrantObserversAndPreservesTheirEvents()
    {
        PowerTransferScan.Clear();
        try
        {
            var method = AccessTools.Method(typeof(PowerServerTracker), nameof(PowerServerTracker.UpdatePowerTransfers));
            var native = (Action<PowerServerTracker, float>)ManagedMethod.Clone(method, (codes, _) => codes);
            var projected = (Action<PowerServerTracker, float>)ManagedMethod.Clone(method, (codes, _) => PowerTransferScan.Rewrite(codes));
            var a = new Fixture(); var b = new Fixture(); var eventsA = new List<uint>(); var eventsB = new List<uint>();
            void Attach(Fixture fixture, List<uint> events) => fixture.Cables.OnCablePowerUpdated = e =>
            {
                events.Add(e.Id);
                // Repeated native writes can restore a callback's replacement.
                if (events.Count == 1) fixture.Watts[e.Id] = new CablePowerState { Watts = 12345f };
            };
            Attach(a, eventsA); Attach(b, eventsB);
            foreach (var group in a.Groups.Values.Concat(b.Groups.Values)) { group.TotalPowerNeeded = 10; group.TotalPowerProductionCapacity = 20; }
            native(a.Power, .02f); projected(b.Power, .02f);
            Assert.Equal(eventsA, eventsB); Assert.True(eventsA.Count(id => id == 4) > 1);
            Assert.Equal(a.Watts, b.Watts);
        }
        finally { PowerTransferScan.Clear(); }
    }

    [Fact]
    public void CacheLimitsRetainNativeListsAndClearReleasesAccounting()
    {
        PowerTransferScan.Clear();
        try
        {
            var retained = new List<List<uint>>();
            for (var i = 0; i < PowerTransferScan.EntryLimit; i++) { var source = new List<uint> { 1, 1 }; retained.Add(source); Assert.Single(PowerTransferScan.Project(source)); }
            var overflow = new List<uint> { 1, 1 }; Assert.Same(overflow, PowerTransferScan.Project(overflow));
            PowerTransferScan.Clear(); Assert.Single(PowerTransferScan.Project(overflow));
            var large = Enumerable.Range(0, PowerTransferScan.UniqueLimit + 1).Select(x => (uint)x).ToList(); large.Add(0);
            Assert.Same(large, PowerTransferScan.Project(large));
            GC.KeepAlive(retained);
        }
        finally { PowerTransferScan.Clear(); }
    }

    [Fact]
    public void ByteAccountingStopsBeforeCacheBudgetIsExceeded()
    {
        PowerTransferScan.Clear();
        try
        {
            var retained = new List<List<uint>>();
            var cached = 0;
            for (var i = 0; i < 9; i++)
            {
                var source = Enumerable.Range(0, PowerTransferScan.UniqueLimit).Select(x => (uint)x).ToList(); source.Add(0); retained.Add(source);
                if (!ReferenceEquals(source, PowerTransferScan.Project(source))) cached++;
            }
            Assert.Equal(7, cached);
            var bytes = (int)AccessTools.Field(typeof(PowerTransferScan), "_bytes").GetValue(null)!;
            Assert.InRange(bytes, 1, PowerTransferScan.ByteLimit);
            GC.KeepAlive(retained);
        }
        finally { PowerTransferScan.Clear(); }
    }
}
