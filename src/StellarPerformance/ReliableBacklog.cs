using System;
using System.Collections.Generic;

namespace StellarPerformance;

internal enum RetryResult { Accepted, Congested, Failed }

// Single-threaded core; the transport adapter serializes access. Each retained
// packet is owned, ordered and indivisible. Capacity includes channel headroom.
internal sealed class ReliableBacklog<TKey> where TKey : notnull
{
    internal const int PeerByteLimit = 2 * 1024 * 1024, GlobalByteLimit = 8 * 1024 * 1024;
    internal const int PeerLimit = 64, PacketLimit = 2048;
    internal const double AgeLimit = 30;
    private sealed class Packet
    {
        internal byte[] Bytes = null!;
        internal int Count;
        internal byte Channel;
        internal double Created;
    }
    private sealed class Peer
    {
        internal TKey Key = default!;
        internal readonly Queue<Packet> Packets = new Queue<Packet>();
        internal int Bytes;
        internal double NextAttempt, Backoff = .01;
    }
    private readonly Dictionary<TKey, Peer> _peers = new Dictionary<TKey, Peer>();
    private readonly List<Peer> _rotation = new List<Peer>();
    private int _cursor;
    internal int Bytes { get; private set; }
    internal int Count => _peers.Count;
    internal bool Contains(TKey key) => _peers.ContainsKey(key);

    internal bool Enqueue(TKey key, ArraySegment<byte> segment, byte channel, double now)
    {
        if (segment.Array == null || segment.Count > PeerByteLimit - 2) return false;
        var size = segment.Count + 2;
        _peers.TryGetValue(key, out var peer);
        if (Bytes > GlobalByteLimit - size || (peer == null ? Count >= PeerLimit :
            peer.Bytes > PeerByteLimit - size || peer.Packets.Count >= PacketLimit || now - peer.Packets.Peek().Created >= AgeLimit)) return false;
        var copy = new byte[size];
        Buffer.BlockCopy(segment.Array, segment.Offset, copy, 0, segment.Count);
        if (peer == null)
        {
            peer = new Peer { Key = key, NextAttempt = now + .01 };
            _peers.Add(key, peer); _rotation.Add(peer);
        }
        peer.Packets.Enqueue(new Packet { Bytes = copy, Count = segment.Count, Channel = channel, Created = now });
        peer.Bytes += size; Bytes += size;
        return true;
    }

    internal void Remove(TKey key)
    {
        if (!_peers.TryGetValue(key, out var peer)) return;
        Bytes -= peer.Bytes; _peers.Remove(key);
        var index = _rotation.IndexOf(peer);
        _rotation.RemoveAt(index);
        if (index < _cursor) _cursor--;
        if (_cursor >= _rotation.Count) _cursor = 0;
    }

    internal void RemoveWhere(Predicate<TKey> matches)
    {
        for (var i = _rotation.Count - 1; i >= 0; i--) if (matches(_rotation[i].Key)) Remove(_rotation[i].Key);
    }

    internal void Clear() { _peers.Clear(); _rotation.Clear(); Bytes = 0; _cursor = 0; }

    internal void Drain(double now, Func<TKey, ArraySegment<byte>, byte, RetryResult> send,
        Action<TKey, string> fail, Func<double> clock)
    {
        var visited = 0; var peers = Count; var attempts = 0; var bytes = 0; var deadline = clock() + .001;
        while (visited++ < peers && _rotation.Count > 0 && attempts < 64 && bytes < 128 * 1024)
        {
            if (_cursor >= _rotation.Count) _cursor = 0;
            var peer = _rotation[_cursor]; _cursor = (_cursor + 1) % _rotation.Count;
            if (now - peer.Packets.Peek().Created >= AgeLimit)
            {
                Remove(peer.Key); fail(peer.Key, "reliable backlog exceeded 30 seconds");
                if (clock() >= deadline) return;
                continue;
            }
            if (now < peer.NextAttempt) continue;
            for (var burst = 0; burst < 4 && peer.Packets.Count > 0 && attempts < 64 && bytes < 128 * 1024; burst++)
            {
                var packet = peer.Packets.Peek(); attempts++; bytes += packet.Count;
                var result = send(peer.Key, new ArraySegment<byte>(packet.Bytes, 0, packet.Count), packet.Channel);
                // A native callback may tear down the connection during a send.
                if (!_peers.TryGetValue(peer.Key, out var current) || !ReferenceEquals(current, peer)) break;
                if (result == RetryResult.Congested)
                {
                    peer.NextAttempt = now + peer.Backoff; peer.Backoff = Math.Min(.25, peer.Backoff * 2); break;
                }
                if (result == RetryResult.Failed)
                {
                    Remove(peer.Key); fail(peer.Key, "reliable retry failed"); break;
                }
                peer.Packets.Dequeue(); peer.Bytes -= packet.Bytes.Length; Bytes -= packet.Bytes.Length;
                peer.Backoff = .01; peer.NextAttempt = now;
                if (peer.Packets.Count == 0) { Remove(peer.Key); break; }
                if (clock() >= deadline) return;
            }
            if (clock() >= deadline) return;
        }
    }
}
