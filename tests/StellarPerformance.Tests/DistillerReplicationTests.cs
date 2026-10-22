using System;
using System.Linq;
using FishNet.Serializing;
using Items.Model;
using Managers.Server.Network.Serialization;
using Ships.Interface.Model.Parts.State;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class DistillerReplicationTests
{
    private static EthanolDistillerState Filled(byte quantity = 255) => new EthanolDistillerState
    {
        InventorySlots = new[] { new InventorySlot { HasItem = true, ItemId = 110, Quantity = quantity } },
        NormalizedProductionRate = 1, PartialBiomass = .8f
    };

    private static byte[] Encode(EthanolDistillerState state)
    {
        if (GenericWriter<float>.Write == null) GenericWriter<float>.SetWrite((w, v) => w.WriteSingle(v));
        var writer = new Writer(); StatefulPartSerialization.WriteState(writer, state);
        return writer.GetArraySegment().ToArray();
    }

    [Fact]
    public void FractionalChangesRemainAuthoritativeButProduceIdenticalNativePayloads()
    {
        var a = Filled(); var b = a with { PartialBiomass = .4f, InventorySlots = (InventorySlot[])a.InventorySlots.Clone() };
        Assert.False(a.Equals(b)); Assert.Equal(Encode(a), Encode(b));
        Assert.True(ReplicationDeduplication.SamePayload(a, b));
        Assert.Equal(.4f, b.PartialBiomass); Assert.NotSame(a.InventorySlots, b.InventorySlots);
    }

    [Fact]
    public void EveryEncodedTransitionIncludingReturnToAnEarlierValueRemainsImmediate()
    {
        var a = Filled();
        foreach (var b in new[] { Filled(254), a with { IsInUse = true }, a with { NormalizedProductionRate = 0 },
            a with { InventorySlots = new[] { new InventorySlot { HasItem = true, ItemId = 111, Quantity = 255 } } },
            a with { InventorySlots = new[] { new InventorySlot() } } })
        {
            Assert.False(ReplicationDeduplication.SamePayload(a, b));
            Assert.False(ReplicationDeduplication.SamePayload(b, a));
            Assert.False(Encode(a).SequenceEqual(Encode(b)));
        }
        Assert.False(ReplicationDeduplication.SamePayload(null, a));
        Assert.False(ReplicationDeduplication.SamePayload(new FluidPumpState(), a));
        Assert.False(ReplicationDeduplication.SamePayload(a, a with { InventorySlots = null! }));
        Assert.False(ReplicationDeduplication.SamePayload(a, a with { InventorySlots = Array.Empty<InventorySlot>() }));
    }

    [Fact]
    public void ComparisonMatchesActualNativeWriterForEmptySlotsBitPatternsAndRandomStates()
    {
        var random = new Random(211026);
        for (var i = 0; i < 3000; i++)
        {
            var a = Filled((byte)random.Next(256)) with { NormalizedProductionRate = i % 9 == 0 ? float.NaN : (float)random.NextDouble() };
            if (i % 3 == 0) a = a with { InventorySlots = new[] { new InventorySlot() } };
            var slots = (InventorySlot[])a.InventorySlots.Clone();
            var b = a with { PartialBiomass = (float)random.NextDouble(), InventorySlots = slots };
            if (i % 5 == 0) slots[0].Quantity++;
            if (i % 7 == 0) slots[0].ItemId++;
            if (i % 11 == 0) b = b with { IsInUse = !b.IsInUse };
            if (i % 13 == 0) b = b with { NormalizedProductionRate = -b.NormalizedProductionRate };
            Assert.Equal(Encode(a).SequenceEqual(Encode(b)), ReplicationDeduplication.SamePayload(a, b));
        }
        var positive = Filled() with { NormalizedProductionRate = 0f };
        var negative = positive with { NormalizedProductionRate = BitConverter.Int32BitsToSingle(int.MinValue) };
        Assert.False(ReplicationDeduplication.SamePayload(positive, negative));
    }
}
