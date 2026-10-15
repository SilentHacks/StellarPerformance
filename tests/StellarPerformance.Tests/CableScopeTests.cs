using System;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class CableScopeTests
{
    [Fact]
    public void ReentrantCableArraysCannotAliasAndFinalizersPreserveNativeExceptions()
    {
        CableBuffers.Begin(out var outer);
        var first = CableBuffers.RentIndices(27);
        first[0] = 42;
        CableBuffers.Begin(out var inner);
        var second = CableBuffers.RentIndices(27);
        Assert.NotSame(first, second);
        var error = new InvalidOperationException("native");
        Assert.Same(error, CableBuffers.End(error, inner));
        Assert.Equal(42, first[0]);
        Assert.Null(CableBuffers.End(null, outer));
        CableBuffers.Begin(out var fresh);
        Assert.All(CableBuffers.RentIndices(27), value => Assert.Equal(0, value));
        CableBuffers.End(null, fresh);
    }

    [Fact]
    public void WarmingFixedCableShapesRemovesPerCallManagedAllocationsFromThePool()
    {
        void RentAndReturn()
        {
            CableBuffers.Begin(out var state);
            CableBuffers.RentVector3(60);
            CableBuffers.RentVector3(60);
            CableBuffers.RentVector2(60);
            CableBuffers.RentVector2(60);
            CableBuffers.RentIndices(120);
            CableBuffers.End(null, state);
        }
        for (var index = 0; index < 100; index++) RentAndReturn();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1000; index++) RentAndReturn();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }
}
