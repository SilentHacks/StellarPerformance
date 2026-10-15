using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class ReleaseSafetyTests
{
    private static void Observe(out long __state) { __state = 0; }
    private static void Rewrite(ref int value) { value++; }
    private static bool SkipOriginal() => false;

    [Fact]
    public void ReplacementPolicyAllowsTimingStateButRejectsArgumentRewritesAndSkipPrefixes()
    {
        Assert.False(PatchSafety.RewritesArgumentsOrSkips(AccessTools.Method(typeof(ReleaseSafetyTests), nameof(Observe))));
        Assert.True(PatchSafety.RewritesArgumentsOrSkips(AccessTools.Method(typeof(ReleaseSafetyTests), nameof(Rewrite))));
        Assert.True(PatchSafety.RewritesArgumentsOrSkips(AccessTools.Method(typeof(ReleaseSafetyTests), nameof(SkipOriginal))));
        Assert.True(PatchSafety.HasOnlyOwners(new[] { "observer" }, new[] { "observer" }));
        Assert.False(PatchSafety.HasOnlyOwners(new[] { "observer", "replacement" }, new[] { "observer" }));
    }

    [Fact]
    public void DiagnosticConfigurationDoesNotBindPrivateGameFields()
    {
        Diagnostics.Configure("Frame");
        Assert.Equal(DiagnosticMode.Frame, Diagnostics.Mode);
        // Accessing capture/configuration must not initialize any private game bindings.
        Assert.Null(AccessTools.Field(typeof(Diagnostics), "StaticSpawnedOf").GetValue(null));
        Assert.Null(AccessTools.Field(typeof(Diagnostics), "HotspotCountOf").GetValue(null));
        Diagnostics.Configure("Lean");
    }

    [Fact]
    public void MissingAssemblyDisablesCompatibilityWithoutThrowing()
    {
        Assert.False(Compatibility.Validate(new[] { "StellarPerformance.Absent.TestModule" }, out var reason));
        Assert.Contains("not installed", reason);
    }

    [Fact]
    public void VersionSpecificBindingsCannotRunBeforeTheirFirstExplicitModuleCall()
    {
        foreach (var type in new[] { typeof(FluidCableTopology), typeof(CableVisualUpdates), typeof(PhysicsSceneCache),
            typeof(PhysicsSceneTransforms), typeof(PartVisualUpdates), typeof(LiquidVisualUpdates), typeof(WorldUiVisibility) })
            Assert.False((type.Attributes & TypeAttributes.BeforeFieldInit) != 0, type.Name);
    }

    [Fact]
    public void SceneStorageDropsInactiveObjectsAndRemainsBoundedAcrossManySpaces()
    {
        var scratch = new PhysicsSceneCache.ServerSceneScratch();
        var discarded = new List<PhysicsSceneCache.ObjectEntry>();
        var fakeObject = (GameObject)RuntimeHelpers.GetUninitializedObject(typeof(GameObject));
        discarded.Add(new PhysicsSceneCache.ObjectEntry(fakeObject, default, 1));
        scratch.SpacePool.Add(1, new object());
        scratch.EntriesBySpace.Add(1, discarded);
        for (uint id = 2; id < 1000; id++)
        {
            scratch.SpacePool[id] = new object();
            scratch.EntriesBySpace[id] = new List<PhysicsSceneCache.ObjectEntry>();
            scratch.ActiveSpaceIds.Clear();
            scratch.ActiveSpaceIds.Add(id);
            scratch.PruneInactive();
            Assert.Equal(id, Assert.Single(scratch.SpacePool).Key);
            Assert.Equal(id, Assert.Single(scratch.EntriesBySpace).Key);
        }
        Assert.Empty(discarded);
        scratch.ActiveSpaceIds.Clear();
        scratch.PruneInactive();
        Assert.Empty(scratch.SpacePool);
        Assert.Empty(scratch.EntriesBySpace);
    }
}
