using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Core.Values;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Saving.Serialization.V12.Ships;

namespace StellarPerformance;

// Keep the native save transaction and replace only its JSON serialization call.
// Every cached hull hit compares all scalar fields and array order. The cache
// owns its record copies and JSON; it never retains a live world/save DTO array.
internal static class SaveSerialization
{
    static SaveSerialization() { }
    internal const string Owner = "stellarperformance.save-serialization";
    private static readonly object Gate = new object();
    private static Type ResolverType = null!;
    private static PropertyInfo[] ResolverOptions = Array.Empty<PropertyInfo>();
    private static object?[] ResolverDefaults = Array.Empty<object?>();
    private static IContractResolver? Resolver;
    private static HullSegments Segments = new HullSegments();

    internal static void Bind()
    {
        ResolverType = AccessTools.TypeByName("Saving.Settings.WritableOnlyContractResolver")
            ?? throw new TypeLoadException("Native writable-only save resolver is missing.");
        ResolverOptions = typeof(DefaultContractResolver).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0).ToArray();
        var defaults = new DefaultContractResolver();
        ResolverDefaults = ResolverOptions.Select(property => property.GetValue(defaults, null)).ToArray();
    }

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var writer = AccessTools.TypeByName("Saving.Services.IO.SaveFileWriter") ?? throw new TypeLoadException("Native save writer is missing.");
        var target = AccessTools.Method(writer, "WriteGameFile") ?? throw new MissingMethodException("Native save-file writer is missing.");
        PatchSafety.RequireOriginalBody(target, Owner);
        Bind();
        PatchSafety.RequireNoForeignOwners(AccessTools.Method(ResolverType, "CreateProperties"), Owner);
        PatchSafety.RequireNoForeignOwners(AccessTools.Constructor(ResolverType), Owner);
        PatchSafety.RequireNoForeignOwners(AccessTools.Constructor(typeof(DefaultContractResolver)), Owner);
        foreach (var option in ResolverOptions)
            PatchSafety.RequireNoForeignOwners(option.GetGetMethod() ?? throw new MissingMemberException("Native resolver option getter " + option.Name), Owner);
        foreach (var name in new[] { "R", "G", "B" })
            PatchSafety.RequireNoForeignOwners(AccessTools.PropertyGetter(typeof(Vector3Byte), name), Owner);
        harmony.Patch(target, transpiler: new HarmonyMethod(typeof(SaveSerialization), nameof(Rewrite)));
    }

    internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
    {
        var original = AccessTools.Method(typeof(JsonConvert), nameof(JsonConvert.SerializeObject), new[] { typeof(object), typeof(Formatting), typeof(JsonSerializerSettings) });
        var replacement = AccessTools.Method(typeof(SaveSerialization), nameof(Serialize));
        var found = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(original)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; found++; }
            yield return instruction;
        }
        if (found != 1) throw new InvalidOperationException("Native save serializer call contract changed.");
    }

    internal static string Serialize(object value, Formatting formatting, JsonSerializerSettings settings)
    {
        if (formatting != Formatting.None || JsonConvert.DefaultSettings != null || settings.TraceWriter != null || settings.EqualityComparer != null ||
            settings.Converters.Count != 0 || !DefaultResolver(settings.ContractResolver))
            return JsonConvert.SerializeObject(value, formatting, settings);
        lock (Gate)
        {
            if (Resolver != null && !DefaultResolver(Resolver)) return JsonConvert.SerializeObject(value, formatting, settings);
            var originalResolver = settings.ContractResolver;
            Resolver ??= originalResolver;
            settings.ContractResolver = Resolver;
            var segments = Segments;
            settings.Converters.Add(segments);
            try
            {
                // This is the native entry point, once. In particular, global
                // JsonConvert.DefaultSettings factories keep their call count.
                return JsonConvert.SerializeObject(value, formatting, settings);
            }
            finally
            {
                settings.Converters.Remove(segments);
                settings.ContractResolver = originalResolver;
            }
        }
    }

    private static bool DefaultResolver(IContractResolver? resolver)
    {
        if (ResolverType == null || resolver == null || resolver.GetType() != ResolverType) return false;
        for (var i = 0; i < ResolverOptions.Length; i++)
        {
            var actual = ResolverOptions[i].GetValue(resolver, null);
            // Native defaults are null or scalar flags. A mutable strategy or
            // any future reference option retains the complete native path.
            if (actual != null && !actual.GetType().IsValueType && !(actual is string)) return false;
            if (!Equals(actual, ResolverDefaults[i])) return false;
        }
        return true;
    }

    internal static void Clear()
    {
        lock (Gate) { Resolver = null; Segments = new HullSegments(); }
    }

    internal sealed class HullSegments : JsonConverter
    {
        internal const int EntryLimit = 256, PartLimit = 32768, ByteLimit = 16 * 1024 * 1024;
        private sealed class Entry
        {
            internal SerializableHullPart[] Parts = null!;
            internal string Json = "";
        }
        private readonly Dictionary<int, List<Entry>> Cache = new Dictionary<int, List<Entry>>();
        private int Bytes;
        internal int Entries { get; private set; }
        internal int RetainedBytes => Bytes;
        private static readonly Type[] NativeTypes = { typeof(SerializableHullPart[]), typeof(SerializableHullPart), typeof(Vector3Byte), typeof(ushort), typeof(byte), typeof(int) };

        public override bool CanRead => false;
        public override bool CanConvert(Type type) => type == typeof(SerializableHullPart[]);
        public override object ReadJson(JsonReader reader, Type type, object? existingValue, JsonSerializer serializer) => throw new NotSupportedException();

        private bool Compatible(JsonSerializer serializer)
        {
            if (serializer.TypeNameHandling != TypeNameHandling.None || serializer.PreserveReferencesHandling != PreserveReferencesHandling.None ||
                serializer.DefaultValueHandling != DefaultValueHandling.Include || serializer.ContractResolver.GetType() != ResolverType ||
                serializer.TraceWriter != null || serializer.EqualityComparer != null ||
                serializer.Converters.Count != 1 || !ReferenceEquals(serializer.Converters[0], this)) return false;
            foreach (var type in NativeTypes)
            {
                var contract = serializer.ContractResolver.ResolveContract(type);
                if (contract.Converter != null || contract.IsReference == true) return false;
            }
            return true;
        }

        private void Native(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var index = serializer.Converters.IndexOf(this);
            if (index < 0) throw new InvalidOperationException("Save converter is not owned by this serializer.");
            serializer.Converters.RemoveAt(index);
            try { serializer.Serialize(writer, value); }
            finally { serializer.Converters.Insert(index, this); }
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value == null) { writer.WriteNull(); return; }
            if (!Compatible(serializer)) { Native(writer, value, serializer); return; }
            var parts = (SerializableHullPart[])value;
            if (parts.Length == 0 || parts.Length > PartLimit) { WriteParts(writer, parts); return; }
            var hash = Hash(parts);
            if (Cache.TryGetValue(hash, out var candidates))
                foreach (var candidate in candidates)
                    if (Same(parts, candidate.Parts)) { writer.WriteRawValue(candidate.Json); return; }

            string json;
            using (var text = new StringWriter(CultureInfo.InvariantCulture))
            using (var jsonWriter = new JsonTextWriter(text))
            { WriteParts(jsonWriter, parts); jsonWriter.Flush(); json = text.ToString(); }
            // The conservative estimate includes copied scalar records, UTF-16
            // text, collection nodes and headers; both bytes and entries are bounded.
            var bytes = 256L + 64L * parts.Length + 2L * json.Length;
            if (bytes <= ByteLimit)
            {
                if (Bytes + bytes > ByteLimit || Entries >= EntryLimit) { Cache.Clear(); Bytes = Entries = 0; candidates = null; }
                if (candidates == null) Cache[hash] = candidates = new List<Entry>();
                candidates.Add(new Entry { Parts = (SerializableHullPart[])parts.Clone(), Json = json });
                Bytes += (int)bytes; Entries++;
            }
            writer.WriteRawValue(json);
        }

        private static void Number(JsonWriter writer, string name, int value) { writer.WritePropertyName(name); writer.WriteValue(value); }
        private static void Color(JsonWriter writer, string name, Vector3Byte color)
        {
            writer.WritePropertyName(name); writer.WriteStartObject();
            Number(writer, "x", color.x); Number(writer, "y", color.y); Number(writer, "z", color.z);
            Number(writer, "R", color.R); Number(writer, "G", color.G); Number(writer, "B", color.B);
            writer.WriteEndObject();
        }
        private static void WriteParts(JsonWriter writer, SerializableHullPart[] parts)
        {
            writer.WriteStartArray();
            foreach (var part in parts)
            {
                writer.WriteStartObject();
                Number(writer, "partId", part.partId); Number(writer, "x", part.x); Number(writer, "y", part.y); Number(writer, "z", part.z);
                Number(writer, "forwardOrientationId", part.forwardOrientationId); Number(writer, "upOrientationId", part.upOrientationId); Number(writer, "voxelPartTypeId", part.voxelPartTypeId);
                Color(writer, "FrontColor", part.FrontColor); Color(writer, "BackColor", part.BackColor);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        internal static int Hash(SerializableHullPart[] parts)
        {
            unchecked
            {
                var hash = parts.Length;
                foreach (var part in parts)
                {
                    hash = hash * 31 + part.partId; hash = hash * 31 + part.x; hash = hash * 31 + part.y; hash = hash * 31 + part.z;
                    hash = hash * 31 + part.forwardOrientationId; hash = hash * 31 + part.upOrientationId; hash = hash * 31 + part.voxelPartTypeId;
                    hash = hash * 31 + part.FrontColor.x; hash = hash * 31 + part.FrontColor.y; hash = hash * 31 + part.FrontColor.z;
                    hash = hash * 31 + part.BackColor.x; hash = hash * 31 + part.BackColor.y; hash = hash * 31 + part.BackColor.z;
                }
                return hash;
            }
        }
        private static bool Same(SerializableHullPart[] left, SerializableHullPart[] right)
        {
            if (left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++)
            {
                var a = left[i]; var b = right[i];
                if (a.partId != b.partId || a.x != b.x || a.y != b.y || a.z != b.z || a.forwardOrientationId != b.forwardOrientationId || a.upOrientationId != b.upOrientationId || a.voxelPartTypeId != b.voxelPartTypeId ||
                    a.FrontColor.x != b.FrontColor.x || a.FrontColor.y != b.FrontColor.y || a.FrontColor.z != b.FrontColor.z || a.BackColor.x != b.BackColor.x || a.BackColor.y != b.BackColor.y || a.BackColor.z != b.BackColor.z) return false;
            }
            return true;
        }
    }
}
