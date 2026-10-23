using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Core.Values;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Saving.Serialization.V12.Ships;
using Xunit;

namespace StellarPerformance.Tests;

[CollectionDefinition("Save serialization globals", DisableParallelization = true)]
public sealed class SaveSerializationCollection { }

[Collection("Save serialization globals")]
public sealed class SaveSerializationTests
{
    private static JsonSerializerSettings Settings()
    {
        var type = Assembly.Load("Saving").GetType("Saving.Settings.WritableOnlyContractResolver", true)!;
        return new JsonSerializerSettings { ContractResolver = (IContractResolver)Activator.CreateInstance(type)! };
    }
    private static void Bind() { _ = Settings(); SaveSerialization.Bind(); SaveSerialization.Clear(); }
    private static SerializableHullPart Part(int value = 0) => new SerializableHullPart
    {
        partId = 65535, x = int.MinValue + value, y = int.MaxValue - value, z = -value,
        forwardOrientationId = 255, upOrientationId = 1, voxelPartTypeId = 0,
        FrontColor = new Vector3Byte(0, 127, 255), BackColor = new Vector3Byte(255, 1, 128)
    };
    private static string Native(object value, JsonSerializerSettings? settings = null) => JsonConvert.SerializeObject(value, Formatting.None, settings ?? Settings());
    private static string Fast(object value, JsonSerializerSettings? settings = null) => SaveSerialization.Serialize(value, Formatting.None, settings ?? Settings());

    [Fact]
    public void HullFieldsOrderingAliasesAndUnrelatedDataMatchNativeJsonExactly()
    {
        Bind();
        var random = new Random(231026);
        var parts = Enumerable.Range(0, 250).Select(i => Part(random.Next(100000))).ToArray();
        var value = new Envelope { hullParts = parts, note = "quotes\"\\\n<>&\u2028", gas = -0.0, other = new[] { 1.0 / 3, double.NaN, double.PositiveInfinity } };
        var expected = Native(value);
        Assert.Equal(expected, Fast(value));
        Assert.Equal(expected, Fast(value));
        Assert.Contains("\"FrontColor\":{\"x\":0,\"y\":127,\"z\":255,\"R\":0,\"G\":127,\"B\":255}", expected);
        Assert.Equal(Native(new Envelope { hullParts = null }), Fast(new Envelope { hullParts = null }));
        Assert.Equal(Native(new Envelope { hullParts = Array.Empty<SerializableHullPart>() }), Fast(new Envelope { hullParts = Array.Empty<SerializableHullPart>() }));
    }

    [Fact]
    public void EveryHullFieldMutationOrderChangeAndHashCollisionInvalidatesReuse()
    {
        Bind();
        var parts = new[] { Part(10), Part(20) };
        var original = (SerializableHullPart[])parts.Clone();
        Assert.Equal(Native(parts), Fast(parts));
        var mutations = new Action[]
        {
            () => parts[0].partId--, () => parts[0].x++, () => parts[0].y--, () => parts[0].z++,
            () => parts[0].forwardOrientationId--, () => parts[0].upOrientationId++, () => parts[0].voxelPartTypeId++,
            () => parts[0].FrontColor.x++, () => parts[0].FrontColor.y++, () => parts[0].FrontColor.z--,
            () => parts[0].BackColor.x--, () => parts[0].BackColor.y++, () => parts[0].BackColor.z++
        };
        foreach (var mutate in mutations)
        {
            Array.Copy(original, parts, parts.Length); mutate();
            Assert.Equal(Native(parts), Fast(parts));
        }
        Array.Copy(original, parts, parts.Length);
        var hash = SaveSerialization.HullSegments.Hash(parts);
        parts[0].x++; parts[0].y -= 31;
        Assert.Equal(hash, SaveSerialization.HullSegments.Hash(parts));
        Assert.NotEqual(Native(original), Native(parts));
        Assert.Equal(Native(parts), Fast(parts));
        Array.Reverse(parts); Assert.Equal(Native(parts), Fast(parts));
        Assert.Equal(Native(parts.Take(1).ToArray()), Fast(parts.Take(1).ToArray()));
        Assert.Equal(Native(original), Fast(original));
    }

