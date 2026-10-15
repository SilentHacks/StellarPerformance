using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class ReuseTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(33)]
    [InlineData(1000)]
    public void ExactArraysKeepNativeLengthsAndZeroInitialization(int length)
    {
        var pool = new ExactArrayPool<int>(2000);
        var first = pool.Rent(length);
        Array.Fill(first, 123);
        pool.Return(first);
        var second = pool.Rent(length);
        Assert.Same(first, second);
        Assert.Equal(length, second.Length);
        Assert.All(second, value => Assert.Equal(0, value));
        Assert.Equal(0, pool.RetainedArrays);
    }

    [Fact]
    public void OutstandingArraysNeverAliasAndOtherLengthsNeverUploadExtraCapacity()
    {
        var pool = new ExactArrayPool<int>(100);
        var first = pool.Rent(10);
        var second = pool.Rent(10);
        Assert.NotSame(first, second);
        pool.Return(first);
        Assert.Equal(9, pool.Rent(9).Length);
        Assert.Same(first, pool.Rent(10));
    }

    [Fact]
    public void RetainedMemoryAndArrayCountAreBoundedEvenWithManyLengths()
    {
        var pool = new ExactArrayPool<int>(100, 4);
        for (var length = 1; length < 10000; length++) pool.Return(new int[length]);
        Assert.InRange(pool.RetainedElements, 0, 100);
        Assert.InRange(pool.RetainedArrays, 0, 4);
        var large = pool.Rent(1000);
        pool.Return(large);
        Assert.NotSame(large, pool.Rent(1000));
        Assert.Throws<OverflowException>(() => pool.Rent(-1));
    }

    [Fact]
    public void NestedScratchScopesRestoreParentsAndClearReferenceOwnership()
    {
        var pool = new ScopedReuse<List<object>>(() => new List<object>(), items => items.Clear(), items => items.Count < 10);
        var outer = pool.Enter();
        var original = pool.Rent();
        original.Add(new object());
        var inner = pool.Enter();
        var nested = pool.Rent();
        Assert.NotSame(original, nested);
        pool.Exit(inner);
        Assert.Same(outer, pool.Current);
        Assert.Single(original);
        pool.Exit(outer);
        Assert.Null(pool.Current);
        var next = pool.Enter();
        Assert.Empty(pool.Rent());
        pool.Exit(next);
    }

    [Fact]
    public void LargeCollectionsAreClearedButNotRetained()
    {
        var pool = new ScopedReuse<List<int>>(() => new List<int>(), items => items.Clear(), items => items.Count <= 2);
        var lease = pool.Enter();
        var large = pool.Rent();
        large.AddRange(new[] { 1, 2, 3 });
        pool.Exit(lease);
        Assert.Empty(large);
        lease = pool.Enter();
        Assert.NotSame(large, pool.Rent());
        pool.Exit(lease);
    }







    [Fact]
    public void MembershipRetainsAllPartsAndReadsLiveValuesWithoutMutatingOldSnapshots()
    {
        var firstPart = new MutablePart { Registered = false, Value = 1 };
        var members = new List<MutablePart> { firstPart };
        var cache = new MembershipSnapshot<MutablePart>();
        var before = cache.Get(members);
        firstPart.Registered = true;
        firstPart.Value = 2;
        Assert.Same(before, cache.Get(members));
        Assert.True(before[0].Registered);
        Assert.Equal(2, before[0].Value);
        members.Clear();
        cache.Invalidate();
        Assert.Empty(cache.Get(members));
        Assert.Single(before);
    }

    private sealed class MutablePart
    {
        internal bool Registered;
        internal int Value;
    }
}
