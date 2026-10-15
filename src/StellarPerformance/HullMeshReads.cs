using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ships.Services;
using UnityEngine;

namespace StellarPerformance;

internal static class HullMeshReads
{
    static HullMeshReads() { }

    internal sealed class Data
    {
        internal Vector3[]? Vertices, Normals;
        internal Vector4[]? Tangents;
        internal readonly Dictionary<int, int[]> Triangles = new Dictionary<int, int[]>();
    }

    private sealed class Identity : IEqualityComparer<Mesh>
    {
        public bool Equals(Mesh? x, Mesh? y) => ReferenceEquals(x, y);
        public int GetHashCode(Mesh mesh) => RuntimeHelpers.GetHashCode(mesh);
    }

    [ThreadStatic] private static ScopedReuse<Dictionary<Mesh, Data>>? _pool;
    private static ScopedReuse<Dictionary<Mesh, Data>> Pool => _pool ?? (_pool = new ScopedReuse<Dictionary<Mesh, Data>>(() => new Dictionary<Mesh, Data>(new Identity()), map => map.Clear(), map => map.Count <= 256));
    private static Dictionary<Mesh, Data>? Current => Pool.Current == null ? null : Pool.Current.Items[0];

    internal static readonly Dictionary<MethodInfo, MethodInfo> Replacements = new Dictionary<MethodInfo, MethodInfo>
    {
        [AccessTools.Method(typeof(Mesh), nameof(Mesh.GetVertices), new[] { typeof(List<Vector3>) })] = AccessTools.Method(typeof(HullMeshReads), nameof(GetVertices)),
        [AccessTools.Method(typeof(Mesh), nameof(Mesh.GetNormals), new[] { typeof(List<Vector3>) })] = AccessTools.Method(typeof(HullMeshReads), nameof(GetNormals)),
        [AccessTools.Method(typeof(Mesh), nameof(Mesh.GetTangents), new[] { typeof(List<Vector4>) })] = AccessTools.Method(typeof(HullMeshReads), nameof(GetTangents)),
        [AccessTools.Method(typeof(Mesh), nameof(Mesh.GetTriangles), new[] { typeof(List<int>), typeof(int) })] = AccessTools.Method(typeof(HullMeshReads), nameof(GetTriangles))
    };

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        foreach (var name in new[] { "BuildHullMesh", "BuildEdgeMesh" })
        {
            var method = AccessTools.Method(typeof(HullMeshBuilder), name);
            PatchSafety.RejectDuplicateCache(method, "stellarworkshop.mesh");
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(HullMeshReads), nameof(Begin)), transpiler: new HarmonyMethod(typeof(HullMeshReads), nameof(Transpile)), finalizer: new HarmonyMethod(typeof(HullMeshReads), nameof(End)));
        }
    }

    private static void Begin(out ScopedReuse<Dictionary<Mesh, Data>>.Lease __state)
    {
        __state = Pool.Enter();
        Pool.Rent();
    }

    private static Exception? End(Exception? __exception, ScopedReuse<Dictionary<Mesh, Data>>.Lease? __state)
    {
        if (__state != null) Pool.Exit(__state);
        return __exception;
    }

    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var codes = instructions.Select(code => new CodeInstruction(code)).ToList();
        var count = 0;
        foreach (var code in codes)
        {
            if (!(code.operand is MethodInfo method) || !Replacements.TryGetValue(method, out var replacement) || (code.opcode != OpCodes.Call && code.opcode != OpCodes.Callvirt)) continue;
            code.opcode = OpCodes.Call;
            code.operand = replacement;
            count++;
        }
        if (count != (__originalMethod.Name == "BuildHullMesh" ? 4 : 3)) throw new InvalidOperationException("Hull source mesh read pattern changed.");
        return codes;
    }

    private static Data GetData(Dictionary<Mesh, Data> map, Mesh mesh)
    {
        if (!map.TryGetValue(mesh, out var data)) map.Add(mesh, data = new Data());
        return data;
    }

    private static void GetVertices(Mesh mesh, List<Vector3> output)
    {
        var current = Current;
        if (current == null) { mesh.GetVertices(output); return; }
        var data = GetData(current, mesh);
        if (data.Vertices == null) { mesh.GetVertices(output); data.Vertices = output.ToArray(); return; }
        output.Clear(); output.AddRange(data.Vertices);
    }

    private static void GetNormals(Mesh mesh, List<Vector3> output)
    {
        var current = Current;
        if (current == null) { mesh.GetNormals(output); return; }
        var data = GetData(current, mesh);
        if (data.Normals == null) { mesh.GetNormals(output); data.Normals = output.ToArray(); return; }
        output.Clear(); output.AddRange(data.Normals);
    }

    private static void GetTangents(Mesh mesh, List<Vector4> output)
    {
        var current = Current;
        if (current == null) { mesh.GetTangents(output); return; }
        var data = GetData(current, mesh);
        if (data.Tangents == null) { mesh.GetTangents(output); data.Tangents = output.ToArray(); return; }
        output.Clear(); output.AddRange(data.Tangents);
    }

    private static void GetTriangles(Mesh mesh, List<int> output, int submesh)
    {
        var current = Current;
        if (current == null) { mesh.GetTriangles(output, submesh); return; }
        var data = GetData(current, mesh);
        if (!data.Triangles.TryGetValue(submesh, out var triangles)) { mesh.GetTriangles(output, submesh); data.Triangles.Add(submesh, output.ToArray()); return; }
        output.Clear(); output.AddRange(triangles);
    }
}
