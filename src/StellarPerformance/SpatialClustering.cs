using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Core.Values;
using HarmonyLib;
using WorldTracking.Interface.Model;
using WorldTracking.Spaces;

namespace StellarPerformance;

// The native surface-space graph materializes every nearby edge, then enqueues
// those edges repeatedly during BFS. Keep the native node/transfer pipeline and
// its exact first-visit order; discover only nodes not already enqueued.
internal static class SpatialClustering
{
    static SpatialClustering() { }
    internal const string Owner = "stellarperformance.spatial-clustering";
    internal const int MinimumNodes = 128, NodeLimit = 32768, ByteLimit = 32 * 1024 * 1024;
    private const double PositionLimit = 1099511627776d;
    private static Type _node = null!, _array = null!, _components = null!;
    private static Func<object, Vector3Double> _position = null!;
    private static Func<object, object> _identity = null!;
    private static Func<object, int, object> _at = null!;
    private static Func<IList> _newList = null!, _newComponents = null!;
    internal static bool Initialized { get; private set; }
    internal static long Queries, Optimized, Fallbacks;
    internal static MethodInfo Target => AccessTools.Method(typeof(PlanetSurfaceSpacesServerUpdater), "UpdateClustering");

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var node = InspectedNode();
        GuardContracts(node);
        PatchSafety.RequireOriginalBody(Target, harmony.Id);
        Initialize();
        harmony.Patch(Target, transpiler: new HarmonyMethod(typeof(SpatialClustering), nameof(Rewrite)));
    }

    private static Type InspectedNode()
    {
        if (!Compatibility.Validate(new[] { "WorldTracking", "Core" }, out var reason)) throw new InvalidOperationException(reason);
        return typeof(PlanetSurfaceSpacesServerUpdater).GetNestedType("ObjectNeighbors", BindingFlags.NonPublic) ?? throw new MissingMemberException("Native clustering node changed.");
    }
    // Only runtime installation queries Harmony ownership. The offline path
    // validates metadata and emits method copies without initializing detours.
    private static void GuardContracts(Type node)
    {
        var list = typeof(List<>).MakeGenericType(node); var components = typeof(List<>).MakeGenericType(list);
        foreach (var method in new[] {
            AccessTools.Method(node, "Equals", new[] { node }), AccessTools.Method(node, "Equals", new[] { typeof(object) }), AccessTools.Method(node, "GetHashCode"),
            AccessTools.Method(typeof(object), "Equals", new[] { typeof(object), typeof(object) }), AccessTools.Method(typeof(object), "Equals", new[] { typeof(object) }), AccessTools.Method(typeof(object), "GetHashCode"),
            AccessTools.Method(typeof(Vector3Double), "op_Addition"), AccessTools.Method(typeof(Vector3Double), "op_Subtraction"), AccessTools.Method(typeof(Vector3Double), "op_UnaryNegation"), AccessTools.PropertyGetter(typeof(Vector3Double), "Magnitude"),
            AccessTools.Method(typeof(Math), nameof(Math.Sqrt), new[] { typeof(double) }),
            AccessTools.Method(typeof(HashSet<>).MakeGenericType(node), "Add"), AccessTools.Method(list, "Add"), AccessTools.Method(list, "GetEnumerator") })
            PatchSafety.RequireNoForeignOwners(method);
        var equality = typeof(EqualityComparer<>).MakeGenericType(node);
        var defaultGetter = AccessTools.PropertyGetter(equality, "Default");
        PatchSafety.RequireNoForeignOwners(defaultGetter);
        var comparer = defaultGetter.Invoke(null, null)!;
        var expected = typeof(EqualityComparer<>).Assembly.GetType("System.Collections.Generic.GenericEqualityComparer`1")?.MakeGenericType(node);
        if (expected == null || comparer.GetType() != expected) throw new InvalidOperationException("Native clustering default equality comparer changed.");
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(expected, "Equals", new[] { node, node }));
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(expected, "GetHashCode", new[] { node }));
        // Native transfer code uses only MainObject/PlanetPos. A foreign observer
        // that retained private nodes or inspected their adjacency would make
        // empty replacement neighbor lists observable, so reject those paths.
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(typeof(PlanetSurfaceSpacesServerUpdater), "GroupObjectsInSameSpace"));
        foreach (var constructor in AccessTools.GetDeclaredConstructors(node)) PatchSafety.RequireNoForeignOwners(constructor);
        var dictionary = typeof(Dictionary<,>).MakeGenericType(typeof(uint), node);
        foreach (var constructor in dictionary.GetConstructors()) PatchSafety.RequireNoForeignOwners(constructor);
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(dictionary, "Add"));
        var valuesGetter = AccessTools.PropertyGetter(dictionary, "Values");
        PatchSafety.RequireNoForeignOwners(valuesGetter);
        var values = valuesGetter.ReturnType;
        foreach (var constructor in values.GetConstructors()) PatchSafety.RequireNoForeignOwners(constructor);
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(values, "CopyTo"));
        var materialize = typeof(Enumerable).GetMethods().Single(method => method.Name == "ToArray" && method.IsGenericMethodDefinition && method.GetParameters().Length == 1).MakeGenericMethod(node);
        PatchSafety.RequireNoForeignOwners(materialize);
        var queue = typeof(Queue<>).MakeGenericType(node); var visited = typeof(HashSet<>).MakeGenericType(node);
        foreach (var collection in new[] { list, components, queue, visited })
            foreach (var constructor in collection.GetConstructors()) PatchSafety.RequireNoForeignOwners(constructor);
        foreach (var method in new[] { AccessTools.Method(queue, "Enqueue"), AccessTools.Method(queue, "TryDequeue"), AccessTools.PropertyGetter(list, "Count"), AccessTools.Method(components, "Add") })
            PatchSafety.RequireNoForeignOwners(method);
        var enumerator = list.GetNestedType("Enumerator")!.MakeGenericType(node);
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(enumerator, "MoveNext"));
        PatchSafety.RequireNoForeignOwners(AccessTools.PropertyGetter(enumerator, "Current"));
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(enumerator, "Dispose"));
    }
    internal static void Initialize()
    {
        var node = InspectedNode();
        var list = typeof(List<>).MakeGenericType(node); var components = typeof(List<>).MakeGenericType(list);
        var position = AccessTools.Field(node, "PlanetPos"); var identity = AccessTools.Field(node, "MainObject"); var neighbors = AccessTools.Field(node, "Neighbors");
        if (position?.FieldType != typeof(Vector3Double) || identity?.FieldType != typeof(ITrackedWorldObjectServer) || neighbors?.FieldType != list)
            throw new InvalidOperationException("Native clustering node fields changed.");
        if (Initialized) return;
        _node = node; _array = node.MakeArrayType(); _components = components;
        var owner = Expression.Parameter(typeof(object)); var index = Expression.Parameter(typeof(int));
        _position = Expression.Lambda<Func<object, Vector3Double>>(Expression.Field(Expression.Convert(owner, node), position), owner).Compile();
        _identity = Expression.Lambda<Func<object, object>>(Expression.Convert(Expression.Field(Expression.Convert(owner, node), identity), typeof(object)), owner).Compile();
        _at = Expression.Lambda<Func<object, int, object>>(Expression.Convert(Expression.ArrayIndex(Expression.Convert(owner, _array), index), typeof(object)), owner, index).Compile();
        _newList = Expression.Lambda<Func<IList>>(Expression.Convert(Expression.New(list), typeof(IList))).Compile();
        _newComponents = Expression.Lambda<Func<IList>>(Expression.Convert(Expression.New(components), typeof(IList))).Compile();
        Initialized = true;
    }

    private static int Local(CodeInstruction code, bool load)
    {
        var name = code.opcode.Name;
        var prefix = load ? "ldloc" : "stloc";
        if (name == prefix || name == prefix + ".s") return code.operand is LocalBuilder builder ? builder.LocalIndex : Convert.ToInt32(code.operand);
        for (var i = 0; i < 4; i++) if (name == prefix + "." + i) return i;
        throw new InvalidOperationException("Native clustering local changed.");
    }

    internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        if (!Initialized) throw new InvalidOperationException("Spatial clustering is not initialized.");
        var codes = instructions.ToList();
        var arrays = codes.Select((code, index) => new { code, index }).Where(x => x.code.operand is MethodInfo method && method.Name == "ToArray" && method.IsGenericMethod && method.GetGenericArguments()[0] == _node).Select(x => x.index).ToArray();
        var outputs = codes.Select((code, index) => new { code, index }).Where(x => x.code.operand is MethodInfo method && method.DeclaringType == _components && method.Name == "GetEnumerator").Select(x => x.index).ToArray();
        if (arrays.Length != 1 || outputs.Length != 1 || outputs[0] <= arrays[0] + 2) throw new InvalidOperationException("Native clustering graph boundaries changed.");
        var arrayStore = arrays[0] + 1; var outputLoad = outputs[0] - 1;
        var arrayIndex = Local(codes[arrayStore], false); var outputIndex = Local(codes[outputLoad], true);
        var temporary = generator.DeclareLocal(typeof(object)); var fallback = generator.DefineLabel(); var output = generator.DefineLabel();
        codes[arrayStore + 1].labels.Add(fallback); codes[outputLoad].labels.Add(output);
        foreach (var code in codes.Take(arrayStore + 1)) yield return code;
        yield return new CodeInstruction(OpCodes.Ldloc, arrayIndex);
        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SpatialClustering), nameof(TryBuildGraph)));
        yield return new CodeInstruction(OpCodes.Stloc, temporary);
        yield return new CodeInstruction(OpCodes.Ldloc, temporary);
        yield return new CodeInstruction(OpCodes.Brfalse, fallback);
        yield return new CodeInstruction(OpCodes.Ldloc, temporary);
        yield return new CodeInstruction(OpCodes.Castclass, _components);
        yield return new CodeInstruction(OpCodes.Stloc, outputIndex);
        yield return new CodeInstruction(OpCodes.Br, output);
        // Keep the complete native graph block and its exception regions for
        // unsupported inputs, then execute the unchanged native transfer loop.
        foreach (var code in codes.Skip(arrayStore + 1)) yield return code;
    }

    internal static object? TryBuildGraph(object source)
    {
        if (DiagnosticCounters.Active) Queries++;
        if (!Initialized || source.GetType() != _array) return Fallback();
        var count = ((Array)source).Length;
        if (count < MinimumNodes || count > NodeLimit || ScratchBytes(count) > ByteLimit) return Fallback();
        var positions = new Vector3Double[count]; var identities = new HashSet<object>(ReferenceComparer.Instance); var types = new Dictionary<Type, bool>();
        for (var i = 0; i < count; i++)
        {
            var node = _at(source, i); var identity = _identity(node);
            if (identity == null) return Fallback();
            var type = identity.GetType();
            if (!types.TryGetValue(type, out var supported))
            {
                if (types.Count >= 32) return Fallback();
                supported = type.GetMethod("Equals", new[] { typeof(object) })?.DeclaringType == typeof(object) && type.GetMethod("GetHashCode", Type.EmptyTypes)?.DeclaringType == typeof(object);
                types.Add(type, supported);
            }
            // Native ObjectNeighbors equality delegates to MainObject. Preserve
            // custom equality, duplicate references and their native hash calls.
            if (!supported || !identities.Add(identity)) return Fallback();
            positions[i] = _position(node);
            if (!Supported(positions[i])) return Fallback();
        }
        var groups = Cluster(positions); var result = _newComponents();
        foreach (var component in groups)
        {
            var list = _newList();
            foreach (var index in component) list.Add(_at(source, index));
            result.Add(list);
        }
        if (DiagnosticCounters.Active) Optimized++;
        return result;
    }

    private static object? Fallback() { if (DiagnosticCounters.Active) Fallbacks++; return null; }
    // At most O(n) nodes, arrays, queues, component buffers and native output
    // lists exist. Conservative accounting includes array/object headers and
    // simultaneous growth buffers. Nothing retains a world/position reference.
    internal static long ScratchBytes(int count) => 131072L + 768L * count;
    private static bool Supported(Vector3Double p) => p.x >= -PositionLimit && p.x <= PositionLimit && p.y >= -PositionLimit && p.y <= PositionLimit && p.z >= -PositionLimit && p.z <= PositionLimit;
    internal static bool TryCluster(Vector3Double[] positions, out List<int[]>? groups)
    {
        groups = null;
        if (positions.Length > NodeLimit || ScratchBytes(positions.Length) > ByteLimit) return false;
        foreach (var point in positions) if (!Supported(point)) return false;
        groups = Cluster(positions); return true;
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceComparer Instance = new ReferenceComparer();
        public new bool Equals(object? a, object? b) => ReferenceEquals(a, b);
        public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
    }
    private sealed class TreeNode
    {
        internal TreeNode? Parent, Left, Right;
        internal int Begin, End, Active;
        internal Vector3Double Min, Max;
    }
    private sealed class AxisComparer : IComparer<int>
    {
        internal Vector3Double[] Points = null!; internal int Axis;
        public int Compare(int a, int b)
        {
            var x = Points[a]; var y = Points[b];
            var c = Axis == 0 ? x.x.CompareTo(y.x) : Axis == 1 ? x.y.CompareTo(y.y) : x.z.CompareTo(y.z);
            return c == 0 ? a.CompareTo(b) : c;
        }
    }
    private static double Min(double a, double b) => a < b ? a : b;
    private static double Max(double a, double b) => a > b ? a : b;
    private static List<int[]> Cluster(Vector3Double[] positions)
    {
        var count = positions.Length; var indices = new int[count]; var owners = new TreeNode[count];
        for (var i = 0; i < count; i++) indices[i] = i;
        var comparer = new AxisComparer { Points = positions };
        TreeNode Build(int begin, int end, TreeNode? parent)
        {
            var node = new TreeNode { Parent = parent, Begin = begin, End = end, Active = end - begin };
            if (begin == end) return node;
            node.Min = node.Max = positions[indices[begin]];
            for (var at = begin + 1; at < end; at++)
            {
                var p = positions[indices[at]];
                node.Min = new Vector3Double(Min(node.Min.x, p.x), Min(node.Min.y, p.y), Min(node.Min.z, p.z));
                node.Max = new Vector3Double(Max(node.Max.x, p.x), Max(node.Max.y, p.y), Max(node.Max.z, p.z));
            }
            if (end - begin <= 8) { for (var at = begin; at < end; at++) owners[indices[at]] = node; return node; }
            var span = node.Max - node.Min; comparer.Axis = span.x >= span.y && span.x >= span.z ? 0 : span.y >= span.z ? 1 : 2;
            Array.Sort(indices, begin, end - begin, comparer); var middle = begin + (end - begin) / 2;
            node.Left = Build(begin, middle, node); node.Right = Build(middle, end, node); return node;
        }
        var tree = Build(0, count, null); var discovered = new bool[count]; var candidates = new List<int>(); var pending = new Queue<int>(); var result = new List<int[]>();
        void Remove(int at)
        {
            discovered[at] = true;
            for (TreeNode? node = owners[at]; node != null; node = node.Parent) node.Active--;
        }
        void Query(TreeNode node, Vector3Double point)
        {
            if (node.Active == 0) return;
            var closest = new Vector3Double(Max(node.Min.x, Min(node.Max.x, point.x)), Max(node.Min.y, Min(node.Max.y, point.y)), Max(node.Min.z, Min(node.Max.z, point.z)));
            // For finite bounded coordinates, each absolute subtraction, square,
            // ordered sum and sqrt is monotone. This native magnitude is a lower
            // bound on EVERY native distance inside the box, including rounding
            // at 1,000. Do not replace it with a differently rounded squared test.
            if ((point - closest).Magnitude >= 1000d) return;
            if (node.Left == null)
            {
                for (var i = node.Begin; i < node.End; i++) if (!discovered[indices[i]]) candidates.Add(indices[i]);
                return;
            }
            Query(node.Left, point); Query(node.Right!, point);
        }
        for (var root = 0; root < count; root++)
        {
            if (discovered[root]) continue;
            var component = new List<int>(); Remove(root); pending.Enqueue(root);
            while (pending.Count > 0)
            {
                var at = pending.Dequeue(); component.Add(at); candidates.Clear(); Query(tree, positions[at]); candidates.Sort();
                foreach (var neighbor in candidates)
                {
                    if ((positions[at] - positions[neighbor]).Magnitude >= 1000d) continue;
                    Remove(neighbor); pending.Enqueue(neighbor);
                }
            }
            result.Add(component.ToArray());
        }
        return result;
    }
}
