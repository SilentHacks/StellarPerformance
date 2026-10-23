using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ships;
using Ships.Cables.Model;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.State;
using Ships.Services;
using Xunit;

namespace StellarPerformance.Tests;

[Collection("Diagnostic counters")]
public sealed class SignalEndpointPlanTests
{
    [Fact]
    public void ModernDictionaryOverwriteSemanticsCannotEnablePointerReuse()
    {
        // The game runs an older Mono implementation. Its positive check must
        // reject CoreCLR, whose indexer overwrite leaves enumerators valid.
        Assert.False(SignalEndpointPlan.SupportsMutationVersions<uint, TrackedShipServer>(1));
        Assert.False(SignalEndpointPlan.SupportsMutationVersions<ushort, StatefulPart>(1));
    }

    private sealed class Once : IReadOnlyCollection<SocketConnection>
    {
        internal int Enumerations;
        internal readonly SocketConnection Wire = new SocketConnection(new EntityPartSocket(1, 1, 0), null, SocketConnectionType.Cable);
        public int Count => 1;
        public IEnumerator<SocketConnection> GetEnumerator() { if (++Enumerations > 1) throw new InvalidOperationException("Fallback reread an already supplied union."); yield return Wire; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void UnsupportedRuntimeStreamsTheProvidedUnionOnceAndReadsStateLive()
    {
        var part = (StatefulPart)RuntimeHelpers.GetUninitializedObject(typeof(StatefulPart)); part.Id = 1; part.State = new ConstantState { ConstantValue = -7 };
        var state = (ShipState)RuntimeHelpers.GetUninitializedObject(typeof(ShipState));
        AccessTools.Field(typeof(ShipState), "_statefulParts").SetValue(state, new Dictionary<ushort, StatefulPart> { [1] = part });
        var ship = (TrackedShipServer)RuntimeHelpers.GetUninitializedObject(typeof(TrackedShipServer));
        AccessTools.Field(typeof(TrackedShip), "<State>k__BackingField").SetValue(ship, state);
        var provider = (ShipsServerTracker)RuntimeHelpers.GetUninitializedObject(typeof(ShipsServerTracker));
        AccessTools.Field(typeof(ShipsServerTracker), "_shipsDict").SetValue(provider, new Dictionary<uint, TrackedShipServer> { [1] = ship });
        var group = new ConnectionGroup(9);
        foreach (var value in new[] { -7.0, 15.0, double.NaN, -0.0 })
        {
            part.State = new ConstantState { ConstantValue = value }; var source = new Once();
            Assert.True(SignalEndpointPlan.TryReduce(group, provider, source, out var result));
            Assert.Equal(BitConverter.DoubleToInt64Bits(Ships.Cables.SignalCablesUtils.ResolveConflictingSignal(0.0, value)), BitConverter.DoubleToInt64Bits(result));
            Assert.Equal(1, source.Enumerations);
        }
        Assert.Equal(0, SignalEndpointPlan.RetainedEntries);
    }

    [Fact]
    public void OnlyValidatedRetainedUnionIdentitiesCanBackEndpointPlans()
    {
        SignalConnectionScan.Clear();
        try
        {
            var group = new ConnectionGroup(1); group.AddConnection(new Once().Wire);
            var first = SignalConnectionScan.Read(group);
            Assert.True(SignalConnectionScan.IsRetained(group, first));
            Assert.False(SignalConnectionScan.IsRetained(group, group.AllConnections));
            group.AddConnection(new SocketConnection(new EntityPartSocket(1, 2, 0), null, SocketConnectionType.Cable) { CableId = 2 });
            var next = SignalConnectionScan.Read(group);
            Assert.False(SignalConnectionScan.IsRetained(group, first)); Assert.True(SignalConnectionScan.IsRetained(group, next));
            SignalConnectionScan.Clear(); Assert.False(SignalConnectionScan.IsRetained(group, next));
        }
        finally { SignalConnectionScan.Clear(); }
    }
}
