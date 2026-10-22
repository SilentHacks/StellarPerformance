using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Planets.EnvironmentGeneration.Tracking;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class VisualDetailTests
{
    private static MethodInfo Helper(string name) => typeof(VisualDetail).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
    private sealed class OtherObserver : PlanetDenseEnvTreeTraversalObserver { }
    [Fact]
    public void NativePolicyPreservesEveryFloatBitAndOtherObserverRanges()
    {
        var vegetation = (Func<float, PlanetDenseEnvTreeTraversalObserver, float>)Helper("ScaleVegetation").CreateDelegate(typeof(Func<float, PlanetDenseEnvTreeTraversalObserver, float>));
        var visuals = (PlanetDenseEnvVisualsTreeTraversalObserver)RuntimeHelpers.GetUninitializedObject(typeof(PlanetDenseEnvVisualsTreeTraversalObserver));
        var other = new OtherObserver();
        try
        {
            VisualDetail.Change(PresetPolicy.Named("Balanced"));
            foreach (var bits in new[] { 0, int.MinValue, 1, 0x7f800000, unchecked((int)0xff800000), 0x7fc12345, 0x423a1481 })
            {
                var value = BitConverter.Int32BitsToSingle(bits);
                Assert.Equal(bits, BitConverter.SingleToInt32Bits(vegetation(value, visuals)));
            }
            VisualDetail.Change(PresetPolicy.Named("Aggressive"));
            Assert.Equal(50, vegetation(100, visuals));
            VisualDetail.Change(new PresetPolicy("Custom", true, 0, 0, 0, 0, 0, 1, 0, 0, 0, .1f));
            Assert.Equal(20, vegetation(100, visuals));
            Assert.Equal(20, vegetation(30, visuals));
            Assert.Equal(10, vegetation(10, visuals)); // Never increase the native radius.
            Assert.Equal(100, vegetation(100, other)); // Collision/audio/unknown observers stay native.
        }
        finally { VisualDetail.Change(PresetPolicy.Named("Balanced")); }
    }
    [Fact]
    public void InspectedNativeBodyRetainsEveryInstructionAndOnlyAddsTheVisualScale()
    {
        var source = AccessTools.Method(typeof(PlanetDenseEnvTreeTraversalObserver), "ObserveTraversal");
        var original = ManagedMethod.Instructions(source);
        var transform = Helper("Vegetation");
        var changed = ((IEnumerable<CodeInstruction>)transform.Invoke(null, new object[] { original })!).ToList();
        Assert.Equal(original.Count + 2, changed.Count);
        Assert.Equal(original, changed.Where(original.Contains));
        Assert.Single(changed, c => c.Calls(Helper("ScaleVegetation")));
        // Emit the complete body to catch invalid stacks/labels without runtime detours.
        Assert.NotNull(ManagedMethod.Clone(source, (codes, _) => (IEnumerable<CodeInstruction>)transform.Invoke(null, new object[] { codes })!));
    }
}