    [Theory]
    [InlineData("types")]
    [InlineData("references")]
    [InlineData("defaults")]
    [InlineData("integers")]
    public void EffectiveGlobalJsonPoliciesFallbackWithoutAnExtraDefaultsFactoryCall(string policy)
    {
        Bind();
        var previous = JsonConvert.DefaultSettings;
        var calls = 0;
        JsonSerializerSettings Factory()
        {
            calls++;
            var settings = new JsonSerializerSettings();
            if (policy == "types") settings.TypeNameHandling = TypeNameHandling.All;
            if (policy == "references") settings.PreserveReferencesHandling = PreserveReferencesHandling.All;
            if (policy == "defaults") settings.DefaultValueHandling = DefaultValueHandling.Ignore;
            if (policy == "integers") settings.Converters.Add(new IntegerConverter());
            return settings;
        }
        try
        {
            JsonConvert.DefaultSettings = Factory;
            var value = new[] { Part(42), Part(0) };
            var native = Native(value); Assert.Equal(1, calls);
            var settings = Settings(); var resolver = settings.ContractResolver;
            Assert.Equal(native, Fast(value, settings)); Assert.Equal(2, calls);
            Assert.Same(resolver, settings.ContractResolver); Assert.Empty(settings.Converters);
        }
        finally { JsonConvert.DefaultSettings = previous; SaveSerialization.Clear(); }
    }

