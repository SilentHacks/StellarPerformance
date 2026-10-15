using System;
using System.Collections.Generic;
using System.Linq;

namespace StellarPerformance;

internal sealed class ExactArrayPool<T>
{
    private readonly Dictionary<int, Stack<T[]>> _available = new Dictionary<int, Stack<T[]>>();
    private readonly int _maxElements, _maxArrays;
    internal int RetainedElements { get; private set; }
    internal int RetainedArrays { get; private set; }

    internal ExactArrayPool(int maxElements, int maxArrays = 32)
    {
        if (maxElements < 0 || maxArrays < 0) throw new ArgumentOutOfRangeException();
        _maxElements = maxElements; _maxArrays = maxArrays;
    }

    internal T[] Rent(int length)
    {
        if (_available.TryGetValue(length, out var arrays) && arrays.Count > 0)
        {
            var array = arrays.Pop();
            RetainedElements -= length;
            RetainedArrays--;
            return array;
        }
        return new T[length];
    }

    internal void Return(T[] array)
    {
        if (array.Length > _maxElements - RetainedElements || RetainedArrays >= _maxArrays) return;
        Array.Clear(array, 0, array.Length);
        if (!_available.TryGetValue(array.Length, out var arrays))
        {
            if (_available.Count >= _maxArrays)
            {
                var emptyKey = -1;
                foreach (var pair in _available) if (pair.Value.Count == 0) { emptyKey = pair.Key; break; }
                if (emptyKey < 0) return;
                _available.Remove(emptyKey);
            }
            _available.Add(array.Length, arrays = new Stack<T[]>());
        }
        arrays.Push(array);
        RetainedArrays++;
        RetainedElements += array.Length;
    }
}

internal sealed class ScopedReuse<T> where T : class
{
    internal sealed class Lease
    {
        internal readonly List<T> Items = new List<T>();
        internal Lease? Parent;
    }

    private readonly Func<T> _create;
    private readonly Action<T> _clear;
    private readonly Func<T, bool> _retain;
    private readonly Stack<T> _available = new Stack<T>();
    private readonly Stack<Lease> _leases = new Stack<Lease>();
    internal Lease? Current { get; private set; }

    internal ScopedReuse(Func<T> create, Action<T> clear, Func<T, bool> retain)
    {
        _create = create; _clear = clear; _retain = retain;
    }

    internal Lease Enter()
    {
        var lease = _leases.Count == 0 ? new Lease() : _leases.Pop();
        lease.Parent = Current;
        return Current = lease;
    }

    internal T Rent()
    {
        if (Current == null) return _create();
        var item = _available.Count == 0 ? _create() : _available.Pop();
        Current.Items.Add(item);
        return item;
    }

    internal void Exit(Lease lease)
    {
        if (!ReferenceEquals(Current, lease)) throw new InvalidOperationException("Scratch scopes must exit in nesting order.");
        try
        {
            foreach (var item in lease.Items)
            {
                var retain = _available.Count < 8 && _retain(item);
                _clear(item);
                if (retain) _available.Push(item);
            }
        }
        finally
        {
            Current = lease.Parent;
            lease.Parent = null;
            lease.Items.Clear();
            if (_leases.Count < 8 && lease.Items.Capacity <= 32) _leases.Push(lease);
        }
    }
}

internal sealed class MembershipSnapshot<T>
{
    private T[]? _snapshot;
    internal void Invalidate() => _snapshot = null;
    internal T[] Get(IEnumerable<T> members)
    {
        if (_snapshot == null) _snapshot = members.ToArray();
        return _snapshot;
    }
}
