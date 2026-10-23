using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Core.Values;
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
using UnityEngine;
using Xunit;

namespace StellarPerformance.Tests;

[Collection("Diagnostic counters")]
public sealed class SignalReductionTests
{
    // Execute emitted native managed copies. Never call Install/Harmony patch
    // state APIs, whose old runtime-detour initialization is unsafe on CoreCLR.
    private static readonly Action<SignalConnectionGroupValues, uint> NativeUpdate = (Action<SignalConnectionGroupValues, uint>)ManagedMethod.Clone(AccessTools.Method(typeof(SignalConnectionGroupValues), nameof(SignalConnectionGroupValues.UpdateGroupSignalValue)), (codes, _) => codes);
    private static readonly Func<SignalConnectionGroupValues, EntityPartSocket, double> NativeSocket = (Func<SignalConnectionGroupValues, EntityPartSocket, double>)ManagedMethod.Clone(AccessTools.Method(typeof(SignalConnectionGroupValues), "GetSocketSignal"), (codes, _) => codes);
    private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static StatefulPart Part(ushort id, IStatefulPartState state) { var part = Empty<StatefulPart>(); part.Id = id; part.State = state; return part; }

    private sealed class Fixture
    {
        internal readonly ConnectionGroup Group = new ConnectionGroup(9);
        internal readonly Dictionary<ushort, StatefulPart> Parts = new Dictionary<ushort, StatefulPart>();
        internal readonly ShipsServerTracker Provider;
        internal readonly SignalConnectionGroupValues Values;
        internal Fixture()
        {
            var state = Empty<ShipState>(); AccessTools.Field(typeof(ShipState), "_statefulParts").SetValue(state, Parts);
            var ship = Empty<TrackedShipServer>(); AccessTools.Field(typeof(TrackedShip), "<State>k__BackingField").SetValue(ship, state);
            Provider = Empty<ShipsServerTracker>(); AccessTools.Field(typeof(ShipsServerTracker), "_shipsDict").SetValue(Provider, new Dictionary<uint, TrackedShipServer> { [1] = ship });
            var map = new ConnectionGroupMap();
            ((Dictionary<uint, ConnectionGroup>)AccessTools.Field(typeof(ConnectionGroupMap), "_connectionGroups").GetValue(map)!)[9] = Group;
            Values = new SignalConnectionGroupValues(map, Provider);
        }
        internal void Compare()
        {
            NativeUpdate(Values, 9);
            var expected = ((Dictionary<uint, double>)AccessTools.Field(typeof(SignalConnectionGroupValues), "_groupSignalValues").GetValue(Values)!)[9];
            Assert.True(SignalReduction.TryReduce(Group, Provider, SignalConnectionScan.Read(Group), out var actual));
            Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
        }
    }

    private static IStatefulPartState[] States(double number) => new IStatefulPartState[] {
        new LeverState { LeverHeight = (float)number }, new PilotSeatState { MoveInputY = (float)number, MoveInputZ = (float)-number },
        new PilotSeatAdvState { MoveInput = new Vector3((float)number, (float)-number, (float)number), RotationInput = new Vector3((float)-number, (float)number, (float)-number) },
        new TargetingComputerState { RelativePosition = new Vector3Double(number, -number, number) },
        new MathGateState { Output = number }, new SignalProcessorState { Output = number }, new ConstantState { ConstantValue = number },
        new DeltaState { DeltaValue = number }, new MemoryRegisterState { Value = number }, new VelocitySensorState { Velocity = number },
        new AngularVelSensorState { AngularVel = number }, new GasSensorState { Output = number }, new FluidTankState { FluidQuantity = (float)number }
    };

