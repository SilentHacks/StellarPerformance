using System;
using HarmonyLib;

namespace StellarPerformance;

internal static class ChannelTrailer
{
    static ChannelTrailer() { }

    internal static ArraySegment<byte> EnsureCapacity(ArraySegment<byte> segment)
    {
        var source = segment.Array ?? throw new ArgumentException("A packet must have a backing array.", nameof(segment));
        if (source.Length - segment.Offset - segment.Count >= 2) return segment;
        var buffer = new byte[checked(segment.Count + 2)];
        Buffer.BlockCopy(source, segment.Offset, buffer, 0, segment.Count);
        return new ArraySegment<byte>(buffer, 0, segment.Count);
    }
}

internal static class PacketTrailerPatch
{
    internal static void Install(HarmonyLib.Harmony harmony) => harmony.Patch(AccessTools.Method(typeof(FishySteamworks.CommonSocket), "Send"), prefix: new HarmonyMethod(typeof(PacketTrailerPatch), nameof(Begin)));
    private static void Begin(ref ArraySegment<byte> segment) => segment = ChannelTrailer.EnsureCapacity(segment);
}
