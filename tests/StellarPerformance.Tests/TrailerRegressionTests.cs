using System;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class TrailerRegressionTests
{
    [Fact]
    public void NativeZeroSpareCapacityThrowsBeforeSending()
    {
        Assert.Throws<ArgumentException>(() => NativeAppend(new ArraySegment<byte>(new byte[3]), 1));
    }

    [Fact]
    public void NativeOneSpareCapacitySendsAnUnwrittenTrailer()
    {
        var buffer = new byte[] { 7, 8, 9, 0 };
        var framed = NativeAppend(new ArraySegment<byte>(buffer, 0, 3), 1);
        Assert.Equal(4, framed.Count);
        Assert.NotEqual(1, framed.Array![framed.Offset + framed.Count - 1]);
    }

    internal static ArraySegment<byte> NativeAppend(ArraySegment<byte> segment, byte channel)
    {
        if (segment.Array!.Length - 1 <= segment.Offset + segment.Count)
        {
            var resized = segment.Array;
            Array.Resize(ref resized, resized.Length + 1);
            resized[resized.Length - 1] = channel;
        }
        else segment.Array[segment.Offset + segment.Count] = channel;
        return new ArraySegment<byte>(segment.Array, segment.Offset, segment.Count + 1);
    }
}
