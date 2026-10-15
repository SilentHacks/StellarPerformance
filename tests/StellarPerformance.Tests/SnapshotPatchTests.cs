using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class SnapshotPatchTests
{
    public sealed class Source
    {
        internal int Reads;
        internal int[] Items = Array.Empty<int>();
        public int[] WorldObjects { get { Reads++; return Items.ToArray(); } }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int NativeSync(Source source)
    {
        var count = source.WorldObjects.Length;
        var sum = 0;
        for (var index = 0; index < count; index++) sum += source.WorldObjects[index];
        return sum;
    }

    private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, ILGenerator generator) => WorldSnapshotPatch.Rewrite(instructions, generator, AccessTools.PropertyGetter(typeof(Source), nameof(Source.WorldObjects)));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(1000)]
    public void LocalSnapshotChangesNPlusOneMaterializationsIntoOneWithoutChangingOutput(int count)
    {
        var source = new Source { Items = Enumerable.Range(0, count).ToArray() };
        var before = NativeSync(source);
        Assert.Equal(count + 1, source.Reads);
        source.Reads = 0;
        var replacement = (Func<Source, int>)ManagedMethod.Clone(AccessTools.Method(typeof(SnapshotPatchTests), nameof(NativeSync)), Transpile);
        Assert.Equal(before, replacement(source));
        Assert.Equal(1, source.Reads);
        source.Items = new[] { 19, 23 };
        Assert.Equal(42, replacement(source));
        Assert.Equal(2, source.Reads);
    }

    [Fact]
    public void UnsupportedAccessPatternsFailClosed()
    {
        var getter = AccessTools.PropertyGetter(typeof(Source), nameof(Source.WorldObjects));
        var generator = new DynamicMethod("test", typeof(void), Type.EmptyTypes).GetILGenerator();
        Assert.Throws<InvalidOperationException>(() => WorldSnapshotPatch.Rewrite(new[] { new CodeInstruction(OpCodes.Callvirt, getter) }, generator, getter).ToArray());
    }
}
