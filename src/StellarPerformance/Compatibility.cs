using System;
using System.Collections.Generic;
using System.Reflection;

namespace StellarPerformance;

internal static class Compatibility
{
    internal static readonly IReadOnlyDictionary<string, string> ExpectedModules = new Dictionary<string, string>
    {
        ["Assembly-CSharp"] = "0379aaca-5dff-430e-b4db-aa2538738891",
        ["Core"] = "3bb6ad46-50a6-4469-9cce-fce9952fdd7d",
        ["Players"] = "e20d6376-9433-4ae4-b34a-857934659cb5",
        ["Ships"] = "f1589ce1-5692-4187-a0d8-d2d85bb4aa43",
        ["Ships.Interface"] = "ebf1fc59-7637-4f52-9fec-0ff2589c6bc0",
        ["Planets"] = "98365094-6cc6-4889-87a1-e0b749f4a868",
        ["Rendering"] = "48d4d761-edab-4409-95aa-90d44eac53da",
        ["MKEdgeDetection"] = "4a69ad5e-b179-440c-b8c2-690bcda0a87a",
        ["MKEdgeDetectionUniversalRendererFeature"] = "390e664c-df37-4efd-b3f7-58ac1e0c3b73",
        ["Items.Model"] = "4c3b9d4a-7b05-4bae-82fd-14d7ce62c913",
        ["SDModKit"] = "5f5977f6-e8b4-4aab-bc6e-c0179e0d3f9d",
        ["UnityEngine.CoreModule"] = "9eb5f512-edf8-4b29-b73c-bc699a604447",
        ["UnityEngine.UI"] = "44187a17-305e-4f69-bb70-baffd98c8045",
        ["UnityEngine.UIModule"] = "a900a696-0f2c-4e54-b0ba-82d209f24d5e",
        ["UI"] = "1e4d82f5-832b-4320-bca2-cd5da4fa7ddd",
        ["Unity.TextMeshPro"] = "cf485f2b-d734-4dfa-9ff4-2627abe76f17",
        ["Unity.RenderPipelines.Universal.Runtime"] = "88e54920-c9a5-42d4-9bd3-28fb4a3504ff",
        ["Unity.RenderPipelines.Core.Runtime"] = "ae2b49eb-00fd-41f6-a372-12dcc2d259f6",
        ["UnityEngine.ParticleSystemModule"] = "f55349eb-22fe-4d55-aca1-f68d3c946dff",
        ["UnityEngine.IMGUIModule"] = "248a3a48-4edb-42ed-95ba-436c12887b52",
        ["Unity.InputSystem"] = "35fba23b-51c9-4ebc-8010-04e6172278b9",
        ["UI.Interface"] = "0007052d-91d7-488f-9739-a7834a0905ff",
        ["FishNet.Runtime"] = "f6b6c552-6a3f-4fec-b4ff-f9edae0f0238",
        ["com.rlabrecque.steamworks.net"] = "6063c1a2-b253-4861-a41a-d1fee5e6303f",
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
