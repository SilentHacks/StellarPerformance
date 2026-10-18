using System;
using System.Collections.Generic;
using System.Reflection;

namespace StellarPerformance;

internal static class Compatibility
{
    internal static readonly IReadOnlyDictionary<string, string> ExpectedModules = new Dictionary<string, string>
    {
        ["Core"] = "3bb6ad46-50a6-4469-9cce-fce9952fdd7d",
        ["Players"] = "e20d6376-9433-4ae4-b34a-857934659cb5",
        ["Ships"] = "f1589ce1-5692-4187-a0d8-d2d85bb4aa43",
        ["Ships.Interface"] = "ebf1fc59-7637-4f52-9fec-0ff2589c6bc0",
        ["SDModKit"] = "5f5977f6-e8b4-4aab-bc6e-c0179e0d3f9d",
        ["UnityEngine.CoreModule"] = "9eb5f512-edf8-4b29-b73c-bc699a604447",
        ["UI"] = "1e4d82f5-832b-4320-bca2-cd5da4fa7ddd",
        ["Unity.TextMeshPro"] = "cf485f2b-d734-4dfa-9ff4-2627abe76f17",
        ["FishNet.Runtime"] = "f6b6c552-6a3f-4fec-b4ff-f9edae0f0238",
    };

    internal static bool Validate(IEnumerable<string> modules, out string reason)
    {
        foreach (var name in modules)
        {
            Guid identity;
            try { identity = Assembly.Load(name).ManifestModule.ModuleVersionId; }
            catch (System.IO.FileNotFoundException) { reason = name + " is not installed."; return false; }
            catch (System.IO.FileLoadException) { reason = name + " could not be loaded."; return false; }
            catch (BadImageFormatException) { reason = name + " is not a supported managed assembly."; return false; }
            if (!Matches(name, identity))
            {
                reason = name + " differs from the inspected build.";
                return false;
            }
        }
        reason = string.Empty;
        return true;
    }

    internal static bool Matches(string name, Guid mvid) => ExpectedModules.TryGetValue(name, out var expected) && mvid == new Guid(expected);
}
