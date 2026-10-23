using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ships;
using Ships.Cables;
using Ships.Cables.Model;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Services;
using Ships.Services;

namespace StellarPerformance;

// Opt-in exact native-state specialization. The native method builds two endpoint
// Lists, discards one, then resolves every emitting ship/part a second time. For
// inspected pure native states, stream the same ordered values into the unchanged
// native reducer. Unknown/derived states retain the complete native method.
internal static class SignalReduction
{
    static SignalReduction() { }
    private static Func<SignalConnectionGroupValues, ConnectionGroupMap> _groups = null!;
    private static Func<SignalConnectionGroupValues, IShipsProvider> _ships = null!;
    private static Func<SignalConnectionGroupValues, Dictionary<uint, double>> _values = null!;
    private static Func<ConnectionGroup, IReadOnlyCollection<SocketConnection>> _connections = NativeConnections;
    private const string UnionOwner = "stellarperformance.signal-connection-scan";

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var update = AccessTools.Method(typeof(SignalConnectionGroupValues), nameof(SignalConnectionGroupValues.UpdateGroupSignalValue));
        var query = AccessTools.Method(typeof(SignalConnectionGroupValues), nameof(SignalConnectionGroupValues.GetGroupInputsAndOutputs));
        PatchSafety.RequireOriginalBody(update, harmony.Id);
        PatchSafety.RequireNoForeignOwners(query, harmony.Id, UnionOwner);
        if (AccessTools.Field(typeof(StatefulPart), nameof(StatefulPart.State))?.FieldType != typeof(IStatefulPartState))
            throw new InvalidOperationException("Native StatefulPart.State field changed.");
        foreach (var method in BypassedMethods()) PatchSafety.RequireNoForeignOwners(method, harmony.Id);
        foreach (var pair in ProjectedProperties()) RequirePureGetter(pair.Item1, pair.Item2);
        _groups = NativeAccess.Read<SignalConnectionGroupValues, ConnectionGroupMap>("_groupMap");
        _ships = NativeAccess.Read<SignalConnectionGroupValues, IShipsProvider>("_ships");
        _values = NativeAccess.Read<SignalConnectionGroupValues, Dictionary<uint, double>>("_groupSignalValues");
        _connections = HarmonyLib.Harmony.GetPatchInfo(query)?.Owners.Contains(UnionOwner) == true ? SignalConnectionScan.Read : NativeConnections;
        SignalEndpointPlan.Initialize(harmony);
        harmony.Patch(update, prefix: new HarmonyMethod(typeof(SignalReduction), nameof(BeforeUpdate)));
    }

    private static IReadOnlyCollection<SocketConnection> NativeConnections(ConnectionGroup group) => group.AllConnections;

    private static IEnumerable<Tuple<Type, string>> ProjectedProperties()
    {
        var properties = new[] {
            Tuple.Create(typeof(LeverState), "LeverHeight"), Tuple.Create(typeof(PilotSeatState), "MoveInputY"), Tuple.Create(typeof(PilotSeatState), "MoveInputZ"),
            Tuple.Create(typeof(PilotSeatAdvState), "MoveInput"), Tuple.Create(typeof(PilotSeatAdvState), "RotationInput"), Tuple.Create(typeof(TargetingComputerState), "RelativePosition"),
            Tuple.Create(typeof(MathGateState), "Output"), Tuple.Create(typeof(SignalProcessorState), "Output"), Tuple.Create(typeof(ConstantState), "ConstantValue"),
            Tuple.Create(typeof(DeltaState), "DeltaValue"), Tuple.Create(typeof(MemoryRegisterState), "Value"), Tuple.Create(typeof(VelocitySensorState), "Velocity"),
            Tuple.Create(typeof(AngularVelSensorState), "AngularVel"), Tuple.Create(typeof(GasSensorState), "Output"), Tuple.Create(typeof(FluidTankState), "FluidQuantity")
        };
        foreach (var property in properties) yield return property;
        foreach (var type in properties.Select(pair => pair.Item1).Distinct())
        {
            yield return Tuple.Create(type, "EmissionSockets");
            if (typeof(ISignalProcessorState).IsAssignableFrom(type)) yield return Tuple.Create(type, "ListeningSockets");
        }
    }

    internal static void RequirePureGetter(Type type, string name)
    {
        var getter = AccessTools.PropertyGetter(type, name) ?? throw new MissingMemberException(type.FullName, name);
        var body = PatchProcessor.GetOriginalInstructions(getter);
        var fieldRead = body.Count == 2 && body[0].opcode == OpCodes.Ldsfld && body[1].opcode == OpCodes.Ret;
        fieldRead |= body.Count == 3 && body[0].opcode == OpCodes.Ldarg_0 && body[1].opcode == OpCodes.Ldfld && body[2].opcode == OpCodes.Ret;
        if (!fieldRead) throw new InvalidOperationException("Signal projection requires a pure native field getter: " + type.Name + "." + name);
    }

    private static IEnumerable<MethodBase> BypassedMethods()
    {
        // Reduction is interleaved with endpoint discovery, so its inspected
        // pure implementation must not mutate state or socket membership.
        yield return AccessTools.Method(typeof(SignalCablesUtils), nameof(SignalCablesUtils.ResolveConflictingSignal));
        yield return AccessTools.Method(typeof(ConnectionGroupMap), nameof(ConnectionGroupMap.TryGetConnectionGroupWithId));
        foreach (var name in new[] { "GetSocketSignal", "TryAddSocket" }) yield return AccessTools.Method(typeof(SignalConnectionGroupValues), name);
        yield return AccessTools.PropertyGetter(typeof(ConnectionGroup), nameof(ConnectionGroup.AllConnections));
        yield return AccessTools.PropertyGetter(typeof(EntityPartSocket), nameof(EntityPartSocket.PartContext));
        yield return AccessTools.Method(typeof(ISignalEmitterState), nameof(ISignalEmitterState.HasEmissionSocketId));
        yield return AccessTools.Method(typeof(ISignalProcessorState), nameof(ISignalProcessorState.HasEmissionSocketId));
        yield return AccessTools.Method(typeof(ISignalProcessorState), nameof(ISignalProcessorState.HasListeningSocketId));
        yield return AccessTools.Method(typeof(TrackedShip), nameof(TrackedShip.TryGetStatefulPart));
        yield return AccessTools.Method(typeof(ShipState), nameof(ShipState.TryGetPart));
        yield return AccessTools.PropertyGetter(typeof(TrackedShip), nameof(TrackedShip.State));
        foreach (var provider in new[] { typeof(ShipsServerTracker), typeof(ShipsClientTracker) })
            yield return provider.GetInterfaceMap(typeof(IShipsProvider)).TargetMethods.Single(method => method.Name == nameof(IShipsProvider.TryGetShip));
        foreach (var property in ProjectedProperties()) yield return AccessTools.PropertyGetter(property.Item1, property.Item2);
    }

    private static bool BeforeUpdate(SignalConnectionGroupValues __instance, uint groupId)
    {
        var provider = _ships(__instance);
        if (!NativeProvider(provider)) return true;
        var value = 0.0;
        if (_groups(__instance).TryGetConnectionGroupWithId(groupId, out var group) && !TryReduce(group, provider, _connections(group), out value)) return true;
        _values(__instance)[groupId] = value;
        return false;
    }

    private static bool NativeProvider(IShipsProvider provider) => provider.GetType() == typeof(ShipsServerTracker) || provider.GetType() == typeof(ShipsClientTracker);

    internal static bool TryReduce(ConnectionGroup group, IShipsProvider provider, IReadOnlyCollection<SocketConnection> connections, out double result)
    {
        result = 0.0;
        if (!NativeProvider(provider)) return false;
        if (provider.GetType() == typeof(ShipsServerTracker) && SignalEndpointPlan.Initialized)
            return SignalEndpointPlan.TryReduce(group, (ShipsServerTracker)provider, connections, out result);
        return TryReduceStreaming(provider, connections, out result);
    }

    internal static bool TryReduceStreaming(IShipsProvider provider, IReadOnlyCollection<SocketConnection> connections, out double result)
    {
        result = 0.0;
        foreach (var connection in connections)
        {
            if (connection.SocketA.HasValue && !Add(provider, connection.SocketA.Value, ref result)) return false;
            if (connection.SocketB.HasValue && !Add(provider, connection.SocketB.Value, ref result)) return false;
        }
        return true;
    }

    private static bool Add(IShipsProvider provider, EntityPartSocket socket, ref double result)
    {
        if (!provider.TryGetShip(socket.EntityId, out var ship)) return true;
        if (ship.GetType() != typeof(TrackedShipServer) && ship.GetType() != typeof(TrackedShipClient)) return false;
        if (!ship.TryGetStatefulPart(socket.PartContext.PartId, out var part)) return true;
        return Accumulate(part.State, socket.Socket.SocketId, ref result);
    }
    internal static bool Accumulate(IStatefulPartState state, byte socketId, ref double result)
    {
        if (!(state is ISignalEmitterState) && !(state is ISignalProcessorState)) return true;
        if (!TryRead(state, socketId, out var value)) return false;
        // Arrays remain live and mutable. Preserve native multiplicity for a state
        // implementing both interfaces, and native A/B connection traversal order.
        if (state is ISignalEmitterState emitter && emitter.HasEmissionSocketId(socketId)) result = SignalCablesUtils.ResolveConflictingSignal(result, value);
        if (state is ISignalProcessorState processor && processor.HasEmissionSocketId(socketId)) result = SignalCablesUtils.ResolveConflictingSignal(result, value);
        return true;
    }

    internal static bool TryRead(IStatefulPartState state, byte socketId, out double value)
    {
        value = 0.0;
        var type = state.GetType();
        if (type == typeof(LeverState)) value = ((LeverState)state).LeverHeight;
        else if (type == typeof(PilotSeatState)) { var s = (PilotSeatState)state; value = socketId == 0 ? s.MoveInputY : socketId == 1 ? s.MoveInputZ : 0.0; }
        else if (type == typeof(PilotSeatAdvState)) { var s = (PilotSeatAdvState)state; value = socketId switch { 2 => s.MoveInput.x, 1 => s.MoveInput.y, 0 => s.MoveInput.z, 5 => s.RotationInput.x, 4 => s.RotationInput.y, 3 => s.RotationInput.z, _ => 0.0 }; }
        else if (type == typeof(TargetingComputerState)) { var s = (TargetingComputerState)state; value = socketId switch { 0 => s.RelativePosition.x, 1 => s.RelativePosition.y, 2 => s.RelativePosition.z, _ => 0.0 }; }
        else if (type == typeof(MathGateState)) value = socketId == 2 ? ((MathGateState)state).Output : 0.0;
        else if (type == typeof(SignalProcessorState)) value = socketId == 1 ? ((SignalProcessorState)state).Output : 0.0;
        else if (type == typeof(ConstantState)) value = socketId == 0 ? ((ConstantState)state).ConstantValue : 0.0;
        else if (type == typeof(DeltaState)) value = socketId == 1 ? ((DeltaState)state).DeltaValue : 0.0;
        else if (type == typeof(MemoryRegisterState)) value = socketId == 2 ? ((MemoryRegisterState)state).Value : 0.0;
        else if (type == typeof(VelocitySensorState)) value = socketId == 0 ? ((VelocitySensorState)state).Velocity : 0.0;
        else if (type == typeof(AngularVelSensorState)) value = socketId == 0 || socketId == 1 ? ((AngularVelSensorState)state).AngularVel : 0.0;
        else if (type == typeof(GasSensorState)) value = socketId == 0 ? ((GasSensorState)state).Output : 0.0;
        else if (type == typeof(FluidTankState)) value = socketId == 2 ? ((FluidTankState)state).FluidQuantity : 0.0;
        else return false;
        return true;
    }
}
