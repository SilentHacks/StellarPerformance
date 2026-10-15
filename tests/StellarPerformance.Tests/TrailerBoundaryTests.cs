using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class TrailerBoundaryTests
{
    public static IEnumerable<object[]> Boundaries()
    {
        foreach (var offset in new[] { 0, 5 })
            foreach (var count in new[] { 0, 1, 1200, 524287, 524288 })
                foreach (var spare in new[] { 0, 1, 2, 4 })
                    foreach (byte channel in new byte[] { 0, 1 })
                        yield return new object[] { offset, count, spare, channel };
    }

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void CapacityGuardMakesTheNativeTrailerCorrectAtEveryBoundary(int offset, int count, int spare, byte channel)
    {
        var original = Enumerable.Repeat((byte)37, offset + count + spare).ToArray();
        var source = new ArraySegment<byte>(original, offset, count);
        var prepared = ChannelTrailer.EnsureCapacity(source);
        var framed = TrailerRegressionTests.NativeAppend(prepared, channel);
        Assert.Equal(count + 1, framed.Count);
        Assert.Equal(channel, framed.Array![framed.Offset + count]);
        Assert.All(framed.Take(count), value => Assert.Equal(37, value));
        Assert.All(original.Take(offset), value => Assert.Equal(37, value));
        if (spare < 2)
        {
            Assert.NotSame(original, prepared.Array);
            Assert.All(original, value => Assert.Equal(37, value));
            Assert.Equal(0, prepared.Offset);
        }
        else
        {
            Assert.Same(original, prepared.Array);
            Assert.Equal(offset, prepared.Offset);
        }
    }

    [Fact]
    public void PooledBuffersCanBeReusedOnDifferentChannelsWithoutStaleTrailers()
    {
        var buffer = new byte[50];
        var segment = new ArraySegment<byte>(buffer, 7, 12);
        foreach (byte channel in new byte[] { 1, 0, 1, 255 })
        {
            Array.Fill(buffer, (byte)42, 7, 12);
            var framed = TrailerRegressionTests.NativeAppend(ChannelTrailer.EnsureCapacity(segment), channel);
            Assert.Equal(channel, framed.Array![framed.Offset + 12]);
            Assert.All(framed.Take(12), value => Assert.Equal(42, value));
        }
    }

    [Fact]
    public void AnUnbackedSegmentIsNotAcceptedAsAValidPacket()
    {
        Assert.Throws<ArgumentException>(() => ChannelTrailer.EnsureCapacity(default));
    }
}
