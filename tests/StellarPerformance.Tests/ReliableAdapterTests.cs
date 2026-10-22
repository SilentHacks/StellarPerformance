using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using FishySteamworks.Server;
using HarmonyLib;
using Steamworks;
using Xunit;

namespace StellarPerformance.Tests;

[Collection("Diagnostic counters")]
public sealed class ReliableAdapterTests
{
    [Fact]
    public void RealRefusalRetainsPrefixPayloadBeforeNativeAppendsItsChannel()
    {
        ReliableTransport.Shutdown(); Assert.False(NetworkCapture.Active);
        try
        {
            var owner = new ServerSocket(); var connection = new HSteamNetConnection(71);
            var original = new byte[] { 1, 2, 99, 99 };
            var before = AccessTools.Method(typeof(ReliableTransport), "BeforeSend");
            var after = AccessTools.Method(typeof(ReliableTransport), "AfterSend");
            object?[] call = { owner, connection, new ArraySegment<byte>(original, 0, 2), (byte)0, EResult.k_EResultOK, null };
            Assert.True((bool)before.Invoke(null, call)!);
            // Stock Send rewrites its local segment to include the channel byte.
            original[2] = 0; call[2] = new ArraySegment<byte>(original, 0, 3);
            object?[] result = { owner, connection, (byte)0, EResult.k_EResultLimitExceeded, call[5], true };
            after.Invoke(null, result); Assert.Equal(EResult.k_EResultOK, result[3]); Array.Clear(original);
            object?[] follower = { owner, connection, new ArraySegment<byte>(new byte[] { 3 }), (byte)2, EResult.k_EResultOK, null };
            Assert.False((bool)before.Invoke(null, follower)!);
            object?[] unreliable = { owner, connection, new ArraySegment<byte>(new byte[] { 4 }), (byte)1, EResult.k_EResultOK, null };
            Assert.True((bool)before.Invoke(null, unreliable)!);
            var queue = AccessTools.Field(typeof(ReliableTransport), "Backlog").GetValue(null)!;
            var drain = typeof(ReliableAdapterTests).GetMethod(nameof(Drain), BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(queue.GetType().GetGenericArguments());
            var packets = (List<byte[]>)drain.Invoke(null, new[] { queue })!;
            Assert.Equal(2, packets.Count); Assert.Equal(new byte[] { 1, 2, 0 }, packets[0]); Assert.Equal(new byte[] { 3, 2 }, packets[1]);
            object?[] ordinary = { owner, connection, new ArraySegment<byte>(new byte[] { 5, 99, 99 }, 0, 1), (byte)0, EResult.k_EResultOK, null };
            Assert.True((bool)before.Invoke(null, ordinary)!);
            object?[] invalid = { owner, connection, (byte)0, EResult.k_EResultInvalidParam, ordinary[5], true };
            after.Invoke(null, invalid); Assert.Equal(EResult.k_EResultInvalidParam, invalid[3]);
            Assert.Empty((List<byte[]>)drain.Invoke(null, new[] { queue })!);
        }
        finally { ReliableTransport.Shutdown(); }
    }
    private static List<byte[]> Drain<TKey>(object instance) where TKey : notnull
    {
        var queue = (ReliableBacklog<TKey>)instance; var data = new List<byte[]>();
        queue.Drain(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency + .1, (_, segment, channel) =>
        {
            var packet = new byte[segment.Count + 1]; Array.Copy(segment.Array!, segment.Offset, packet, 0, segment.Count);
            packet[segment.Count] = channel; data.Add(packet); return RetryResult.Accepted;
        }, (_, reason) => throw new Exception(reason), () => 0);
        return data;
    }
}
