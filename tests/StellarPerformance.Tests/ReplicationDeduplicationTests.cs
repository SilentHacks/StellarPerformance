using System;
using System.Linq;
using System.Reflection;
using FishNet.Serializing;
using Items.Model;
using Managers.Server.Network.Serialization;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Values;
using Ships.Interface.Model.Parts.Common;
using UnityEngine;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class ReplicationDeduplicationTests
{
    [Fact]
    public void NativeRecordEqualityRunsExactlyOnceIncludingUnknownTypes()
    {
        var value = new EqualityProbe();
        Assert.False(ReplicationDeduplication.NativeEqualsOrSamePayload(value, new object()));
        Assert.Equal(1, value.Calls);
        Assert.True(ReplicationDeduplication.NativeEqualsOrSamePayload(value, value));
        Assert.Equal(2, value.Calls);
    }
    private sealed class EqualityProbe
    {
        internal int Calls;
        public override bool Equals(object? other) { Calls++; return ReferenceEquals(this, other); }
        public override int GetHashCode() => 1;
    }
    private static readonly InventorySlot Slot = new InventorySlot { HasItem = true, ItemId = 42, Quantity = 255 };
    internal static byte[] Encode(IStatefulPartState state)
    {
        GenericWriter<float>.SetWrite((w, v) => w.WriteSingle(v));
        GenericWriter<uint>.SetWrite((w, v) => w.WriteUInt32(v));
        GenericWriter<bool>.SetWrite((w, v) => w.WriteBoolean(v));
        var method = Assembly.Load("Players").GetType("FishNet.Serializing.Generated.GeneratedWriters___Internal", true)!
            .GetMethod("GWrite___Items.Model.InventorySlotFishNet.Serializing.Generated", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        GenericWriter<InventorySlot>.SetWrite(method.CreateDelegate<Action<Writer, InventorySlot>>());
        var writer = new Writer(); StatefulPartSerialization.WriteState(writer, state);
        return writer.GetArraySegment().ToArray();
    }

    [Fact]
    public void InternalOnlyChangesMatchNativePayloadsAndEncodedChangesRemainImmediate()
    {
        void Check(IStatefulPartState a, IStatefulPartState b, IStatefulPartState changed)
        {
            Assert.False(a.Equals(b)); Assert.Equal(Encode(a), Encode(b));
            Assert.True(ReplicationDeduplication.SamePayload(a, b));
            Assert.False(Encode(a).SequenceEqual(Encode(changed)));
            Assert.False(ReplicationDeduplication.SamePayload(a, changed));
            Assert.False(ReplicationDeduplication.SamePayload(changed, a));
        }
        var e = new ElectrolyzerState { IceItemSlot = Slot, NormalizedProductionRate = 1 };
        Check(e, e with { PartialItemConsumptionLevel = .4f }, e with { NormalizedProductionRate = 0 });
        var g = new WoodPowerGeneratorState { ItemSlot = Slot, NormalizedProductionLevel = 1 };
        Check(g, g with { PartialItemConsumptionLevel = .4f }, g with { ItemSlot = new InventorySlot() });
        var t = new WoodThrusterState { ItemSlot = Slot, NormalizedThrustLevel = 1 };
        Check(t, t with { PartialItemConsumptionLevel = .4f }, t with { IsHavingTroubleStarting = true });
        var gyro = new GyroscopeState();
        Check(gyro, gyro with { LastInputReceivedTick = 42, RotationStrength = new Vector3(1, 2, 3) }, gyro with { TargetGyroscopeAnimation = new Vector3(1, 0, 0) });
        var p = new PilotSeatState();
        Check(p, p with { LastInputReceivedTick = 42, InternalGyroStrength = new Vector3(1, 2, 3) }, p with { MoveInputY = 1 });
        var v = new VelocitySensorState();
        Check(v, v with { PreviousPos = new Vector3(1, 2, 3) }, v with { Velocity = 1 });
        var s = new SignalProcessorState();
        Check(s, s with { PreviousValue = 42 }, s with { Output = 1 });
        var transmitter = new SignalTransmitterState { Channel = "channel" };
        Check(transmitter, transmitter with { SignalValue = 42 }, transmitter with { Channel = "changed" });
        var warp = new WarpDriveState();
        Check(warp, warp with { WarpStartTimer = 42 }, warp with { State = (WarpDriveStateType)1 });
        var storage = new StorageState();
        Check(storage, storage with { InventorySlots = new InventorySlot[10] }, storage with { IsInUse = true });
        var gas = new GasSensorState { Output = 1 };
        Check(gas, gas with { Output = Math.BitIncrement(1d) }, gas with { Output = 2 });
    }

    [Fact]
    public void InteractionPickupUnknownAndMalformedStatesRetainNativePath()
    {
        var e = new ElectrolyzerState(); Assert.False(ReplicationDeduplication.SamePayload(e, e with { IsInUse = true }));
        var g = new WoodPowerGeneratorState(); Assert.False(ReplicationDeduplication.SamePayload(g, g with { IsInUse = true }));
        var t = new WoodThrusterState(); Assert.False(ReplicationDeduplication.SamePayload(t, t with { IsInUse = true }));
        var v = new VelocitySensorState(); Assert.False(ReplicationDeduplication.SamePayload(v, v with { IsHeldByPlayer = true }));
        Assert.False(ReplicationDeduplication.SamePayload(v, v with { HoldingPlayerId = 1 }));
        var s = new SignalProcessorState(); Assert.False(ReplicationDeduplication.SamePayload(s, s with { IsHeldByPlayer = true }));
        var tx = new SignalTransmitterState(); Assert.False(ReplicationDeduplication.SamePayload(tx, tx with { IsInUse = true }));
        Assert.False(ReplicationDeduplication.SamePayload(tx, tx with { HoldingPlayerId = 1 }));
        var storage = new StorageState();
        Assert.False(ReplicationDeduplication.SamePayload(storage, storage with { InventorySlots = null! }));
        Assert.False(ReplicationDeduplication.SamePayload(storage, storage with { InventorySlots = new InventorySlot[9] }));
        Assert.False(ReplicationDeduplication.SamePayload(new FluidTankState(), new FluidTankState()));
        Assert.False(ReplicationDeduplication.SamePayload(new GasIntakeState(), new GasIntakeState()));
        Assert.False(ReplicationDeduplication.SamePayload(new CustomElectrolyzer(), new CustomElectrolyzer()));
        Assert.False(ReplicationDeduplication.SamePayload(null, storage));
        Assert.False(ReplicationDeduplication.SamePayload(storage, null));
        Assert.False(ReplicationDeduplication.SamePayload(storage, e));
    }
    private sealed record CustomElectrolyzer : ElectrolyzerState;

    [Fact]
    public void EverySlotFieldEveryVectorComponentAndSignalOperationArePreserved()
    {
        var random = new System.Random(221026);
        for (var i = 0; i < 1000; i++)
        {
            var slot = new InventorySlot { HasItem = i % 2 == 0, ItemId = (uint)random.Next(), Quantity = (byte)random.Next(256) };
            var e = new ElectrolyzerState { IceItemSlot = slot, NormalizedProductionRate = (float)random.NextDouble() };
            var changed = slot;
            if (i % 3 == 0) changed.HasItem = !changed.HasItem;
            else if (i % 3 == 1) changed.ItemId++;
            else changed.Quantity++;
            var b = e with { IceItemSlot = changed };
            Assert.Equal(Encode(e).SequenceEqual(Encode(b)), ReplicationDeduplication.SamePayload(e, b));
            var gyro = new GyroscopeState { TargetGyroscopeAnimation = new Vector3(i, i + 1, i + 2) };
            var vector = gyro.TargetGyroscopeAnimation; vector[i % 3]++;
            Assert.False(ReplicationDeduplication.SamePayload(gyro, gyro with { TargetGyroscopeAnimation = vector }));
            var p = new SignalProcessorState { Output = random.NextDouble(), PreviousValue = random.NextDouble() };
            Assert.True(ReplicationDeduplication.SamePayload(p, p with { PreviousValue = random.NextDouble() }));
            Assert.False(ReplicationDeduplication.SamePayload(p, p with { Operation = (SignalProcessorOperation)1 }));
            var slots = new InventorySlot[10]; slots[i % 10] = slot;
            var storage = new StorageState { InventorySlots = slots };
            Assert.True(ReplicationDeduplication.SamePayload(storage, storage with { InventorySlots = (InventorySlot[])slots.Clone() }));
            var copy = (InventorySlot[])slots.Clone(); copy[i % 10].HasItem = !copy[i % 10].HasItem;
            Assert.False(ReplicationDeduplication.SamePayload(storage, storage with { InventorySlots = copy }));
        }
    }

    [Fact]
    public void NativeWirePrecisionAndFloatBitsDetermineEqualityWithoutNewTolerance()
    {
        foreach (var bits in new[] { 0, int.MinValue, 1, int.MaxValue, 0x7f800000, unchecked((int)0xff800000), 0x7fc00001, 0x7fc00002 })
        {
            var value = BitConverter.Int32BitsToSingle(bits);
            var a = new ElectrolyzerState { NormalizedProductionRate = value };
            var b = a with { PartialItemConsumptionLevel = .5f };
            Assert.True(ReplicationDeduplication.SamePayload(a, b)); Assert.Equal(Encode(a), Encode(b));
            var gas = new GasSensorState { Output = value };
            var gas2 = gas with { Output = -gas.Output };
            Assert.Equal(Encode(gas).SequenceEqual(Encode(gas2)), ReplicationDeduplication.SamePayload(gas, gas2));
        }
        var zero = new SignalProcessorState { Output = 0 };
        Assert.False(ReplicationDeduplication.SamePayload(zero, zero with { Output = BitConverter.Int64BitsToDouble(long.MinValue) }));
        var pilot = new PilotSeatState { MoveInputY = 1 };
        Assert.True(ReplicationDeduplication.SamePayload(pilot, pilot with { MoveInputY = Math.BitIncrement(1d) }));
        Assert.False(ReplicationDeduplication.SamePayload(pilot, pilot with { RotationInput = new Vector3(0, 0, 1) }));
    }
}
