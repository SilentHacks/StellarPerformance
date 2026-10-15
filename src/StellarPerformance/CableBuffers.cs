using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Imports.CablesMesh;
using UnityEngine;

namespace StellarPerformance;

internal static class CableBuffers
{
    static CableBuffers() { }

    internal sealed class Scope
    {
        internal Scope? Parent;
        internal readonly List<Vector3[]> Vectors3 = new List<Vector3[]>();
        internal readonly List<Vector2[]> Vectors2 = new List<Vector2[]>();
        internal readonly List<int[]> Indices = new List<int[]>();
    }

    private sealed class Storage
    {
        internal readonly ExactArrayPool<Vector3> Vectors3 = new ExactArrayPool<Vector3>(262144);
        internal readonly ExactArrayPool<Vector2> Vectors2 = new ExactArrayPool<Vector2>(262144);
        internal readonly ExactArrayPool<int> Indices = new ExactArrayPool<int>(262144);
        internal readonly Stack<Scope> Scopes = new Stack<Scope>();
    }

    [ThreadStatic] private static Storage? _storage;
    [ThreadStatic] private static Scope? _current;
    private static Storage Buffers => _storage ?? (_storage = new Storage());

    internal static void Install(HarmonyLib.Harmony harmony) => harmony.Patch(AccessTools.Method(typeof(ExtrudableCableMeshGenerator), nameof(ExtrudableCableMeshGenerator.GenerateCableMesh)), prefix: new HarmonyMethod(typeof(CableBuffers), nameof(Begin)), transpiler: new HarmonyMethod(typeof(CableBuffers), nameof(Transpile)), finalizer: new HarmonyMethod(typeof(CableBuffers), nameof(End)));

    internal static void Begin(out Scope __state)
    {
        __state = Buffers.Scopes.Count == 0 ? new Scope() : Buffers.Scopes.Pop();
        __state.Parent = _current;
        _current = __state;
    }

    internal static Exception? End(Exception? __exception, Scope? __state)
    {
        if (__state == null) return __exception;
        try
        {
            foreach (var array in __state.Vectors3) Buffers.Vectors3.Return(array);
            foreach (var array in __state.Vectors2) Buffers.Vectors2.Return(array);
            foreach (var array in __state.Indices) Buffers.Indices.Return(array);
        }
        finally
        {
            _current = __state.Parent;
            __state.Parent = null;
            __state.Vectors3.Clear(); __state.Vectors2.Clear(); __state.Indices.Clear();
            if (Buffers.Scopes.Count < 8 && __state.Vectors3.Capacity <= 8 && __state.Vectors2.Capacity <= 8 && __state.Indices.Capacity <= 8) Buffers.Scopes.Push(__state);
        }
        return __exception;
    }

    internal static Vector3[] RentVector3(int length)
    {
        if (_current == null) return new Vector3[length];
        var array = Buffers.Vectors3.Rent(length);
        _current.Vectors3.Add(array);
        return array;
    }

    internal static Vector2[] RentVector2(int length)
    {
        if (_current == null) return new Vector2[length];
        var array = Buffers.Vectors2.Rent(length);
        _current.Vectors2.Add(array);
        return array;
    }

    internal static int[] RentIndices(int length)
    {
        if (_current == null) return new int[length];
        var array = Buffers.Indices.Rent(length);
        _current.Indices.Add(array);
        return array;
    }

    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.Select(code => new CodeInstruction(code)).ToList();
        var counts = new int[3];
        foreach (var code in codes)
        {
            if (code.opcode != OpCodes.Newarr) continue;
            var index = code.operand as Type == typeof(Vector3) ? 0 : code.operand as Type == typeof(Vector2) ? 1 : code.operand as Type == typeof(int) ? 2 : -1;
            if (index < 0) throw new InvalidOperationException("Unexpected cable mesh array type.");
            code.opcode = OpCodes.Call;
            code.operand = AccessTools.Method(typeof(CableBuffers), index == 0 ? nameof(RentVector3) : index == 1 ? nameof(RentVector2) : nameof(RentIndices));
            counts[index]++;
        }
        if (!counts.SequenceEqual(new[] { 2, 2, 1 })) throw new InvalidOperationException("Cable mesh allocation pattern changed.");
        return codes;
    }
}