    [Fact]
    public void AllKnownStatesMatchNativeSocketBitsIncludingNonfiniteValuesAndEverySocketId()
    {
        var fixture = new Fixture();
        foreach (var number in new[] { 0.0, -0.0, 3.0, -3.0, double.NaN, BitConverter.Int64BitsToDouble(unchecked((long)0xfff8000000000421)), double.PositiveInfinity, double.NegativeInfinity, double.MaxValue, double.Epsilon })
        {
            foreach (var state in States(number))
            {
                fixture.Parts[1] = Part(1, state);
                for (var socket = 0; socket <= byte.MaxValue; socket++)
                {
                    var native = NativeSocket(fixture.Values, new EntityPartSocket(1, 1, (byte)socket));
                    Assert.True(SignalReduction.TryRead(state, (byte)socket, out var candidate));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(native), BitConverter.DoubleToInt64Bits(candidate));
                }
            }
        }
    }

    [Fact]
    public void NativeOrderedGroupResultsSurviveStateReplacementAndDirectTopologyMutations()
    {
        SignalConnectionScan.Clear();
        try
        {
            var fixture = new Fixture(); var random = new System.Random(231026); var states = States(-2.0);
            for (ushort i = 0; i < states.Length; i++) fixture.Parts[i] = Part(i, states[i]);
            SocketConnection Wire(int id) => new SocketConnection(
                new EntityPartSocket((uint)(id % 11 == 0 ? 2 : 1), (ushort)(id % 15), (byte)(id % 7)),
                new EntityPartSocket(1, (ushort)((id * 3 + 1) % 15), (byte)((id / 3) % 7)), SocketConnectionType.Cable) { CableId = (uint)id };
            for (var i = 0; i < 2500; i++)
            {
                var wire = Wire(random.Next(80));
                if (i % 3 == 0) fixture.Group.RemoveConnection(wire); else fixture.Group.AddConnection(wire);
                var state = States(i % 5 == 0 ? double.NaN : i % 17 - 8)[i % states.Length];
                fixture.Parts[(ushort)(i % states.Length)] = Part((ushort)(i % states.Length), state);
                if (i % 23 == 0 && fixture.Group.Sockets.Count > 0)
                {
                    var key = fixture.Group.Sockets.Keys.First();
                    fixture.Group.Sockets[key] = new HashSet<SocketConnection>(fixture.Group.Sockets[key].Reverse());
                }
                if (i % 31 == 0 && fixture.Group.Sockets.Count > 0)
                {
                    var set = fixture.Group.Sockets.Values.First();
                    if (set.Count > 0) { set.Remove(set.First()); set.Add(Wire(100 + i)); }
                }
                if (i % 47 == 0) fixture.Parts.Remove((ushort)(i % states.Length));
                fixture.Compare();
            }
            fixture.Group.Sockets.Clear(); fixture.Compare();
        }
        finally { SignalConnectionScan.Clear(); }
    }

    private sealed record DerivedConstant : ConstantState;
    private sealed class CustomSignal : ISignalEmitterState, ISignalProcessorState
    {
        public byte[] EmissionSockets => throw new InvalidOperationException("Custom callbacks must remain native.");
        public byte[] ListeningSockets => throw new InvalidOperationException("Custom callbacks must remain native.");
        public bool HasEmissionSocketId(byte id) => throw new InvalidOperationException("Custom callbacks must remain native.");
        public bool HasListeningSocketId(byte id) => throw new InvalidOperationException("Custom callbacks must remain native.");
    }

    [Fact]
    public void DerivedAndCustomSignalStatesFallbackBeforeInvokingTheirCallbacks()
    {
        var fixture = new Fixture();
        fixture.Group.AddConnection(new SocketConnection(new EntityPartSocket(1, 0, 0), new EntityPartSocket(1, 1, 0), SocketConnectionType.Cable));
        fixture.Parts[0] = Part(0, new ConstantState { ConstantValue = 99 });
        foreach (var state in new IStatefulPartState[] { new DerivedConstant { ConstantValue = -5 }, new CustomSignal() })
        {
            fixture.Parts[1] = Part(1, state);
            Assert.False(SignalReduction.TryReduce(fixture.Group, fixture.Provider, fixture.Group.AllConnections, out _));
        }
    }

    [Fact]
    public void MutableNativeEmissionArraysAndPureGetterContractsRemainLive()
    {
        var fixture = new Fixture(); fixture.Parts[0] = Part(0, new ConstantState { ConstantValue = 5 });
        fixture.Group.AddConnection(new SocketConnection(new EntityPartSocket(1, 0, 0), null, SocketConnectionType.Cable));
        var original = ConstantState._emissionSockets[0];
        try
        {
            fixture.Compare(); ConstantState._emissionSockets[0] = 3; fixture.Compare();
            fixture.Group.AddConnection(new SocketConnection(new EntityPartSocket(1, 0, 3), null, SocketConnectionType.Cable)); fixture.Compare();
        }
        finally { ConstantState._emissionSockets[0] = original; SignalConnectionScan.Clear(); }
        foreach (var type in States(0).Select(state => state.GetType()))
        {
            SignalReduction.RequirePureGetter(type, "EmissionSockets");
            if (typeof(ISignalProcessorState).IsAssignableFrom(type)) SignalReduction.RequirePureGetter(type, "ListeningSockets");
        }
    }
}
