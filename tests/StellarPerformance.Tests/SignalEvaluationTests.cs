using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ships;
using Ships.Cables;
using Ships.Cables.Model;
using Ships.Interface.Model.State;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Services;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class SignalEvaluationTests
{
    private static readonly Dictionary<ushort, StatefulPart> Parts = new Dictionary<ushort, StatefulPart>();
    private static readonly List<string> Calls = new List<string>();
    private static readonly TrackedShipServer Ship = (TrackedShipServer)RuntimeHelpers.GetUninitializedObject(typeof(TrackedShipServer));
    private static readonly IShipsProvider Provider = (IShipsProvider)RuntimeHelpers.GetUninitializedObject(typeof(Ships.Services.ShipsServerTracker));
    private static MethodInfo Method(string name) => AccessTools.Method(typeof(SignalEvaluationTests), name);
    private static StatefulPart Part(ushort id, IStatefulPartState state) { var part = (StatefulPart)RuntimeHelpers.GetUninitializedObject(typeof(StatefulPart)); part.Id = id; part.State = state; return part; }
    private static bool TryGetShip(IShipsProvider provider, uint id, out IShipStateRead ship) { Calls.Add("ship:" + id); ship = Ship; return id == 1; }
    private static bool TryGetPart(IShipStateRead ship, ushort id, out StatefulPart part) { Calls.Add("part:" + id); return Parts.TryGetValue(id, out part!); }
    private static readonly Dictionary<MethodInfo, MethodInfo> Lookups = new Dictionary<MethodInfo, MethodInfo>
    {
        [AccessTools.Method(typeof(IShipsProvider), nameof(IShipsProvider.TryGetShip))] = Method(nameof(TryGetShip)),
        [AccessTools.Method(typeof(IShipStateRead), nameof(IShipStateRead.TryGetStatefulPart))] = Method(nameof(TryGetPart))
    };
    private static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> codes, Dictionary<MethodInfo, MethodInfo> replacements) => codes.Select(original =>
    {
        var code = new CodeInstruction(original);
        if (code.operand is MethodInfo method && replacements.TryGetValue(method, out var replacement)) { code.opcode = OpCodes.Call; code.operand = replacement; }
        return code;
    });
    private static readonly Action<SignalConnectionGroupValues, EntityPartSocket, List<EntityPartSocket>, List<EntityPartSocket>> NativeAdd = (Action<SignalConnectionGroupValues, EntityPartSocket, List<EntityPartSocket>, List<EntityPartSocket>>)ManagedMethod.Clone(AccessTools.Method(typeof(SignalConnectionGroupValues), "TryAddSocket"), (codes, _) => Replace(codes, Lookups));
    private static readonly Action<SignalConnectionGroupValues, EntityPartSocket, List<EntityPartSocket>, List<EntityPartSocket>> OptimizedAdd = (Action<SignalConnectionGroupValues, EntityPartSocket, List<EntityPartSocket>, List<EntityPartSocket>>)ManagedMethod.Clone(AccessTools.Method(typeof(SignalInputProjection), nameof(SignalInputProjection.AddInput)), (codes, _) => Replace(codes, Lookups));
    private static void AddNative(SignalConnectionGroupValues values, EntityPartSocket socket, List<EntityPartSocket> inputs, List<EntityPartSocket> outputs) => NativeAdd(values, socket, inputs, outputs);
    private static void AddOptimized(SignalConnectionGroupValues values, EntityPartSocket socket, List<EntityPartSocket> inputs, List<EntityPartSocket> outputs) => OptimizedAdd(values, socket, inputs, outputs);
    private static readonly Delegate NativeQuery = ManagedMethod.Clone(AccessTools.Method(typeof(SignalConnectionGroupValues), nameof(SignalConnectionGroupValues.GetGroupInputsAndOutputs)), (codes, _) => Replace(codes, new Dictionary<MethodInfo, MethodInfo> { [AccessTools.Method(typeof(SignalConnectionGroupValues), "TryAddSocket")] = Method(nameof(AddNative)) }));
    private static readonly Delegate OptimizedQuery = ManagedMethod.Clone(AccessTools.Method(typeof(SignalInputProjection), nameof(SignalInputProjection.ReadInputs)), (codes, _) => Replace(codes, new Dictionary<MethodInfo, MethodInfo>
    {
        [AccessTools.Method(typeof(SignalInputProjection), nameof(SignalInputProjection.AddInput))] = Method(nameof(AddOptimized)),
        [AccessTools.Method(typeof(SignalConnectionGroupValues), nameof(SignalConnectionGroupValues.GetGroupInputsAndOutputs))] = Method(nameof(QueryNative))
    }));
    private static void QueryNative(SignalConnectionGroupValues values, uint id, out List<EntityPartSocket> inputs, out List<EntityPartSocket> outputs) => Query(NativeQuery, values, id, out inputs, out outputs);
    private static void QueryOptimized(SignalConnectionGroupValues values, uint id, out List<EntityPartSocket> inputs, out List<EntityPartSocket> outputs) => Query(OptimizedQuery, values, id, out inputs, out outputs);
    private static void Query(Delegate query, SignalConnectionGroupValues values, uint id, out List<EntityPartSocket> inputs, out List<EntityPartSocket> outputs)
    {
        var arguments = new object?[] { values, id, null, null };
        query.DynamicInvoke(arguments);
        inputs = (List<EntityPartSocket>)arguments[2]!;
        outputs = (List<EntityPartSocket>)arguments[3]!;
    }
    private static double SocketSignal(SignalConnectionGroupValues values, EntityPartSocket socket) => ((ConstantState)Parts[socket.Socket.PartId].State).ConstantValue;
    private static readonly Action<SignalConnectionGroupValues, uint> NativeUpdate = (Action<SignalConnectionGroupValues, uint>)ManagedMethod.Clone(AccessTools.Method(typeof(SignalConnectionGroupValues), nameof(SignalConnectionGroupValues.UpdateGroupSignalValue)), (codes, _) => Replace(codes, new Dictionary<MethodInfo, MethodInfo>
    {
        [AccessTools.Method(typeof(SignalConnectionGroupValues), nameof(SignalConnectionGroupValues.GetGroupInputsAndOutputs))] = Method(nameof(QueryNative)),
        [AccessTools.Method(typeof(SignalConnectionGroupValues), "GetSocketSignal")] = Method(nameof(SocketSignal))
    }));
    private static readonly Action<SignalConnectionGroupValues, uint> OptimizedUpdate = (Action<SignalConnectionGroupValues, uint>)ManagedMethod.Clone(AccessTools.Method(typeof(SignalConnectionGroupValues), nameof(SignalConnectionGroupValues.UpdateGroupSignalValue)), (codes, _) => Replace(SignalInputProjection.Transpile(codes), new Dictionary<MethodInfo, MethodInfo>
    {
        [AccessTools.Method(typeof(SignalInputProjection), nameof(SignalInputProjection.ReadInputs))] = Method(nameof(QueryOptimized)),
        [AccessTools.Method(typeof(SignalConnectionGroupValues), "GetSocketSignal")] = Method(nameof(SocketSignal))
    }));

    [Theory]
    [InlineData(2.0, -2.0)]
    [InlineData(double.NaN, 3.0)]
    [InlineData(double.PositiveInfinity, double.NegativeInfinity)]
    [InlineData(-0.0, 0.0)]
    public void TransformedSignalUpdatePreservesNativeFloatingPointResults(double first, double second)
    {
        Parts.Clear(); SignalConnectionReads.Reset();
        var group = new ConnectionGroup(9);
        group.AddConnection(new SocketConnection(new EntityPartSocket(1, 1, 0), new EntityPartSocket(1, 2, 0), SocketConnectionType.Cable) { CableId = 1 });
        var map = new ConnectionGroupMap(); ((Dictionary<uint, ConnectionGroup>)AccessTools.Field(typeof(ConnectionGroupMap), "_connectionGroups").GetValue(map)!)[9] = group;
        var native = new SignalConnectionGroupValues(map, Provider);
        var optimized = new SignalConnectionGroupValues(map, Provider);
        SignalInputProjection.Enter(true, out var previous);
        try
        {
            foreach (var pair in new[] { (first, second), (second, first) })
            {
                Parts[1] = Part(1, new ConstantState { ConstantValue = pair.Item1 });
                Parts[2] = Part(2, new ConstantState { ConstantValue = pair.Item2 });
                NativeUpdate(native, 9);
                OptimizedUpdate(optimized, 9);
                var expected = ((Dictionary<uint, double>)AccessTools.Field(typeof(SignalConnectionGroupValues), "_groupSignalValues").GetValue(native)!)[9];
                var actual = ((Dictionary<uint, double>)AccessTools.Field(typeof(SignalConnectionGroupValues), "_groupSignalValues").GetValue(optimized)!)[9];
                Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
            }
        }
        finally { SignalInputProjection.End(null, previous); }
    }







    [Fact]
    public void HashSetVersionPositiveControlAndThreadLocalOwnershipWork()
    {
        SignalConnectionReads.Validate(); SignalConnectionReads.Reset();
        var group = new ConnectionGroup(1); group.AddConnection(new SocketConnection(new EntityPartSocket(1, 1, 0), null, SocketConnectionType.Cable));
        var first = SignalConnectionReads.Read(group);
        IReadOnlyCollection<SocketConnection>? other = null;
        var thread = new System.Threading.Thread(() => { SignalConnectionReads.Reset(); other = SignalConnectionReads.Read(group); });
        thread.Start(); thread.Join();
        Assert.NotNull(other);
        Assert.NotSame(first, other);
        Assert.Same(first, SignalConnectionReads.Read(group));
    }

    private sealed class CustomState : ISignalEmitterState, ISignalProcessorState
    {
        public byte[] EmissionSockets => new byte[] { 2 };
        public byte[] ListeningSockets => new byte[] { 0, 1 };
        public bool HasEmissionSocketId(byte id) { Calls.Add("emit:" + id); return id == 2; }
        public bool HasListeningSocketId(byte id) { Calls.Add("listen:" + id); return id < 2; }
    }
    private sealed record DerivedMathState : MathGateState, ISignalProcessorState
    {
        byte[] ISignalProcessorState.ListeningSockets { get { Calls.Add("derived-listen"); return new byte[] { 0 }; } }
    }

    [Fact]
    public void NativeInputsAndCustomCallbacksRemainOrderedLiveAndDuplicated()
    {
        SignalConnectionReads.Reset(); Parts.Clear();
        var map = new ConnectionGroupMap();
        var group = new ConnectionGroup(7);
        var source = new EntityPartSocket(1, 1, 2);
        var destination = new EntityPartSocket(1, 2, 0);
        var first = new SocketConnection(source, destination, SocketConnectionType.Cable) { CableId = 3 };
        var second = first; second.CableId = 4;
        group.AddConnection(first); group.AddConnection(second);
        ((Dictionary<uint, ConnectionGroup>)AccessTools.Field(typeof(ConnectionGroupMap), "_connectionGroups").GetValue(map)!)[7] = group;
        var values = new SignalConnectionGroupValues(map, Provider);
        foreach (var state in new IStatefulPartState[] { new MathGateState(), new DeltaState(), new SignalProcessorState(), new CustomState(), new DerivedMathState(), new ConstantState(), new DefaultState() })
        {
            Parts[1] = Part(1, state);
            Parts[2] = Part(2, state);
            Calls.Clear(); QueryNative(values, 7, out var expected, out var expectedOutputs); var expectedCalls = Calls.ToArray();
            SignalInputProjection.Enter(true, out var previous);
            try
            {
                Calls.Clear(); QueryOptimized(values, 7, out var actual, out var outputs);
                Assert.Equal(expected, actual);
                Assert.Equal(expectedCalls, Calls);
                if (state is CustomState || state is DerivedMathState) Assert.Equal(expectedOutputs, outputs);
                else Assert.Empty(outputs);
                if (state is MathGateState) Assert.Equal(2, actual.Count(socket => socket.Equals(source)));
                if (state is CustomState) Assert.Equal(4, actual.Count(socket => socket.Equals(source)));
            }
            finally { SignalInputProjection.End(null, previous); }
        }
        Parts.Clear();
        SignalInputProjection.Enter(true, out var empty);
        try { QueryOptimized(values, 7, out var missing, out _); Assert.Empty(missing); QueryOptimized(values, 999, out var absent, out _); Assert.Empty(absent); }
        finally { SignalInputProjection.End(null, empty); }
    }

    [Fact]
    public void MembershipSnapshotsDetectPublicMutationReplacementAndEnumerationOrder()
    {
        SignalConnectionReads.Reset();
        var group = new ConnectionGroup(1);
        var socket = new EntityPartSocket(1, 2, 0);
        var first = new SocketConnection(socket, null, SocketConnectionType.Cable) { CableId = 1 };
        var second = first; second.CableId = 2;
        group.AddConnection(first);
        var reads = 0;
        IReadOnlyCollection<SocketConnection> Native(ConnectionGroup value) { reads++; return value.AllConnections; }
        var snapshot = SignalConnectionReads.Read(group, Native);
        for (var i = 0; i < 1000; i++) Assert.Same(snapshot, SignalConnectionReads.Read(group, Native));
        Assert.Equal(1, reads);
        group.Sockets[socket].Remove(first); group.Sockets[socket].Add(second);
        Assert.Equal(group.AllConnections, SignalConnectionReads.Read(group, Native));
        group.Sockets[socket] = new HashSet<SocketConnection> { first };
        Assert.Equal(group.AllConnections, SignalConnectionReads.Read(group, Native));
        group.Sockets.Clear(); group.Sockets[new EntityPartSocket(1, 3, 0)] = new HashSet<SocketConnection> { second, first };
        Assert.Equal(group.AllConnections, SignalConnectionReads.Read(group, Native));
        group.Sockets.Values.Single().Clear();
        Assert.Empty(SignalConnectionReads.Read(group, Native));
        Assert.Equal(5, reads);
        Assert.Empty(SignalConnectionReads.Read(new ConnectionGroup(1), Native));
    }

    [Fact]
    public void SignalScopeRestoresNativeBehaviourAfterNestingAndExceptions()
    {
        Parts.Clear(); Parts[1] = Part(1, new MathGateState());
        var group = new ConnectionGroup(1); group.AddConnection(new SocketConnection(new EntityPartSocket(1, 1, 0), null, SocketConnectionType.Cable));
        var map = new ConnectionGroupMap(); ((Dictionary<uint, ConnectionGroup>)AccessTools.Field(typeof(ConnectionGroupMap), "_connectionGroups").GetValue(map)!)[1] = group;
        var values = new SignalConnectionGroupValues(map, Provider);
        SignalInputProjection.Enter(true, out var previous);
        try
        {
            QueryOptimized(values, 1, out _, out var omitted); Assert.Empty(omitted);
            SignalInputProjection.Enter(false, out var nested);
            QueryOptimized(values, 1, out _, out var native); Assert.Single(native);
            var error = new InvalidOperationException("native");
            Assert.Same(error, SignalInputProjection.End(error, nested));
            QueryOptimized(values, 1, out _, out omitted); Assert.Empty(omitted);
        }
        finally { SignalInputProjection.End(null, previous); }
        QueryOptimized(values, 1, out _, out var restored); Assert.Single(restored);
    }

    [Fact]
    public void ProjectionChangesOnlyTheNativeDiscardOnlyCallAndFailsClosed()
    {
        var original = ManagedMethod.Instructions(AccessTools.Method(typeof(SignalConnectionGroupValues), nameof(SignalConnectionGroupValues.UpdateGroupSignalValue)));
        var transformed = SignalInputProjection.Transpile(original).ToArray();
        Assert.Equal(original.Count, transformed.Length);
        Assert.Single(transformed, code => code.Calls(AccessTools.Method(typeof(SignalInputProjection), nameof(SignalInputProjection.ReadInputs))));
        Assert.Throws<InvalidOperationException>(() => SignalInputProjection.Transpile(Array.Empty<CodeInstruction>()).ToArray());
        var discarded = (LocalBuilder)original[original.FindIndex(code => code.Calls(AccessTools.Method(typeof(SignalConnectionGroupValues), nameof(SignalConnectionGroupValues.GetGroupInputsAndOutputs)))) - 1].operand;
        var read = discarded.LocalIndex < 4 ? new CodeInstruction(new[] { OpCodes.Ldloc_0, OpCodes.Ldloc_1, OpCodes.Ldloc_2, OpCodes.Ldloc_3 }[discarded.LocalIndex]) : new CodeInstruction(OpCodes.Ldloc, discarded);
        var consumed = original.Concat(new[] { read });
        Assert.Throws<InvalidOperationException>(() => SignalInputProjection.Transpile(consumed).ToArray());
    }

    [Fact]
    public void OversizedSourcesAreNotRetainedAndTotalRetentionIsBounded()
    {
        SignalConnectionReads.Reset();
        var oversized = new ConnectionGroup(1);
        for (ushort i = 0; i < 1025; i++) oversized.Sockets[new EntityPartSocket(1, i, 0)] = new HashSet<SocketConnection>();
        Assert.Empty(SignalConnectionReads.Read(oversized));
        Assert.Equal(0, SignalConnectionReads.RetainedBytes);
        var groups = new List<ConnectionGroup>();
        for (uint i = 0; i < 100; i++)
        {
            var group = new ConnectionGroup(i); groups.Add(group);
            for (ushort part = 0; part < 1024; part++) group.AddConnection(new SocketConnection(new EntityPartSocket(i, part, 0), null, SocketConnectionType.Cable) { CableId = part });
            Assert.Equal(group.AllConnections, SignalConnectionReads.Read(group));
            Assert.InRange(SignalConnectionReads.RetainedBytes, 0, SignalConnectionReads.MaximumBytes);
        }
        GC.KeepAlive(groups);
    }

    [Fact]
    public void ManySmallNetworksReuseTheirNativeUnionsWithinTheExistingByteBudget()
    {
        SignalConnectionReads.Reset();
        var groups = new List<ConnectionGroup>(); var reads = 0;
        IReadOnlyCollection<SocketConnection> Native(ConnectionGroup group) { reads++; return group.AllConnections; }
        for (uint i = 0; i < 256; i++)
        {
            var group = new ConnectionGroup(i); groups.Add(group);
            group.AddConnection(new SocketConnection(new EntityPartSocket(i, 1, 0), null, SocketConnectionType.Cable));
            SignalConnectionReads.Read(group, Native);
        }
        foreach (var group in groups) SignalConnectionReads.Read(group, Native);
        Assert.Equal(groups.Count, reads);
        Assert.InRange(SignalConnectionReads.RetainedBytes, 1, SignalConnectionReads.MaximumBytes);
    }
}
