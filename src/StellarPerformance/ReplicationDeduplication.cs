using System;
using HarmonyLib;
using Managers.Server.Network;
using Managers.Server.Network.Serialization;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Items.Model;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace StellarPerformance;

// Compare consecutive inspected wire fields at the notification boundary only.
// Authoritative assignments, internal bookkeeping and callbacks remain native.
internal static class ReplicationDeduplication
{
    static ReplicationDeduplication() { }
    internal const string Owner = "stellarperformance.replication-deduplication";
    internal static bool Distillers = true, OtherParts = true;

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var target = AccessTools.Method(typeof(MasterSync), "ServerQueueStatefulPartUpdate");
        PatchSafety.RequireNoForeignOwners(target, Owner);
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(typeof(StatefulPartSerialization), "WriteState"));
        // Machines use FishNet's generated InventorySlot writer, whereas storage
        // and distillers write occupied-slot contents explicitly.
        var slotWriter = Assembly.Load("Players").GetType("FishNet.Serializing.Generated.GeneratedWriters___Internal", true)!
            .GetMethod("GWrite___Items.Model.InventorySlotFishNet.Serializing.Generated", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("Native InventorySlot writer");
        PatchSafety.RequireNoForeignOwners(slotWriter);
        if (FishNet.Serializing.GenericWriter<InventorySlot>.Write?.Method != slotWriter)
            throw new InvalidOperationException("InventorySlot writer differs from the inspected native writer.");
        foreach (var type in new[] { typeof(EthanolDistillerState), typeof(ElectrolyzerState), typeof(WoodPowerGeneratorState), typeof(WoodThrusterState),
            typeof(GyroscopeState), typeof(PilotSeatState), typeof(VelocitySensorState), typeof(SignalProcessorState),
            typeof(SignalTransmitterState), typeof(WarpDriveState), typeof(StorageState), typeof(GasSensorState) })
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                if (property.GetGetMethod(true) is MethodInfo getter) PatchSafety.RequireNoForeignOwners(getter);
            if (type.GetMethod("GetSlot", BindingFlags.Public | BindingFlags.Instance) is MethodInfo getSlot) PatchSafety.RequireNoForeignOwners(getSlot);
        }
        harmony.Patch(target, transpiler: new HarmonyMethod(typeof(ReplicationDeduplication), nameof(Rewrite)));
    }

    private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
    {
        var equals = AccessTools.Method(typeof(object), nameof(Equals), new[] { typeof(object) });
        var replacement = AccessTools.Method(typeof(ReplicationDeduplication), nameof(NativeEqualsOrSamePayload));
        var found = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(equals)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; found++; }
            yield return instruction;
        }
        if (found != 1) throw new InvalidOperationException("Native replication equality contract changed.");
    }

    internal static bool NativeEqualsOrSamePayload(object previous, object next)
    {
        // Retain the native equality call exactly once. Most steady states are
        // already equal; only unequal records need the additional wire comparison.
        if (previous.Equals(next)) return true;
        if (!(previous is IStatefulPartState a) || !(next is IStatefulPartState b)) return false;
        var enabled = b is EthanolDistillerState ? Distillers : OtherParts;
        var same = enabled && SamePayload(a, b);
        if (same && NetworkCapture.Active) NetworkCapture.DuplicateSuppressed(b);
        return same;
    }

    internal static bool SamePayload(IStatefulPartState? previous, IStatefulPartState? next)
    {
        if (previous == null || next == null || previous.GetType() != next.GetType()) return false;
        var type = previous.GetType();
        if (type == typeof(EthanolDistillerState))
        {
            var a = (EthanolDistillerState)previous; var b = (EthanolDistillerState)next;
            return a.InventorySlots != null && b.InventorySlots != null && a.InventorySlots.Length >= 1 && b.InventorySlots.Length >= 1 &&
                a.IsInUse == b.IsInUse && CoreBits.Same(a.NormalizedProductionRate, b.NormalizedProductionRate) &&
                SameOccupiedSlot(a.InventorySlots[0], b.InventorySlots[0]);
        }
        if (type == typeof(ElectrolyzerState))
        {
            var a = (ElectrolyzerState)previous; var b = (ElectrolyzerState)next;
            return a.IsInUse == b.IsInUse && CoreBits.Same(a.NormalizedProductionRate, b.NormalizedProductionRate) && SameSlot(a.IceItemSlot, b.IceItemSlot);
        }
        if (type == typeof(WoodPowerGeneratorState))
        {
            var a = (WoodPowerGeneratorState)previous; var b = (WoodPowerGeneratorState)next;
            return a.IsInUse == b.IsInUse && CoreBits.Same(a.NormalizedProductionLevel, b.NormalizedProductionLevel) && SameSlot(a.ItemSlot, b.ItemSlot);
        }
        if (type == typeof(WoodThrusterState))
        {
            var a = (WoodThrusterState)previous; var b = (WoodThrusterState)next;
            return a.IsInUse == b.IsInUse && CoreBits.Same(a.NormalizedThrustLevel, b.NormalizedThrustLevel) &&
                a.IsHavingTroubleStarting == b.IsHavingTroubleStarting && SameSlot(a.ItemSlot, b.ItemSlot);
        }
        if (type == typeof(GyroscopeState)) return SameVector(((GyroscopeState)previous).TargetGyroscopeAnimation, ((GyroscopeState)next).TargetGyroscopeAnimation);
        if (type == typeof(PilotSeatState))
        {
            var a = (PilotSeatState)previous; var b = (PilotSeatState)next;
            return a.IsInUse == b.IsInUse && CoreBits.Same((float)a.MoveInputY, (float)b.MoveInputY) && CoreBits.Same((float)a.MoveInputZ, (float)b.MoveInputZ) &&
                SameVector(a.RotationInput, b.RotationInput) && SameVector(a.TargetGyroscopeAnimation, b.TargetGyroscopeAnimation);
        }
        if (type == typeof(VelocitySensorState))
        {
            var a = (VelocitySensorState)previous; var b = (VelocitySensorState)next;
            return SameHolding(a, b) && SameDouble(a.Velocity, b.Velocity);
        }
        if (type == typeof(SignalProcessorState))
        {
            var a = (SignalProcessorState)previous; var b = (SignalProcessorState)next;
            return SameHolding(a, b) && a.Operation == b.Operation && SameDouble(a.Output, b.Output);
        }
        if (type == typeof(SignalTransmitterState))
        {
            var a = (SignalTransmitterState)previous; var b = (SignalTransmitterState)next;
            return SameHolding(a, b) && a.IsInUse == b.IsInUse && string.Equals(a.Channel, b.Channel, StringComparison.Ordinal);
        }
        if (type == typeof(WarpDriveState)) return ((WarpDriveState)previous).State == ((WarpDriveState)next).State;
        if (type == typeof(StorageState))
        {
            var a = (StorageState)previous; var b = (StorageState)next;
            if (a.IsInUse != b.IsInUse) return false;
            // The inspected implementation stores ten slots in a private array.
            // GetSlot can throw for malformed custom states; retain native behavior.
            try { for (byte i = 0; i < 10; i++) if (!SameOccupiedSlot(a.GetSlot(i), b.GetSlot(i))) return false; }
            catch (IndexOutOfRangeException) { return false; }
            catch (NullReferenceException) { return false; }
            return true;
        }
        if (type == typeof(GasSensorState)) return CoreBits.Same((float)((GasSensorState)previous).Output, (float)((GasSensorState)next).Output);
        return false;
    }

    private static bool SameSlot(InventorySlot a, InventorySlot b) => a.HasItem == b.HasItem && a.ItemId == b.ItemId && a.Quantity == b.Quantity;
    private static bool SameOccupiedSlot(InventorySlot a, InventorySlot b) => a.HasItem == b.HasItem && (!a.HasItem || (a.ItemId == b.ItemId && a.Quantity == b.Quantity));
    private static bool SameDouble(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
    private static bool SameVector(Vector3 a, Vector3 b) => CoreBits.Same(a.x, b.x) && CoreBits.Same(a.y, b.y) && CoreBits.Same(a.z, b.z);
    private static bool SameHolding(IPickablePartState a, IPickablePartState b) => a.IsHeldByPlayer == b.IsHeldByPlayer && a.HoldingPlayerId == b.HoldingPlayerId;
}
