using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Ships.Interface.Model;
using Ships.Interface.Model.State;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class OptionalAdapterTests
{
    [Fact]
    public void RuntimeHelpersHaveNoHardReferenceToSDModKit()
    {
        Assert.DoesNotContain(typeof(ContentMembership).Assembly.GetReferencedAssemblies(), assembly => assembly.Name == "SDModKit");
    }

    [OptionalModFact]
    public void OptionalAdapterBindsAnExactTypedPrefixWithoutExecutingNativeContent()
    {
        var assembly = Assembly.Load("SDModKit");
        var runtime = assembly.GetType("SDModKit.Content.ContentRuntime", true)!;
        var partsOn = AccessTools.Method(runtime, "PartsOn");
        var record = partsOn.ReturnType.GetGenericArguments()[0];
        var patch = typeof(TypedContent<>).MakeGenericType(record);
        AccessTools.Method(patch, "Configure").Invoke(null, new object[] { assembly });
        var prefix = AccessTools.Method(patch, "BeforePartsOn");
        Assert.Equal(typeof(IShipStateWrite), prefix.GetParameters()[0].ParameterType);
        Assert.Equal(partsOn.ReturnType.MakeByRefType(), prefix.GetParameters()[1].ParameterType);
        var arguments = new object?[] { null, null };
        Assert.True((bool)prefix.Invoke(null, arguments)!);
        Assert.Null(arguments[1]);
    }

    [Fact]
    public void MembershipCacheDoesNotKeepDestroyedShipStatesAlive()
    {
        var weak = CacheTemporaryShip();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(weak.IsAlive);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CacheTemporaryShip()
    {
        var ship = new ShipState(42, 0, null!);
        ContentMembership.Snapshot(ship, ship.GetStatefulParts());
        return new WeakReference(ship);
    }
}
