using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FishNet.Transporting;
using FishySteamworks;
using FishySteamworks.Client;
using FishySteamworks.Server;
using HarmonyLib;
using Steamworks;

namespace StellarPerformance;

internal static class ReliableTransport
{
    static ReliableTransport() { }
    internal const string Owner = "stellarperformance.reliable-transport";
    private static readonly object Gate = new object();
    private static readonly ReliableBacklog<Peer> Backlog = new ReliableBacklog<Peer>();
    private static AccessTools.FieldRef<ServerSocket, BidirectionalDictionary<HSteamNetConnection, int>> Connections = null!;
    private static Func<ServerSocket, int, HSteamNetConnection, bool> StopServer = null!;
    private static Func<ClientSocket, bool> StopClient = null!;
    private static Func<CommonSocket, LocalConnectionState> State = null!;
    internal static Action<string>? Log;
    internal static bool Installed { get; private set; }
    private readonly struct Peer : IEquatable<Peer>
    {
        internal readonly CommonSocket Socket;
        internal readonly HSteamNetConnection Connection;
        internal Peer(CommonSocket socket, HSteamNetConnection connection) { Socket = socket; Connection = connection; }
        public bool Equals(Peer other) => ReferenceEquals(Socket, other.Socket) && Connection == other.Connection;
        public override bool Equals(object? obj) => obj is Peer other && Equals(other);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(Socket) * 397 ^ Connection.GetHashCode();
    }
    private static double Now() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private struct SendState
    {
        internal bool Attempt;
        internal ArraySegment<byte> Payload;
    }

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var send = AccessTools.Method(typeof(CommonSocket), "Send");
        var state = AccessTools.Method(typeof(CommonSocket), "SetLocalConnectionState");
        var stop = AccessTools.Method(typeof(ServerSocket), "StopConnection", new[] { typeof(int), typeof(HSteamNetConnection) });
        PatchSafety.RequireNoForeignOwners(send, NetworkCapture.Owner);
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(typeof(SteamNetworkingSockets), "SendMessageToConnection"), NetworkCapture.Owner);
        Connections = AccessTools.FieldRefAccess<ServerSocket, BidirectionalDictionary<HSteamNetConnection, int>>("_steamConnections");
        StopServer = AccessTools.MethodDelegate<Func<ServerSocket, int, HSteamNetConnection, bool>>(stop);
        StopClient = AccessTools.MethodDelegate<Func<ClientSocket, bool>>(AccessTools.Method(typeof(ClientSocket), "StopConnection", Type.EmptyTypes));
        State = AccessTools.MethodDelegate<Func<CommonSocket, LocalConnectionState>>(AccessTools.Method(typeof(CommonSocket), "GetLocalConnectionState"));
        harmony.Patch(send, prefix: new HarmonyMethod(typeof(ReliableTransport), nameof(BeforeSend)), postfix: new HarmonyMethod(typeof(ReliableTransport), nameof(AfterSend)));
        harmony.Patch(state, prefix: new HarmonyMethod(typeof(ReliableTransport), nameof(BeforeState)));
        harmony.Patch(stop, prefix: new HarmonyMethod(typeof(ReliableTransport), nameof(BeforePeerStop)));
        harmony.Patch(AccessTools.Method(typeof(ServerSocket), "RemoveConnection"), prefix: new HarmonyMethod(typeof(ReliableTransport), nameof(BeforeRemove)));
        Installed = true;
    }

    private static bool Known(CommonSocket socket) => socket.GetType() == typeof(ServerSocket) || socket.GetType() == typeof(ClientSocket);
    private static bool BeforeSend(CommonSocket __instance, HSteamNetConnection steamConnection, ArraySegment<byte> segment, byte channelId, ref EResult __result, out SendState __state)
    {
        // The native body rewrites its segment argument to include the channel.
        // Retention must use the prefix snapshot, including on a real refusal.
        __state = new SendState { Payload = segment };
        if (channelId == 1 || !Known(__instance)) return true;
        lock (Gate)
        {
            var key = new Peer(__instance, steamConnection);
            if (!Backlog.Contains(key)) { __state.Attempt = true; return true; }
            __result = Retain(key, segment, channelId) ? EResult.k_EResultOK : EResult.k_EResultNoConnection;
            return false;
        }
    }
    private static void AfterSend(CommonSocket __instance, HSteamNetConnection steamConnection, byte channelId, ref EResult __result, SendState __state, bool __runOriginal)
    {
        if (__runOriginal && NetworkCapture.Active) NetworkCapture.NativeAttempt(steamConnection, __state.Payload.Count + 1, __result);
        if (!__state.Attempt || __result != EResult.k_EResultLimitExceeded) return;
        lock (Gate) __result = Retain(new Peer(__instance, steamConnection), __state.Payload, channelId) ? EResult.k_EResultOK : EResult.k_EResultNoConnection;
    }
    private static bool Retain(Peer peer, ArraySegment<byte> segment, byte channel)
    {
        if (!Backlog.Enqueue(peer, segment, channel, Now()))
        {
            Backlog.Remove(peer); Fail(peer, "reliable backlog capacity exceeded"); return false;
        }
        if (NetworkCapture.Active) NetworkCapture.Retained(Backlog.Bytes);
        return true;
    }
    private static void BeforePeerStop(ServerSocket __instance, HSteamNetConnection socket)
    { lock (Gate) Backlog.Remove(new Peer(__instance, socket)); }
    private static void BeforeRemove(ServerSocket __instance, int connectionId)
    {
        lock (Gate) if (Connections(__instance).Second.TryGetValue(connectionId, out var socket)) Backlog.Remove(new Peer(__instance, socket));
    }
    private static void BeforeState(CommonSocket __instance, LocalConnectionState connectionState)
    {
        if (connectionState == LocalConnectionState.Stopped || connectionState == LocalConnectionState.Stopping)
            lock (Gate) Backlog.RemoveWhere(peer => ReferenceEquals(peer.Socket, __instance));
    }
    internal static void Update()
    {
        lock (Gate)
        {
            if (Backlog.Count != 0) Backlog.Drain(Now(), Retry, Fail, Now);
        }
    }
    private static RetryResult Retry(Peer peer, ArraySegment<byte> segment, byte channel)
    {
        // The owned copy has two spare bytes, satisfying the stock channel-byte
        // contract without depending on FishNet's pooled source buffer lifetime.
        if (State(peer.Socket) != LocalConnectionState.Started) return RetryResult.Failed;
        var pin = default(GCHandle);
        try
        {
            segment.Array![segment.Count] = channel;
            pin = GCHandle.Alloc(segment.Array, GCHandleType.Pinned);
            var result = SteamNetworkingSockets.SendMessageToConnection(peer.Connection, pin.AddrOfPinnedObject(), (uint)segment.Count + 1, channel == 1 ? 0 : 8, out _);
            if (NetworkCapture.Active) NetworkCapture.RetryAttempt(peer.Connection, segment.Count + 1, result);
            return result == EResult.k_EResultOK ? RetryResult.Accepted : result == EResult.k_EResultLimitExceeded ? RetryResult.Congested : RetryResult.Failed;
        }
        catch (Exception error) { Log?.Invoke("Reliable retry exception: " + error.GetType().Name); return RetryResult.Failed; }
        finally { if (pin.IsAllocated) pin.Free(); }
    }
    private static void Fail(Peer peer, string reason)
    {
        Log?.Invoke(reason + "; closing the affected connection to prevent silent reliable data loss.");
        if (NetworkCapture.Active) NetworkCapture.Failed();
        if (peer.Socket is ServerSocket server)
        {
            if (Connections(server).First.TryGetValue(peer.Connection, out var id)) StopServer(server, id, peer.Connection);
        }
        else if (peer.Socket is ClientSocket client) StopClient(client);
    }
    internal static void Shutdown() { lock (Gate) Backlog.Clear(); }
}
