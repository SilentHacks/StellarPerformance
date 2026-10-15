using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Ships.Cables;
using Ships.Cables.Fluids;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Services;
using Ships.Interface.Settings;
using UnityEngine;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class FluidSocketReadTests
{
    private static int _reads;
    private static readonly Dictionary<FluidSocketReads.Key, SocketObject[]> Components = new Dictionary<FluidSocketReads.Key, SocketObject[]>();
    private static readonly Dictionary<PartContext, StatefulPart> Parts = new Dictionary<PartContext, StatefulPart>();
    private static readonly Func<GameObject, bool, SocketObject[]> Read = (Func<GameObject, bool, SocketObject[]>)ManagedMethod.Clone(AccessTools.Method(typeof(FluidSocketReads), nameof(FluidSocketReads.GetSockets)), (codes, _) => Replace(codes, new Dictionary<MethodInfo, MethodInfo> { [FluidSocketReads.InactiveRead] = Method(nameof(EngineRead)) }));
    private static SocketObject[] EngineRead(GameObject prefab, bool includeInactive)
    {
        _reads++;
        if (ReferenceEquals(prefab, null)) throw new NullReferenceException();
        return (SocketObject[])Components[new FluidSocketReads.Key(prefab, includeInactive)].Clone();
    }
    private static SocketObject[] CachedActive(GameObject prefab) => Read(prefab, false);
    private static SocketObject[] CachedSockets(GameObject prefab, bool includeInactive) => Read(prefab, includeInactive);
    private static ConnectionGroupMap _contentMap = null!;
    private static IShipsProvider ContentShips(Traverse traverse) => Uninitialized<Ships.Services.ShipsClientTracker>();
    private static ConnectionGroupMap ContentMap(ConnectionsTracker tracker) => _contentMap;
    private static bool TryGetPart(IShipsProvider ships, PartContext context, out StatefulPart part) => Parts.TryGetValue(context, out part!);
    private static bool ObjectEquals(UnityEngine.Object? a, UnityEngine.Object? b) => ReferenceEquals(a, b);
    private static MethodInfo Method(string name) => AccessTools.Method(typeof(FluidSocketReadTests), name);
    private static T Uninitialized<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> codes, Dictionary<MethodInfo, MethodInfo> replacements) => codes.Select(original =>
    {
        var code = new CodeInstruction(original);
        if (code.operand is MethodInfo called && replacements.TryGetValue(called, out var replacement)) { code.opcode = OpCodes.Call; code.operand = replacement; }
        return code;
    });

    [Fact]
    public void ReadsAreSeparatedByPrefabIdentityAndInactiveModeAndNeverSurviveAPass()
    {
        Components.Clear(); _reads = 0;
        var first = Uninitialized<GameObject>();
        var second = Uninitialized<GameObject>();
        var socket = Uninitialized<SocketObject>();
        Components[new FluidSocketReads.Key(first, false)] = new[] { socket };
        Components[new FluidSocketReads.Key(first, true)] = new[] { socket, socket };
        Components[new FluidSocketReads.Key(second, false)] = Array.Empty<SocketObject>();
        FluidSocketReads.Begin(out var scope);
        try
        {
            var active = Read(first, false);
            Assert.Same(active, Read(first, false));
            Assert.Equal(2, Read(first, true).Length);
            Assert.Empty(Read(second, false));
            Assert.Equal(3, _reads);
        }
        finally { FluidSocketReads.End(null, scope); }
        Assert.Empty(Components[new FluidSocketReads.Key(second, false)]);
        Assert.Empty(scope.Items);
        Components[new FluidSocketReads.Key(first, false)] = Array.Empty<SocketObject>();
        FluidSocketReads.Begin(out var fresh);
        try { Assert.Empty(Read(first, false)); Assert.Equal(4, _reads); }
        finally { FluidSocketReads.End(null, fresh); }
        Assert.NotSame(Read(first, false), Read(first, false));
        Assert.Equal(6, _reads);
    }

    [Fact]
    public void NestedAndExceptionalScopesRestoreParentsAndClearOwnership()
    {
        Components.Clear(); _reads = 0;
        var prefab = Uninitialized<GameObject>();
        Components[new FluidSocketReads.Key(prefab, false)] = Array.Empty<SocketObject>();
        FluidSocketReads.Begin(out var outer);
        var outerMap = outer.Items[0];
        try
        {
            var sockets = Read(prefab, false);
            FluidSocketReads.Begin(out var inner);
            var innerMap = inner.Items[0];
            var error = new InvalidOperationException("native");
            try { Assert.NotSame(sockets, Read(prefab, false)); }
            finally { Assert.Same(error, FluidSocketReads.End(error, inner)); }
            Assert.Empty(innerMap);
            Assert.Same(sockets, Read(prefab, false));
            Assert.Throws<NullReferenceException>(() => Read(null!, false));
        }
        finally { FluidSocketReads.End(null, outer); }
        Assert.Empty(outerMap);
    }

    [Fact]
    public void HugePassesBoundEntriesAndDoNotRetainOversizedMaps()
    {
        Components.Clear();
        FluidSocketReads.Begin(out var scope);
        var map = scope.Items[0];
        try
        {
            for (var i = 0; i < 2050; i++)
            {
                var prefab = Uninitialized<GameObject>();
                Components[new FluidSocketReads.Key(prefab, false)] = Array.Empty<SocketObject>();
                Read(prefab, false);
            }
            Assert.Equal(2048, map.Count);
        }
        finally { FluidSocketReads.End(null, scope); }
        Assert.Empty(map);
        FluidSocketReads.Begin(out var fresh);
        try { Assert.NotSame(map, fresh.Items[0]); }
        finally { FluidSocketReads.End(null, fresh); }
        Components.Clear();
    }

    [Fact]
    public async Task ConcurrentScopesNeverShareComponentArrays()
    {
        Components.Clear();
        var prefab = Uninitialized<GameObject>();
        Components[new FluidSocketReads.Key(prefab, false)] = new[] { Uninitialized<SocketObject>() };
        using var barrier = new Barrier(2);
        SocketObject[] OnThread()
        {
            FluidSocketReads.Begin(out var scope);
            try
            {
                var sockets = Read(prefab, false);
                Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
                Assert.Same(sockets, Read(prefab, false));
                return sockets;
            }
            finally { FluidSocketReads.End(null, scope); }
        }
        var arrays = await Task.WhenAll(Task.Run(OnThread), Task.Run(OnThread));
        Assert.NotSame(arrays[0], arrays[1]);
    }

    [Fact]
    public void NativeRelayTranspilerReplacesExactlyOneHierarchyRead() => CheckHierarchyRead(false);

    [OptionalModFact]
    public void OptionalPrefixTranspilerReplacesExactlyOneHierarchyRead() => CheckHierarchyRead(true);

    private static void CheckHierarchyRead(bool content)
    {
        var method = content ? FluidSocketReads.ContentMethod() : AccessTools.Method(typeof(ConnectionsTracker), nameof(ConnectionsTracker.GetRelayedSocketConnections));
        var original = ManagedMethod.Instructions(method);
        var patched = FluidSocketReads.Transpile(original, method).ToArray();
        var normalized = content ? FluidSocketReads.RewriteShipProvider(original) : original;
        Assert.Equal(original.Count - (content ? 3 : 0), patched.Length);
        if (content) Assert.Single(patched, code => code.Calls(AccessTools.Method(typeof(FluidSocketReads), nameof(FluidSocketReads.GetShips))));
        original = normalized;
        var changed = Enumerable.Range(0, original.Count).Where(i => !Equals(original[i].operand, patched[i].operand)).ToArray();
        var index = Assert.Single(changed);
        Assert.Equal(content ? FluidSocketReads.InactiveRead : FluidSocketReads.ActiveRead, original[index].operand);
        Assert.Equal(OpCodes.Call, patched[index].opcode);
        for (var i = 0; i < original.Count; i++)
        {
            if (i != index) { Assert.Equal(original[i].opcode, patched[i].opcode); Assert.Equal(original[i].operand, patched[i].operand); }
            Assert.Equal(original[i].labels, patched[i].labels);
            Assert.Equal(original[i].blocks, patched[i].blocks);
        }
        Assert.NotNull(ManagedMethod.Clone(method, (codes, _) => FluidSocketReads.Transpile(codes, method)));
        Assert.Throws<InvalidOperationException>(() => FluidSocketReads.Transpile(Array.Empty<CodeInstruction>(), method));
        Assert.Throws<InvalidOperationException>(() => FluidSocketReads.Transpile(original.Concat(original), method));
    }

    [Fact]
    public void BoundShipsGetterReadsReplacementsLiveAndDoesNotCacheWorldProviders()
    {
        var tracker = Uninitialized<ConnectionsClientTracker>();
        var field = AccessTools.Field(typeof(ConnectionsTracker), "Ships");
        Assert.Null(FluidSocketReads.GetShips(tracker));
        var first = Uninitialized<Ships.Services.ShipsClientTracker>();
        field.SetValue(tracker, first);
        Assert.Same(first, FluidSocketReads.GetShips(tracker));
        var second = Uninitialized<Ships.Services.ShipsClientTracker>();
        field.SetValue(tracker, second);
        Assert.Same(second, FluidSocketReads.GetShips(tracker));
        field.SetValue(tracker, null);
        Assert.Null(FluidSocketReads.GetShips(tracker));
    }

    [OptionalModFact]
    public void EmittedContentPrefixPreservesNativeFallbackAndLiveItemConnectionMap()
    {
        Components.Clear(); Parts.Clear(); _reads = 0;
        var prefab = Uninitialized<GameObject>();
        var socket = Uninitialized<SocketObject>();
        AccessTools.Field(typeof(SocketObject), "id").SetValue(socket, (byte)2);
        AccessTools.Field(typeof(SocketObject), "type").SetValue(socket, CableType.Fluid);
        AccessTools.Field(typeof(SocketObject), "relayedSockets").SetValue(socket, new[] { socket });
        Components[new FluidSocketReads.Key(prefab, true)] = new[] { socket };
        var settings = Uninitialized<PartSettings>();
        settings.part = prefab;
        var part = Uninitialized<StatefulPart>();
        AccessTools.Field(typeof(StatefulPart), "<Settings>k__BackingField").SetValue(part, settings);
        var entitySocket = new EntityPartSocket(1, 7, 2);
        Parts[entitySocket.PartContext] = part;
        var method = FluidSocketReads.ContentMethod();
        var original = ManagedMethod.Instructions(method);
        var getShips = original.Select(code => code.operand).OfType<MethodInfo>().Single(called => called.DeclaringType == typeof(Traverse) && called.Name == "GetValue" && called.IsGenericMethod);
        var map = original.Select(code => code.operand).OfType<MethodInfo>().Single(called => called.DeclaringType == method.DeclaringType && called.ReturnType == typeof(ConnectionGroupMap));
        var external = new Dictionary<MethodInfo, MethodInfo>
        {
            [getShips] = Method(nameof(ContentShips)),
            [map] = Method(nameof(ContentMap)),
            [AccessTools.Method(typeof(IShipsProvider), nameof(IShipsProvider.TryGetStatefulPart))] = Method(nameof(TryGetPart)),
            [AccessTools.Method(typeof(UnityEngine.Object), "op_Equality")] = Method(nameof(ObjectEquals)),
            [AccessTools.Method(typeof(FluidSocketReads), nameof(FluidSocketReads.GetSockets))] = Method(nameof(CachedSockets))
        };
        var prefix = ManagedMethod.Clone(method, (codes, _) => Replace(FluidSocketReads.Transpile(codes, method), external));
        var arguments = new object?[] { Uninitialized<ConnectionsClientTracker>(), entitySocket, null };
        AccessTools.Field(typeof(ConnectionsTracker), "Ships").SetValue(arguments[0], Uninitialized<Ships.Services.ShipsClientTracker>());
        _contentMap = new ConnectionGroupMap();
        var cable = new SocketConnection(entitySocket, null, SocketConnectionType.Cable) { CableId = 9 };
        _contentMap.OnConnectionCreate(cable);
        FluidSocketReads.Begin(out var scope);
        try
        {
            Assert.True((bool)prefix.DynamicInvoke(arguments)!);
            Assert.Null(arguments[2]);
            AccessTools.Field(typeof(SocketObject), "type").SetValue(socket, (CableType)3);
            for (var i = 0; i < 100; i++)
            {
                Assert.False((bool)prefix.DynamicInvoke(arguments)!);
                Assert.Equal(cable, Assert.Single(Assert.IsAssignableFrom<IReadOnlyCollection<SocketConnection>>(arguments[2])));
            }
            _contentMap.OnConnectionDelete(cable);
            Assert.False((bool)prefix.DynamicInvoke(arguments)!);
            Assert.Empty(Assert.IsAssignableFrom<IReadOnlyCollection<SocketConnection>>(arguments[2]));
            Parts.Clear();
            arguments[2] = null;
            Assert.True((bool)prefix.DynamicInvoke(arguments)!);
            Assert.Null(arguments[2]);
            Assert.Equal(1, _reads);
        }
        finally { FluidSocketReads.End(null, scope); }
    }

    [Fact]
    public void NativeRelayBodyKeepsConnectionLookupsAndSocketPropertiesLiveWhileRemovingRepeatedWalks()
    {
        Components.Clear(); Parts.Clear(); _reads = 0;
        var prefab = Uninitialized<GameObject>();
        var socket = Uninitialized<SocketObject>();
        AccessTools.Field(typeof(SocketObject), "id").SetValue(socket, (byte)2);
        AccessTools.Field(typeof(SocketObject), "type").SetValue(socket, CableType.Fluid);
        AccessTools.Field(typeof(SocketObject), "relayedSockets").SetValue(socket, new[] { socket });
        Components[new FluidSocketReads.Key(prefab, false)] = new[] { socket };
        var settings = Uninitialized<PartSettings>();
        settings.part = prefab;
        var part = Uninitialized<StatefulPart>();
        AccessTools.Field(typeof(StatefulPart), "<Settings>k__BackingField").SetValue(part, settings);
        var entitySocket = new EntityPartSocket(1, 7, 2);
        Parts[entitySocket.PartContext] = part;
        var tracker = Uninitialized<ConnectionsClientTracker>();
        AccessTools.Field(typeof(ConnectionsTracker), "_socketConnections").SetValue(tracker, new List<SocketConnection>());
        var connections = new ConnectionGroupMap();
        AccessTools.Field(typeof(ConnectionsTracker), "FluidGroupMap").SetValue(tracker, connections);
        var cable = new SocketConnection(entitySocket, null, SocketConnectionType.Cable) { CableId = 11 };
        connections.OnConnectionCreate(cable);
        var method = AccessTools.Method(typeof(ConnectionsTracker), nameof(ConnectionsTracker.GetRelayedSocketConnections));
        var external = new Dictionary<MethodInfo, MethodInfo>
        {
            [AccessTools.Method(typeof(IShipsProvider), nameof(IShipsProvider.TryGetStatefulPart))] = Method(nameof(TryGetPart)),
            [AccessTools.Method(typeof(UnityEngine.Object), "op_Equality")] = Method(nameof(ObjectEquals)),
            [AccessTools.Method(typeof(FluidSocketReads), nameof(FluidSocketReads.GetActiveSockets))] = Method(nameof(CachedActive)),
            [FluidSocketReads.ActiveRead] = Method(nameof(CachedActive))
        };
        var native = (Func<ConnectionsTracker, EntityPartSocket, IReadOnlyCollection<SocketConnection>>)ManagedMethod.Clone(method, (codes, _) => Replace(codes, external));
        var patched = (Func<ConnectionsTracker, EntityPartSocket, IReadOnlyCollection<SocketConnection>>)ManagedMethod.Clone(method, (codes, _) => Replace(FluidSocketReads.Transpile(codes, method), external));
        Assert.Equal(cable, Assert.Single(native(tracker, entitySocket)));
        FluidSocketReads.Begin(out var scope);
        try
        {
            for (var i = 0; i < 100; i++) Assert.Equal(cable, Assert.Single(patched(tracker, entitySocket)));
            Assert.Equal(2, _reads);
            connections.OnConnectionDelete(cable);
            Assert.Empty(patched(tracker, entitySocket));
            var replacement = new SocketConnection(entitySocket, null, SocketConnectionType.Cable) { CableId = 12 };
            connections.OnConnectionCreate(replacement);
            Assert.Equal(replacement, Assert.Single(patched(tracker, entitySocket)));
            AccessTools.Field(typeof(SocketObject), "relayedSockets").SetValue(socket, Array.Empty<SocketObject>());
            Assert.Empty(patched(tracker, entitySocket));
            AccessTools.Field(typeof(SocketObject), "relayedSockets").SetValue(socket, new[] { socket });
            AccessTools.Field(typeof(SocketObject), "type").SetValue(socket, CableType.Power);
            Assert.Throws<NotImplementedException>(() => patched(tracker, entitySocket));
            Parts.Clear();
            Assert.Empty(patched(tracker, entitySocket));
            Assert.Equal(2, _reads);
        }
        finally { FluidSocketReads.End(null, scope); }
    }
}
