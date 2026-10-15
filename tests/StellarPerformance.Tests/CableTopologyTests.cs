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

public sealed class CableTopologyTests
{
    [Fact]
    public void FluidCableTopologyFlowAndBalanceMatchNativeFormulas()
    {
        var cache = new FluidCableTopology.UpdaterCache();
        var socketA = new EntityPartSocket(1u, 10, 2);
        var socketB = new EntityPartSocket(1u, 20, 3);
        var nodeA = FluidCableTopology.RecurseBuildNode(cache, socketA, _ => Array.Empty<SocketConnection>());
        nodeA.Parts = new[] { new FluidCableTopology.ConnectedPartEntry(socketA.PartContext, new List<byte> { 2 }) };
        var nodeB = FluidCableTopology.RecurseBuildNode(cache, socketB, _ => Array.Empty<SocketConnection>());
        nodeB.Parts = new[] { new FluidCableTopology.ConnectedPartEntry(socketB.PartContext, new List<byte> { 3 }) };

        var cable = new FluidCableTopology.CachedCable(7u, default, null!) { NodeA = nodeA, NodeB = nodeB };
        nodeA.ConnectedCables.Add(cable);
        nodeB.ConnectedCables.Add(cable);

        var producer = (StatefulPart)RuntimeHelpers.GetUninitializedObject(typeof(StatefulPart));
        producer.Id = 10;
        producer.State = new ElectrolyzerState { NormalizedProductionRate = 1f };

        var consumer = (StatefulPart)RuntimeHelpers.GetUninitializedObject(typeof(StatefulPart));
        consumer.Id = 20;
        consumer.State = new ThrusterState { ThrusterStrength = 1f };

        var nodes = new[] { nodeA, nodeB };
        FluidCableTopology.AccumulatePartFlows(nodes, ctx => ctx.PartId == 10 ? producer : consumer);
        Assert.Equal(-1f / 50f, nodeA.FluidFlow, 6);
        Assert.Equal(0.05f / 50f, nodeB.FluidFlow, 6);

        FluidCableTopology.BalanceFlows(nodes, cache.VisitedCableIds);
        Assert.Equal(0.05f / 50f, cable.Flow, 6);

        var beforeVersion = FluidCableTopology.CurrentTopologyVersion;
        FluidCableTopology.BumpTopologyVersion();
        Assert.True(FluidCableTopology.CurrentTopologyVersion > beforeVersion);
    }
}
