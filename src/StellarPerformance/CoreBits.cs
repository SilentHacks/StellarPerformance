using System.Runtime.InteropServices;
namespace StellarPerformance;
internal static class CoreBits
{
    [StructLayout(LayoutKind.Explicit)]
    private struct FloatBits
    {
        [FieldOffset(0)] internal float Value;
        [FieldOffset(0)] internal int Bits;
    }
    internal static bool Same(float a, float b) => new FloatBits { Value = a }.Bits == new FloatBits { Value = b }.Bits;
}
// Only explicit private validation captures enable these bounded room counters.
internal static class DiagnosticCounters
{
    internal static bool Active = false;
}
