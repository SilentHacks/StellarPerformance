using System;
using System.Collections.Generic;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class ReliableBacklogTests
{
    private static ArraySegment<byte> Packet(params byte[] data) => new ArraySegment<byte>(data);
    private static void NoFailure(int key, string reason) => throw new Exception(reason);

    [Fact]
    public void CopiesOffsetPayloadAndPreservesOrderAndChannelAfterRepeatedRefusals()
    {
        var queue = new ReliableBacklog<int>(); var source = new byte[] { 99, 1, 2, 88 };
        Assert.True(queue.Enqueue(7, new ArraySegment<byte>(source, 1, 2), 0, 0));
        Array.Clear(source);
        Assert.True(queue.Enqueue(7, Packet(3), 2, 0));
        var sent = new List<byte>(); var channels = new List<byte>(); var attempts = 0;
        RetryResult Send(int key, ArraySegment<byte> data, byte channel)
        {
            Assert.Equal(7, key); Assert.True(data.Array!.Length >= data.Count + 2);
            if (++attempts < 4) return RetryResult.Congested;
            sent.AddRange(data); channels.Add(channel); return RetryResult.Accepted;
        }
        for (var i = 1; i < 20; i++) queue.Drain(i * .1, Send, NoFailure, () => 0);
        Assert.Equal(new byte[] { 1, 2, 3 }, sent); Assert.Equal(new byte[] { 0, 2 }, channels);
        Assert.Equal(0, queue.Bytes); Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void CongestedPeerDoesNotBlockOtherPeersAndBackoffPreventsBusyRetries()
    {
        var queue = new ReliableBacklog<int>(); var calls = new List<int>();
        queue.Enqueue(1, Packet(1), 0, 0); queue.Enqueue(2, Packet(2), 0, 0);
        RetryResult Send(int key, ArraySegment<byte> data, byte channel) { calls.Add(key); return key == 1 ? RetryResult.Congested : RetryResult.Accepted; }
        queue.Drain(.1, Send, NoFailure, () => 0);
        Assert.Equal(new[] { 1, 2 }, calls); Assert.True(queue.Contains(1)); Assert.False(queue.Contains(2));
        queue.Drain(.101, Send, NoFailure, () => 0); Assert.Equal(2, calls.Count);
        queue.Drain(.2, Send, NoFailure, () => 0); Assert.Equal(3, calls.Count);
    }

    [Fact]
    public void CapacityAgeAndDisconnectRemoveEveryOwnedBuffer()
    {
        var queue = new ReliableBacklog<int>();
        var max = new byte[ReliableBacklog<int>.PeerByteLimit - 2];
        for (var peer = 0; peer < 4; peer++) Assert.True(queue.Enqueue(peer, Packet(max), 0, 0));
        Assert.Equal(ReliableBacklog<int>.GlobalByteLimit, queue.Bytes);
        Assert.False(queue.Enqueue(5, Packet(1), 0, 0)); Assert.False(queue.Enqueue(0, Packet(1), 0, 0));
        var failures = new List<int>();
        queue.Drain(30, (_, _, _) => throw new Exception("Expired packet sent"), (key, _) => failures.Add(key), () => 0);
        Assert.Equal(4, failures.Count); Assert.Equal(0, queue.Bytes);
        for (var i = 0; i < ReliableBacklog<int>.PeerLimit; i++) Assert.True(queue.Enqueue(i, Packet(1), 0, 0));
        Assert.False(queue.Enqueue(99, Packet(1), 0, 0));
        queue.RemoveWhere(key => key % 2 == 0); Assert.Equal(32, queue.Count);
        queue.Clear(); Assert.Equal(0, queue.Count); Assert.Equal(0, queue.Bytes);
    }

    [Fact]
    public void PacketCountAndTimeBudgetAreBoundedAndFailuresCannotLeak()
    {
        var queue = new ReliableBacklog<int>();
        for (var i = 0; i < ReliableBacklog<int>.PacketLimit; i++) Assert.True(queue.Enqueue(1, Packet(), 0, 0));
        Assert.False(queue.Enqueue(1, Packet(), 0, 0));
        double time = 0; var sent = 0;
        queue.Drain(1, (_, _, _) => { sent++; time += .002; return RetryResult.Accepted; }, NoFailure, () => time);
        Assert.Equal(1, sent);
        queue.Drain(2, (_, _, _) => RetryResult.Failed, (key, _) => { Assert.False(queue.Contains(key)); }, () => time);
        Assert.Equal(0, queue.Bytes);
        queue.Enqueue(1, Packet(1), 0, 0);
        queue.Drain(1, (key, _, _) => { queue.Remove(key); return RetryResult.Accepted; }, NoFailure, () => 0);
        Assert.Equal(0, queue.Bytes);
        queue.Enqueue(1, Packet(1), 0, 0); queue.Enqueue(2, Packet(2), 0, 0);
        var closed = 0; time = 0;
        queue.Drain(30, (_, _, _) => throw new Exception("Expired packet sent"), (_, _) => { closed++; time += .002; }, () => time);
        Assert.Equal(1, closed); Assert.Equal(1, queue.Count);
        queue.Drain(30, (_, _, _) => RetryResult.Failed, (_, _) => closed++, () => time);
        Assert.Equal(2, closed); Assert.Equal(0, queue.Bytes);
    }
}
