using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Imports.CablesMesh;
using Managers.Server.Network;
using Ships.Cables;
using Ships.Cables.Fluids;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Services;
using Xunit;

namespace StellarPerformance.Tests;

[Collection("Diagnostic counters")]
public sealed class ManagedPatchTests
{
    [Fact]
    public void EmittedMembershipBodyInvalidatesBeforeCallbacksEvenWhenTheyThrow()
    {
        var add = (Action<ShipState, StatefulPart>)ManagedMethod.Clone(AccessTools.Method(typeof(ShipState), "AddStatefulPart"), (codes, _) => ContentMembership.InvalidateAfterMutation(codes));
        var addWithoutInit = (Action<ShipState, StatefulPart>)ManagedMethod.Clone(AccessTools.Method(typeof(ShipState), "AddStatefulPartWithoutInit"), (codes, _) => ContentMembership.InvalidateAfterMutation(codes));
        var remove = (Action<ShipState, ushort>)ManagedMethod.Clone(AccessTools.Method(typeof(ShipState), "RemoveStatefulPart"), (codes, _) => ContentMembership.InvalidateAfterMutation(codes));
        var state = new ShipState(1, 2, null!);
        Assert.Empty(ContentMembership.Snapshot(state, state.GetStatefulParts()));
        var part = (StatefulPart)RuntimeHelpers.GetUninitializedObject(typeof(StatefulPart));
        part.Id = 7;
        var error = new InvalidOperationException("callback");
        state.StatefulPartAdded = added =>
        {
            Assert.Same(added, Assert.Single(ContentMembership.Snapshot(state, state.GetStatefulParts())));
            throw error;
        };
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => addWithoutInit(state, part)));
        var inFlight = ContentMembership.Snapshot(state, state.GetStatefulParts());
        state.StatefulPartRemoved = removed =>
        {
            Assert.Empty(ContentMembership.Snapshot(state, state.GetStatefulParts()));
            throw error;
        };
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => remove(state, part.Id)));
        Assert.Same(part, Assert.Single(inFlight));
        state.StatefulPartAdded = null;
        state.StatefulPartRemoved = null;
        add(state, part);
        Assert.Same(part, Assert.Single(ContentMembership.Snapshot(state, state.GetStatefulParts())));
        var replacementWorld = new ShipState(1, 2, null!);
        Assert.Empty(ContentMembership.Snapshot(replacementWorld, replacementWorld.GetStatefulParts()));
    }

    [Theory]
    [InlineData("snapshot")]
    [InlineData("buffers")]
    [InlineData("hull")]
    public void TransformedNativeBodiesCanBeEmittedWithoutEngineCallsOrRuntimeDetours(string module)
    {
        Delegate replacement;
        switch (module)
        {
            case "snapshot":
                replacement = ManagedMethod.Clone(AccessTools.Method(typeof(MasterSyncGameObject), nameof(MasterSyncGameObject.SyncUnreliable)), WorldSnapshotPatch.Transpile);
                break;
            case "buffers":
                replacement = ManagedMethod.Clone(AccessTools.Method(typeof(ExtrudableCableMeshGenerator), nameof(ExtrudableCableMeshGenerator.GenerateCableMesh)), (codes, _) => CableBuffers.Transpile(codes));
                break;
            default:
                var hull = AccessTools.Method(typeof(HullMeshBuilder), "BuildHullMesh");
                replacement = ManagedMethod.Clone(hull, (codes, _) => HullMeshReads.Transpile(codes, hull));
                break;
        }
        Assert.NotNull(replacement);
    }

    [Fact]
    public void DiagnosticAndTrailerInjectedArgumentsMatchNativeSignatures()
    {
        var send = AccessTools.Method(typeof(FishySteamworks.CommonSocket), "Send");
        Assert.Equal(typeof(ArraySegment<byte>), send.GetParameters()[1].ParameterType);
        Assert.Equal("segment", send.GetParameters()[1].Name);
        Assert.Equal("channelId", send.GetParameters()[2].Name);
        Assert.Equal(typeof(Steamworks.EResult), send.ReturnType);
        var beginSend = AccessTools.Method(typeof(Diagnostics), "BeginSend");
        Assert.Equal("segment", beginSend.GetParameters()[0].Name);
        Assert.Equal("channelId", beginSend.GetParameters()[1].Name);
        Assert.True(beginSend.GetParameters()[2].IsOut);
        var begin = AccessTools.Method(typeof(PacketTrailerPatch), "Begin");
        Assert.Equal(typeof(ArraySegment<byte>).MakeByRefType(), begin.GetParameters()[0].ParameterType);
        Assert.Equal("segment", begin.GetParameters()[0].Name);
        Assert.Equal(typeof(MethodBase), AccessTools.Method(typeof(Diagnostics), "Begin").GetParameters()[0].ParameterType);
        Assert.False(Diagnostics.Active);
        var population = AccessTools.Method(typeof(Diagnostics), "Population").GetParameters()[0];
        Assert.Equal("____ships", population.Name);
        Assert.Equal(population.ParameterType, AccessTools.Field(typeof(Managers.Server.WorldPhysicsServerUpdater), population.Name![3..]).FieldType);
        var cables = AccessTools.Method(typeof(Diagnostics), "Cables").GetParameters()[0];
        Assert.Equal("loadedCables", cables.Name);
        Assert.Equal(cables.ParameterType, AccessTools.Method(typeof(SignalCablesVisualUpdater), nameof(SignalCablesVisualUpdater.UpdateSignalCables)).GetParameters()[0].ParameterType);
    }
}
