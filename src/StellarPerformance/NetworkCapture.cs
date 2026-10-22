using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FishySteamworks;
using HarmonyLib;
using Managers.Server.Network;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.State;
using Steamworks;

namespace StellarPerformance;

// No observer hooks or counters exist until Start is explicitly requested.
internal static class NetworkCapture
{
    internal const string Owner = "stellarperformance.network-capture";
    private sealed class Capture
    {
        internal string Path = "";
        internal double Start, End;
        internal long PartNotifications, Distiller, Suppressed, DistillerSuppressed, Packets, Bytes, Limits, Retained, HighWater, Retries, RetryBytes, RetryLimits, Failures;
        internal HarmonyLib.Harmony Harmony = null!;
        internal readonly Dictionary<uint, PeerStats> Peers = new Dictionary<uint, PeerStats>();
    }
    private sealed class PeerStats
    {
        internal double Next;
        internal int Samples, Pending, Unacked, Ping;
        internal long QueueMicroseconds;
    }
    private static Capture? _capture;
    internal static bool Active => _capture != null;
    internal static Action<string>? Log;
    private static double Now() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    internal static void Start(double seconds, string path)
    {
        if (Active || seconds <= 0 || double.IsNaN(seconds)) return;
        var capture = new Capture { Path = path, Start = Now(), End = Now() + Math.Min(600, seconds), Harmony = new HarmonyLib.Harmony(Owner) };
        try
        {
            capture.Harmony.Patch(AccessTools.Method(typeof(MasterSync), "ServerQueueStatefulPartUpdate"), prefix: new HarmonyMethod(typeof(NetworkCapture), nameof(BeforeState)) { priority = Priority.First });
            if (!ReliableTransport.Installed) capture.Harmony.Patch(AccessTools.Method(typeof(CommonSocket), "Send"), prefix: new HarmonyMethod(typeof(NetworkCapture), nameof(BeforeSend)), postfix: new HarmonyMethod(typeof(NetworkCapture), nameof(AfterSend)));
            _capture = capture;
        }
        catch { capture.Harmony.UnpatchSelf(); throw; }
    }
    private static void BeforeState(StatefulPartStateChangeEvent partState) { var c = _capture; if (c == null) return; c.PartNotifications++; if (partState.NewState is EthanolDistillerState) c.Distiller++; }
    private static void BeforeSend(ArraySegment<byte> segment, out int __state) => __state = segment.Count + 1;
    private static void AfterSend(HSteamNetConnection steamConnection, EResult __result, bool __runOriginal, int __state)
    {
        if (__runOriginal && !ReliableTransport.Installed) NativeAttempt(steamConnection, __state, __result);
    }
    internal static void NativeAttempt(HSteamNetConnection connection, int bytes, EResult result)
    {
        var c = _capture; if (c == null) return;
        c.Packets++; c.Bytes += bytes;
        if (result == EResult.k_EResultLimitExceeded) c.Limits++;
        if (!c.Peers.TryGetValue(connection.m_HSteamNetConnection, out var stats))
        {
            if (c.Peers.Count >= 64) return;
            stats = new PeerStats(); c.Peers.Add(connection.m_HSteamNetConnection, stats);
        }
        var now = Now(); if (now < stats.Next) return;
        stats.Next = now + 1;
        try
        {
            var lane = default(SteamNetConnectionRealTimeLaneStatus_t);
            var status = default(SteamNetConnectionRealTimeStatus_t);
            if (SteamNetworkingSockets.GetConnectionRealTimeStatus(connection, ref status, 0, ref lane) != EResult.k_EResultOK) return;
            stats.Samples++; stats.Pending = Math.Max(stats.Pending, status.m_cbPendingReliable);
            stats.Unacked = Math.Max(stats.Unacked, status.m_cbSentUnackedReliable); stats.Ping = Math.Max(stats.Ping, status.m_nPing);
            stats.QueueMicroseconds = Math.Max(stats.QueueMicroseconds, status.m_usecQueueTime.m_SteamNetworkingMicroseconds);
        }
        catch (Exception) { /* Diagnostic availability must not affect transport. */ }
    }
    internal static void DuplicateSuppressed(Ships.Interface.Model.Parts.StateTypes.IStatefulPartState state) { var c = _capture; if (c == null) return; c.Suppressed++; if (state is EthanolDistillerState) c.DistillerSuppressed++; }
    internal static void Retained(int bytes) { var c = _capture; if (c != null) { c.Retained++; c.HighWater = Math.Max(c.HighWater, bytes); } }
    internal static void RetryAttempt(HSteamNetConnection connection, int bytes, EResult result) { NativeAttempt(connection, bytes, result); var c = _capture; if (c != null) { c.Retries++; c.RetryBytes += bytes; if (result == EResult.k_EResultLimitExceeded) c.RetryLimits++; } }
    internal static void Failed() { if (_capture != null) _capture.Failures++; }
    internal static void Update() { if (_capture != null && Now() >= _capture.End) Stop(); }
    internal static void Stop()
    {
        var c = _capture; if (c == null) return;
        _capture = null; c.Harmony.UnpatchSelf();
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(c.Path))!);
            File.WriteAllLines(c.Path, new[] { "seconds,distillerNotifications,suppressedIdenticalPayloads,attemptedPackets,attemptedBytes,limitExceeded,retainedPackets,backlogHighWaterBytes,retryAttempts,retryBytes,retryLimitExceeded,closedPeers,partNotifications,suppressedDistillerPayloads",
                string.Join(",", (Now()-c.Start).ToString("R", CultureInfo.InvariantCulture), c.Distiller, c.Suppressed, c.Packets, c.Bytes, c.Limits, c.Retained, c.HighWater, c.Retries, c.RetryBytes, c.RetryLimits, c.Failures, c.PartNotifications, c.DistillerSuppressed) });
            var rows = new List<string> { "connectionHandle,samples,peakPendingReliableBytes,peakUnackedReliableBytes,peakPingMs,peakQueueMicroseconds" };
            foreach (var peer in c.Peers) rows.Add(string.Join(",", peer.Key, peer.Value.Samples, peer.Value.Pending, peer.Value.Unacked, peer.Value.Ping, peer.Value.QueueMicroseconds));
            File.WriteAllLines(c.Path + ".peers.csv", rows);
            Log?.Invoke("Network capture saved: " + c.Path);
        }
        catch (Exception error) { Log?.Invoke("Network capture export failed: " + error.GetType().Name + ": " + error.Message); }
    }
}
