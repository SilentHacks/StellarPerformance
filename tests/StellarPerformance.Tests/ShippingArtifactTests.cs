using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class ShippingArtifactTests
{
    [Fact]
    public void ShippingDllHasMatchingManifestVersionNetFrameworkTargetAndNoOptionalModDependency()
    {
        var root = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "RepositoryRoot").Value!;
        var configuration = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var path = Path.Combine(root, "src", "StellarPerformance", "bin", configuration, "net472", "StellarPerformance.dll");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "mod.json")));
        var version = manifest.RootElement.GetProperty("version").GetString()!;
        Assert.Equal(PerformancePlugin.Version, version);
        Assert.Equal(version + ".0", FileVersionInfo.GetVersionInfo(path).FileVersion);
        using var input = File.OpenRead(path);
        using var pe = new PEReader(input);
        var metadata = pe.GetMetadataReader();
        Assert.Equal(new Version(version + ".0"), metadata.GetAssemblyDefinition().Version);
        var references = metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToArray();
        Assert.Contains("mscorlib", references);
        Assert.DoesNotContain("System.Private.CoreLib", references);
        Assert.DoesNotContain("SDModKit", references);
        Assert.DoesNotContain("ItemLines", references);
        Assert.DoesNotContain("UnityEngine.InputLegacyModule", references);
        var resources = metadata.ManifestResources.Select(handle => metadata.GetString(metadata.GetManifestResource(handle).Name)).ToArray();
        Assert.Contains("StellarPerformance.LICENSE", resources);
        Assert.Contains("StellarPerformance.CREDITS", resources);
        var melonAttribute = metadata.CustomAttributes.Select(handle => metadata.GetCustomAttribute(handle)).Single(attribute =>
        {
            if (attribute.Constructor.Kind != HandleKind.MemberReference) return false;
            var constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference) return false;
            return metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent).Name) == "MelonInfoAttribute";
        });
        var blob = metadata.GetBlobReader(melonAttribute.Value);
        Assert.Equal(1, blob.ReadUInt16());
        Assert.Contains("StellarPerformance.PerformancePlugin", blob.ReadSerializedString());
        Assert.Equal(manifest.RootElement.GetProperty("name").GetString(), blob.ReadSerializedString());
        Assert.Equal(version, blob.ReadSerializedString());
        Assert.Equal("SilentHacks", blob.ReadSerializedString());
    }
}
