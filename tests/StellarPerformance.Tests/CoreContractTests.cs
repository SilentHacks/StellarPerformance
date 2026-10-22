using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Xunit;

namespace StellarPerformance.Tests;

[CollectionDefinition("Diagnostic counters", DisableParallelization = true)]
public sealed class DiagnosticCounterCollection { }

public sealed class CoreContractTests
{
    [Fact]
    public void EntryPointExposesReviewedFeaturesAndExplicitDiagnostics()
    {
        var body = ManagedMethod.Instructions(AccessTools.Method(typeof(PerformancePlugin), nameof(PerformancePlugin.OnLateInitializeMelon)));
        var keys = body.Where(i => i.operand is MethodInfo m && m.Name == "CreateEntry").ToArray();
        Assert.Equal(12, keys.Length);
        var literals = body.Select(i => i.operand).OfType<string>().ToArray();
        foreach (var name in new[] { "ShipGridBounds", "FluidCableTopology", "IncrementalRooms", "SinglePlayerRooms", "ShipPartBatching", "BatchingContinuousValidation", "DistillerReplication", "PartReplicationDeduplication", "ReliableTransport", "NetworkCaptureSeconds", "SignalConnectionScan", "CanvasCulling" })
            Assert.Contains(name, literals);
        var update = ManagedMethod.Instructions(AccessTools.Method(typeof(PerformancePlugin), nameof(PerformancePlugin.OnUpdate)));
        Assert.Contains(update, i => i.operand is MethodInfo m && m.DeclaringType == typeof(PresetSelector) && m.Name == "Update");
    }

    [Fact]
    public void RetiredSimulationAndPresentationImplementationsCannotBeReactivated()
    {
        var types = typeof(PerformancePlugin).Assembly.GetTypes().Where(t => t.Namespace == "StellarPerformance").Select(t => t.Name).ToArray();
        foreach (var retired in new[] { "FrameCadence", "StableSignals", "NativeStateReuse", "PhysicsSceneCache", "PhysicsSceneTransforms", "RendererDetail", "WorldUiVisibility", "CableVisualUpdates", "PartVisualUpdates", "WorldVisualUpdates", "SignalInputProjection", "ContentMembership", "HullMeshReads", "StatefulPartQueries", "ToolRefsCache", "FluidSocketReads" })
            Assert.DoesNotContain(retired, types);
    }

    [Fact]
    public void FluidTopologyRetainsLiveFlowAndNativeVisualCallsWithoutTimeGates()
    {
        var methods = ManagedMethod.Instructions(AccessTools.Method(typeof(FluidCableTopology), "BeforeUpdateFluidCables"))
            .Select(i => i.operand).OfType<MethodInfo>().ToArray();
        foreach (var name in new[] { "AccumulatePartFlows", "BalanceFlows", "UpdateContent", "UpdateFlow" })
            Assert.Contains(methods, m => m.Name == name);
        Assert.DoesNotContain(methods, m => m.DeclaringType == typeof(UnityEngine.Time));
    }

    [Fact]
    public void RetainedPrivateBindingsHaveGuardedInitialization()
    {
        foreach (var type in new[] { typeof(ShipGridBounds), typeof(FluidCableTopology), typeof(IncrementalRooms), typeof(SinglePlayerRooms), typeof(ShipPartBatching), typeof(ReplicationDeduplication), typeof(ReliableTransport), typeof(CanvasCulling), typeof(SceneOutlines), typeof(VisualCadence) })
            Assert.False((type.Attributes & TypeAttributes.BeforeFieldInit) != 0, type.Name);
    }
}
