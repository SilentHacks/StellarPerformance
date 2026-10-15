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
        ["Planets"] = "98365094-6cc6-4889-87a1-e0b749f4a868",
        ["Planets.Interface"] = "b35f2a77-b160-46a5-84e8-a839c7391e21",
        ["Players"] = "e20d6376-9433-4ae4-b34a-857934659cb5",
        ["Players.Interface"] = "36289a7f-1712-4d9e-8f37-2ff3f458adcf",
        ["Ships"] = "f1589ce1-5692-4187-a0d8-d2d85bb4aa43",
        ["Ships.Interface"] = "ebf1fc59-7637-4f52-9fec-0ff2589c6bc0",
        ["System.Core"] = "e1f614c5-eede-4689-9885-94bcb9796955",
        ["Tools"] = "03619ecd-93e2-4257-b19c-34b344ba2e86",
        ["Tools.Interface"] = "cfc23780-fd66-45dd-91b2-ee7594413a63",
        ["WorldObjects"] = "f5e6c922-9ba8-4a0d-8783-3e80525ae1ae",
        ["WorldObjects.Interface"] = "840abf48-feee-4619-89ce-76aae47a274d",
        ["WorldTracking"] = "9e7dd5cb-71fb-4ad7-b191-2136eb4e6e60",
        ["WorldTracking.Interface"] = "95f8a341-460f-41f0-ab5f-4a73c54bb3d6",
        ["CablesMesh"] = "ad31eeee-6f2c-4bfb-9a48-9cfd62f71e0a",
        ["SDModKit"] = "5f5977f6-e8b4-4aab-bc6e-c0179e0d3f9d",
        ["Saving"] = "2a99be51-4517-4b4f-82c8-f553c7c99567",
        ["UnityEngine.CoreModule"] = "9eb5f512-edf8-4b29-b73c-bc699a604447",
        ["UI"] = "1e4d82f5-832b-4320-bca2-cd5da4fa7ddd",
        ["UnityEngine.UIModule"] = "a900a696-0f2c-4e54-b0ba-82d209f24d5e",
        ["UnityEngine.UI"] = "44187a17-305e-4f69-bb70-baffd98c8045",
        ["Unity.TextMeshPro"] = "cf485f2b-d734-4dfa-9ff4-2627abe76f17",
        ["UnityEngine.PhysicsModule"] = "7916e147-eda7-4a34-9e34-e65df3a74249",
        ["ItemLines"] = "7f0a4c63-9664-4a97-887b-585f8692a3d9",
        ["Unity.InputSystem"] = "35fba23b-51c9-4ebc-8010-04e6172278b9",
        ["FishNet.Runtime"] = "f6b6c552-6a3f-4fec-b4ff-f9edae0f0238",
        ["com.rlabrecque.steamworks.net"] = "6063c1a2-b253-4861-a41a-d1fee5e6303f"
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
