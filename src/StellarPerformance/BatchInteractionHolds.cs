using System;
using System.Collections.Generic;

namespace StellarPerformance;

// Hover callbacks can overlap on a part and highlight calls are idempotent.
// A missing leave keeps rendering native; overflowing the bound fails closed.
internal sealed class BatchInteractionHolds
{
    internal const int Limit = 256;
    private readonly struct Key : IEquatable<Key>
    {
        internal readonly ushort Id;
        private readonly string? _name;
        internal Key(ushort id, string? name) { Id = id; _name = name; }
        public bool Equals(Key other) => Id == other.Id && _name == other._name;
        public override bool Equals(object? obj) => obj is Key other && Equals(other);
        public override int GetHashCode() => Id * 397 ^ (_name?.GetHashCode() ?? 0);
    }
    private readonly Dictionary<Key, int> _keys = new Dictionary<Key, int>();
    private readonly Dictionary<ushort, int> _parts = new Dictionary<ushort, int>();
    private readonly List<Key> _removed = new List<Key>();
    internal bool Held(ushort id) => _parts.ContainsKey(id);
    internal bool Enter(ushort id, string name) => Add(new Key(id, name ?? string.Empty), false);
    internal bool Highlight(ushort id) => Add(new Key(id, null), true);
    internal void Leave(ushort id, string name) => Release(new Key(id, name ?? string.Empty));
    internal void Unhighlight(ushort id) => Release(new Key(id, null));
    private bool Add(Key key, bool idempotent)
    {
        _keys.TryGetValue(key, out var count);
        if (idempotent && count != 0) return true;
        if ((count == 0 && _keys.Count == Limit) || count == Limit) return false;
        _keys[key] = count + 1;
        _parts.TryGetValue(key.Id, out var parts); _parts[key.Id] = parts + 1;
        return true;
    }
    private void Release(Key key)
    {
        if (!_keys.TryGetValue(key, out var count)) return;
        if (count == 1) _keys.Remove(key); else _keys[key] = count - 1;
        var parts = _parts[key.Id];
        if (parts == 1) _parts.Remove(key.Id); else _parts[key.Id] = parts - 1;
    }
    internal void Remove(ushort id)
    {
        _removed.Clear();
        foreach (var key in _keys.Keys) if (key.Id == id) _removed.Add(key);
        foreach (var key in _removed) _keys.Remove(key);
        _removed.Clear(); _parts.Remove(id);
    }
    internal void Clear() { _keys.Clear(); _parts.Clear(); _removed.Clear(); }
}
