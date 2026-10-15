using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using HarmonyLib;
using Imports.CablesMesh;
using Managers.Server.Network;
using Ships.Cables;
using Ships.Cables.Fluids;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.State;
using Ships.Services;
using UnityEngine;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class NativeContractTests
{
    [Fact]
    public void MeasuredOptimizationsDefaultOnWhileFallbackExperimentsAndDiagnosticsStayOff()
    {
        var codes = ManagedMethod.Instructions(AccessTools.Method(typeof(PerformancePlugin), nameof(PerformancePlugin.OnLateInitializeMelon)));
        Assert.DoesNotContain(codes, code => code.opcode == OpCodes.Ldstr && (Equals(code.operand, "SleepingMachinery") || Equals(code.operand, "IdleMachineProbeSeconds")));
        foreach (var setting in new[] { "FluidGraphSocketReads", "Diagnostics", "BatchingContinuousValidation", "SignalEvaluationReads", "IncrementalRooms", "ShipPartBatching", "RendererDetail", "StableSignals" })
        {
            var index = codes.FindIndex(code => code.opcode == OpCodes.Ldstr && Equals(code.operand, setting));
            Assert.True(index >= 0);
            var enabled = setting == "SignalEvaluationReads" || setting == "IncrementalRooms" || setting == "ShipPartBatching" || setting == "RendererDetail" || setting == "StableSignals";
            Assert.Equal(enabled ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0, codes[index + 1].opcode);
        }
    }

    [Fact]
    public void PluginUpdateAndItsHelpersNeverReadDisabledLegacyInput()
    {
        var pending = new Stack<MethodInfo>();
        var visited = new HashSet<MethodInfo>();
        pending.Push(typeof(PerformancePlugin).GetMethod(nameof(PerformancePlugin.OnUpdate))!);
        while (pending.Count > 0)
        {
            var method = pending.Pop();
            if (!visited.Add(method) || method.GetMethodBody() == null) continue;
            foreach (var code in ManagedMethod.Instructions(method))
            {
                if (!(code.operand is MethodInfo called)) continue;
                Assert.NotEqual(typeof(UnityEngine.Input), called.DeclaringType);
                if (called.DeclaringType?.Assembly == typeof(PerformancePlugin).Assembly) pending.Push(called);
            }
        }
    }

    [Fact]
    public void EveryModuleGuardMatchesTheActualInstalledBinary()
    {
        var game = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute => attribute.Key == "GameDirectory").Value!;
        foreach (var pair in Compatibility.ExpectedModules)
        {
            if (!OptionalModTests.Enabled && (pair.Key == "SDModKit" || pair.Key == "ItemLines")) continue;
            var folder = pair.Key == "SDModKit" || pair.Key == "ItemLines" ? "Mods" : "StellarDrive_Data/Managed";
            using var stream = File.OpenRead(Path.Combine(game, folder, pair.Key + ".dll"));
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            Assert.True(Compatibility.Matches(pair.Key, metadata.GetGuid(metadata.GetModuleDefinition().Mvid)));
        }
        Assert.False(Compatibility.Matches("CablesMesh", Guid.Empty));
        Assert.False(Compatibility.Matches("AnUnknownOptionalMod", Guid.Empty));
    }

    [Fact]
    public void WorldSyncHasExactlyTheInspectedTwoSnapshotReads()
    {
        var method = AccessTools.Method(typeof(MasterSyncGameObject), nameof(MasterSyncGameObject.SyncUnreliable));
        var generator = new DynamicMethod("sync", typeof(void), Type.EmptyTypes).GetILGenerator();
        var original = ManagedMethod.Instructions(method, generator);
        var transformed = WorldSnapshotPatch.Transpile(original, generator).ToArray();
        Assert.Equal(original.Count + 3, transformed.Length);
        Assert.Equal(1, transformed.Count(code => code.operand is MethodInfo called && called.Name == "get_WorldObjects"));
        Assert.Equal(2, original.Count(code => code.operand is MethodInfo called && called.Name == "get_WorldObjects"));
    }

    [Fact]
    public void CablePoolTranspilerReplacesOnlyTheFiveAllocations()
    {
        var method = AccessTools.Method(typeof(ExtrudableCableMeshGenerator), nameof(ExtrudableCableMeshGenerator.GenerateCableMesh));
        var original = ManagedMethod.Instructions(method);
        var transformed = CableBuffers.Transpile(original).ToArray();
        Assert.Equal(original.Count, transformed.Length);
        Assert.Equal(5, original.Count(code => code.opcode == OpCodes.Newarr));
        Assert.DoesNotContain(transformed, code => code.opcode == OpCodes.Newarr);
        Assert.Equal(2, transformed.Count(code => code.Calls(AccessTools.Method(typeof(CableBuffers), nameof(CableBuffers.RentVector3)))));
        Assert.Equal(2, transformed.Count(code => code.Calls(AccessTools.Method(typeof(CableBuffers), nameof(CableBuffers.RentVector2)))));
        Assert.Equal(1, transformed.Count(code => code.Calls(AccessTools.Method(typeof(CableBuffers), nameof(CableBuffers.RentIndices)))));
        for (var index = 0; index < original.Count; index++)
            if (original[index].opcode != OpCodes.Newarr)
            {
                Assert.Equal(original[index].opcode, transformed[index].opcode);
                Assert.Equal(original[index].operand, transformed[index].operand);
            }
    }

    [Theory]
    [InlineData("BuildHullMesh", 4)]
    [InlineData("BuildEdgeMesh", 3)]
    public void HullReadCachePreservesAllOtherNativeInstructions(string name, int reads)
    {
        var method = AccessTools.Method(typeof(HullMeshBuilder), name);
        var original = ManagedMethod.Instructions(method);
        var transformed = HullMeshReads.Transpile(original, method).ToArray();
        Assert.Equal(reads, transformed.Count(code => code.operand is MethodInfo called && called.DeclaringType == typeof(HullMeshReads)));
        Assert.Equal(original.Count, transformed.Length);
        for (var index = 0; index < original.Count; index++)
            if (!(original[index].operand is MethodInfo called) || !HullMeshReads.Replacements.ContainsKey(called))
            {
                Assert.Equal(original[index].opcode, transformed[index].opcode);
                Assert.Equal(original[index].operand, transformed[index].operand);
            }
        Assert.Contains(HullMeshReads.Replacements.Keys, methodInfo => methodInfo.Name == "GetTangents");
        Assert.Equal(new[] { typeof(List<int>), typeof(int) }, HullMeshReads.Replacements.Keys.Single(methodInfo => methodInfo.Name == "GetTriangles").GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Theory]
    [InlineData("AddStatefulPart")]
    [InlineData("AddStatefulPartWithoutInit")]
    [InlineData("RemoveStatefulPart")]
    public void MembershipInvalidationIsInsertedImmediatelyAfterNativeDictionaryMutation(string name)
    {
        var method = AccessTools.Method(typeof(ShipState), name);
        var original = ManagedMethod.Instructions(method);
        var transformed = ContentMembership.InvalidateAfterMutation(original).ToList();
        var notify = transformed.FindIndex(code => code.Calls(AccessTools.Method(typeof(ContentMembership), nameof(ContentMembership.Invalidate))));
        Assert.True(notify >= 2);
        Assert.Equal(OpCodes.Ldarg_0, transformed[notify - 1].opcode);
        var mutation = Assert.IsAssignableFrom<MethodInfo>(transformed[notify - 2].operand);
        Assert.Equal(typeof(Dictionary<ushort, StatefulPart>), mutation.DeclaringType);
        Assert.Equal(original.Count + 2, transformed.Count);
    }



    [Fact]
    public void UnexpectedNativeAllocationPatternsAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => CableBuffers.Transpile(Array.Empty<CodeInstruction>()).ToArray());
        var instructions = new[] { new CodeInstruction(OpCodes.Newarr, typeof(byte)) };
        Assert.Throws<InvalidOperationException>(() => CableBuffers.Transpile(instructions).ToArray());
        Assert.Equal(OpCodes.Newarr, instructions[0].opcode);
    }
}