    [Fact]
    public void SameTypeResolverOptionsAndPreviouslyCachedResolverMutationsRetainNativeOutput()
    {
        Bind();
        var value = new[] { Part(7), Part(9) };
        foreach (var property in typeof(DefaultContractResolver).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0))
        {
            SaveSerialization.Clear();
            var settings = Settings(); var resolver = settings.ContractResolver!;
            object changed;
            if (property.PropertyType == typeof(bool)) changed = !(bool)property.GetValue(resolver)!;
            else if (property.PropertyType == typeof(BindingFlags)) changed = (BindingFlags)property.GetValue(resolver)! | BindingFlags.NonPublic;
            else if (property.PropertyType == typeof(NamingStrategy)) changed = new CamelCaseNamingStrategy();
            else throw new InvalidOperationException("Untested mutable native resolver option " + property.Name);
            property.SetValue(resolver, changed);
            Assert.Equal(Native(value, settings), Fast(value, settings));
        }
        SaveSerialization.Clear();
        var first = Settings(); Assert.Equal(Native(value), Fast(value, first));
        ((DefaultContractResolver)first.ContractResolver!).NamingStrategy = new CamelCaseNamingStrategy();
        Assert.Equal(Native(value), Fast(value));
    }

    [Fact]
    public void TraceAndEqualityObserversRetainNativeCallsAndMessages()
    {
        Bind();
        var value = new[] { Part(7), Part(9) };
        var expectedTrace = new TraceRecorder(); var nativeSettings = Settings(); nativeSettings.TraceWriter = expectedTrace;
        var expected = Native(value, nativeSettings);
        var trace = new TraceRecorder(); var settings = Settings(); settings.TraceWriter = trace;
        Assert.Equal(expected, Fast(value, settings)); Assert.Equal(expectedTrace.Messages, trace.Messages);
        var nativeComparer = new EqualityCounter(); nativeSettings = Settings(); nativeSettings.EqualityComparer = nativeComparer;
        var envelope = new Envelope { hullParts = value };
        expected = Native(envelope, nativeSettings);
        var comparer = new EqualityCounter(); settings = Settings(); settings.EqualityComparer = comparer;
        Assert.Equal(expected, Fast(envelope, settings)); Assert.Equal(nativeComparer.Calls, comparer.Calls);
    }

    [Fact]
    public void UnrelatedExternalConvertersRetainTheirNativePredicateCallCount()
    {
        Bind();
        var converter = new PredicateCounter(); var previous = JsonConvert.DefaultSettings;
        var value = new[] { Part(7), Part(9) };
        try
        {
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings { Converters = { converter } };
            var expected = Native(value); var nativeCalls = converter.Calls; converter.Calls = 0;
            Assert.Equal(expected, Fast(value)); Assert.Equal(nativeCalls, converter.Calls);
            JsonConvert.DefaultSettings = null;
            var settings = Settings(); settings.Converters.Add(converter); converter.Calls = 0;
            expected = Native(value, settings); nativeCalls = converter.Calls; converter.Calls = 0;
            Assert.Equal(expected, Fast(value, settings)); Assert.Equal(nativeCalls, converter.Calls);
        }
        finally { JsonConvert.DefaultSettings = previous; SaveSerialization.Clear(); }
    }

    [Fact]
    public void ExceptionsAndAlternateResolversKeepNativeBehaviorAndRestoreSettings()
    {
        Bind();
        var settings = Settings(); var resolver = settings.ContractResolver;
        var converter = new ThrowingConverter(); settings.Converters.Add(converter);
        Assert.Throws<InvalidOperationException>(() => Fast(new[] { Part() }, settings));
        Assert.Same(resolver, settings.ContractResolver); Assert.Equal(new[] { converter }, settings.Converters);
        var alternate = new JsonSerializerSettings { ContractResolver = new DefaultContractResolver() };
        Assert.Equal(Native(new[] { 1, 2, 3 }, alternate), Fast(new[] { 1, 2, 3 }, alternate));
        Assert.Equal(JsonConvert.SerializeObject(new[] { Part() }, Formatting.Indented, Settings()), SaveSerialization.Serialize(new[] { Part() }, Formatting.Indented, Settings()));
    }

    [Fact]
    public void MemoizedStorageIsBoundedAndOversizedArraysRetainExactOutput()
    {
        Bind();
        var converter = new SaveSerialization.HullSegments(); var settings = Settings(); settings.Converters.Add(converter);
        for (var i = 0; i < SaveSerialization.HullSegments.EntryLimit + 50; i++)
        {
            var parts = new[] { Part(i) };
            Assert.Equal(Native(parts), Native(parts, settings));
            Assert.InRange(converter.Entries, 1, SaveSerialization.HullSegments.EntryLimit);
            Assert.InRange(converter.RetainedBytes, 1, SaveSerialization.HullSegments.ByteLimit);
        }
        var before = converter.RetainedBytes;
        var large = Enumerable.Repeat(Part(), SaveSerialization.HullSegments.PartLimit + 1).ToArray();
        Assert.Equal(Native(large), Native(large, settings)); Assert.Equal(before, converter.RetainedBytes);
    }

    [Fact]
    public void TranspilerReplacesExactlyTheOneSerializerCallAndKeepsNativeTransactionInstructions()
    {
        _ = Settings();
        var method = Assembly.Load("Saving").GetType("Saving.Services.IO.SaveFileWriter", true)!.GetMethod("WriteGameFile")!;
        var original = ManagedMethod.Instructions(method);
        var rewritten = SaveSerialization.Rewrite(original.Select(code => new CodeInstruction(code))).ToList();
        var replacement = AccessTools.Method(typeof(SaveSerialization), nameof(SaveSerialization.Serialize));
        Assert.Equal(original.Count, rewritten.Count);
        Assert.Single(rewritten, code => code.Calls(replacement));
        var changes = original.Zip(rewritten, (a, b) => a.opcode != b.opcode || !Equals(a.operand, b.operand)).Count(changed => changed);
        Assert.Equal(1, changes);
        Assert.Throws<InvalidOperationException>(() => SaveSerialization.Rewrite(Array.Empty<CodeInstruction>()).ToList());
        Assert.Throws<InvalidOperationException>(() => SaveSerialization.Rewrite(original.Concat(original).Select(code => new CodeInstruction(code))).ToList());
    }

    public sealed class Envelope
    {
        public SerializableHullPart[]? hullParts;
        public string note = "";
        public double gas;
        public double[] other = Array.Empty<double>();
    }
    private sealed class IntegerConverter : JsonConverter
    {
        public override bool CanConvert(Type type) => type == typeof(int);
        public override object ReadJson(JsonReader reader, Type type, object? existingValue, JsonSerializer serializer) => throw new NotSupportedException();
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) => writer.WriteValue("integer:" + value);
    }
    private sealed class PredicateCounter : JsonConverter
    {
        internal int Calls;
        public override bool CanConvert(Type type) { Calls++; return false; }
        public override object ReadJson(JsonReader reader, Type type, object? existingValue, JsonSerializer serializer) => throw new NotSupportedException();
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) => throw new InvalidOperationException("Unrelated converter should never write");
    }
    private sealed class TraceRecorder : ITraceWriter
    {
        internal readonly List<string> Messages = new List<string>();
        public TraceLevel LevelFilter => TraceLevel.Verbose;
        public void Trace(TraceLevel level, string message, Exception? exception) => Messages.Add(level + ":" + message);
    }
    private sealed class EqualityCounter : IEqualityComparer
    {
        internal int Calls;
        public new bool Equals(object? x, object? y) { Calls++; return ReferenceEquals(x, y); }
        public int GetHashCode(object obj) { Calls++; return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj); }
    }
    private sealed class ThrowingConverter : JsonConverter
    {
        public override bool CanConvert(Type type) => type == typeof(SerializableHullPart[]);
        public override object ReadJson(JsonReader reader, Type type, object? existingValue, JsonSerializer serializer) => throw new NotSupportedException();
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) => throw new InvalidOperationException("Deliberate native converter failure");
    }
}
