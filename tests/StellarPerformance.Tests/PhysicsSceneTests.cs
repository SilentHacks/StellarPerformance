using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Items.Model;
using Managers.Client;
using Managers.Server;
using Planets.EnvironmentGeneration;
using Planets.Terrain;
using Planets.Terrain.Meshes;
using Planets.Terrain.Model;
using Players.Services;
using Ships;
using Ships.Cables;
using Ships.Cables.Fluids;
using Ships.Grid;
using Ships.Grid.Model;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Model.Placement;
using Ships.Interface.Model.State;
using Ships.Interface.Settings;
using Ships.Interface.Values;
using Ships.Parts.Common.Model;
using Ships.Parts.EthanolDistiller;
using Ships.Parts.FluidPump;
using Ships.Parts.WoodPowerGenerator;
using Ships.Physics;
using Tools.Build;
using Tools.Recycler;
using UnityEngine;
using UnityEngine.LowLevel;
using WorldTracking.Interface.Model.Spaces;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class PhysicsSceneTests
{
    [Fact]
    public void PhysicsSceneTransformsGuardsAndOrderingAreExact()
    {
        var serverInterior = AccessTools.Method(typeof(WorldPhysicsServerUpdater), "UpdateShipInteriorLoading");
        var clientInterior = AccessTools.Method(typeof(WorldPhysicsClientUpdater), "UpdateShipInteriorLoading");
        var clientRigidbody = AccessTools.Method(typeof(ShipRigidbody), nameof(ShipRigidbody.SetPhysicsStateClient));
        foreach (var method in new[] { serverInterior, clientInterior, clientRigidbody })
        {
            var original = ManagedMethod.Instructions(method);
            var transformed = PhysicsSceneTransforms.TranspileExactTransformWrites(original).ToArray();
            Assert.Equal(original.Count, transformed.Length);
            Assert.Single(transformed, code => code.Calls(AccessTools.Method(typeof(PhysicsSceneTransforms), nameof(PhysicsSceneTransforms.SetPositionIfChanged))));
            Assert.Single(transformed, code => code.Calls(AccessTools.Method(typeof(PhysicsSceneTransforms), nameof(PhysicsSceneTransforms.SetRotationIfChanged))));
        }

        Assert.True(PhysicsSceneTransforms.ExactEquals(new Vector3(1f, 2f, 3f), new Vector3(1f, 2f, 3f)));
        Assert.False(PhysicsSceneTransforms.ExactEquals(new Vector3(1f, 2f, 3f), new Vector3(1f, 2f, 3f + 1e-6f)));
        var rotA = Quaternion.identity;
        var rotB = new Quaternion(0.0005f, 0f, 0f, 1f);
        Assert.True(rotA == rotB);
        Assert.False(PhysicsSceneTransforms.ExactEquals(rotA, rotB));

        var space1 = new PlanetSurfaceSpace { PlanetId = 1 };
        var space2 = new PlanetSurfaceSpace { PlanetId = 1 };
        Assert.False(PhysicsSceneTransforms.ShouldTransposeCollider(space1, space1));
        Assert.True(PhysicsSceneTransforms.ShouldTransposeCollider(space1, space2));

        var spaces = new List<PhysicsSceneCache.SpaceOrderEntry>
        {
            new PhysicsSceneCache.SpaceOrderEntry(10u, 0, 5f, null!, null!),
            new PhysicsSceneCache.SpaceOrderEntry(20u, 1, 10f, null!, null!),
            new PhysicsSceneCache.SpaceOrderEntry(30u, 2, 5f, null!, null!)
        };
        PhysicsSceneCache.SortSpacesDescendingStable(spaces);
        Assert.Equal(new uint[] { 20u, 10u, 30u }, spaces.Select(s => s.SpaceId).ToArray());
    }
}
