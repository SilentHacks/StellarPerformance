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

public sealed class DistillerSlotTests
{
    [Fact]
    public void DistillerSlotReusePreservesArrayReferenceWhenSlotsAreUnchangedAndUpdatesWhenChanged()
    {
        var updateMethod = AccessTools.Method(typeof(EthanolDistillersUpdater), nameof(EthanolDistillersUpdater.Update));
        var original = ManagedMethod.Instructions(updateMethod);
        var transpiled = DistillerSlotReuse.Transpile(StatefulPartQueries.Transpile(original)).ToArray();
        Assert.Equal(original.Count, transpiled.Length);
        Assert.Single(transpiled, code => code.Calls(AccessTools.Method(typeof(DistillerSlotReuse), nameof(DistillerSlotReuse.SetOrReuseSlots))));

        var initialSlots = new[] { new InventorySlot { HasItem = true, ItemId = 42u, Quantity = 5 } };
        var originalState = new EthanolDistillerState
        {
            NormalizedProductionRate = 1f,
            IsInUse = false,
            InventorySlots = initialSlots,
            PartialBiomass = 0.5f
        };

        var clonedUnchanged = originalState with { NormalizedProductionRate = 1f, PartialBiomass = 0.5f };
        var newlyAllocatedEqualSlots = new[] { new InventorySlot { HasItem = true, ItemId = 42u, Quantity = 5 } };
        Assert.NotSame(initialSlots, newlyAllocatedEqualSlots);
        DistillerSlotReuse.SetOrReuseSlots(clonedUnchanged, newlyAllocatedEqualSlots);
        Assert.Same(initialSlots, clonedUnchanged.InventorySlots);
        Assert.Equal(originalState, clonedUnchanged);

        var clonedChanged = originalState with { NormalizedProductionRate = 1f, PartialBiomass = 0.9f };
        var newlyAllocatedChangedSlots = new[] { new InventorySlot { HasItem = true, ItemId = 42u, Quantity = 4 } };
        DistillerSlotReuse.SetOrReuseSlots(clonedChanged, newlyAllocatedChangedSlots);
        Assert.Same(newlyAllocatedChangedSlots, clonedChanged.InventorySlots);
        Assert.NotEqual(originalState, clonedChanged);
    }
}
