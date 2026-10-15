using System;
using System.Linq;
using System.Reflection;

namespace StellarPerformance;

internal static class PatchSafety
{
    // A whole-method replacement still runs normal observer prefixes, postfixes and
    // finalizers. A foreign transpiler, skip prefix or argument rewrite changes the
    // body contract and must retain that mod's original path instead.
    internal static bool RewritesArgumentsOrSkips(MethodInfo prefix) =>
        prefix.ReturnType != typeof(void) || prefix.GetParameters().Any(parameter =>
            parameter.ParameterType.IsByRef && parameter.Name != "__state");

    internal static void RequireOriginalBody(MethodBase method, string owner, params string[] allowed)
    {
        if (method == null) throw new MissingMethodException("A required replacement target is missing.");
        var patches = HarmonyLib.Harmony.GetPatchInfo(method);
        if (patches == null) return;
        bool Foreign(string patchOwner) => patchOwner != owner && !allowed.Contains(patchOwner);
        if (patches.Transpilers.Any(patch => Foreign(patch.owner)) ||
            patches.Prefixes.Any(patch => Foreign(patch.owner) && RewritesArgumentsOrSkips(patch.PatchMethod)))
            throw new InvalidOperationException(method.DeclaringType?.Name + "." + method.Name + " has a conflicting body or argument owner.");
    }

    internal static bool HasOnlyOwners(MethodBase method, string[] allowed)
    {
        var owners = HarmonyLib.Harmony.GetPatchInfo(method)?.Owners;
        return owners == null || HasOnlyOwners(owners, allowed);
    }

    internal static void RequireNoForeignOwners(MethodBase method, params string[] allowed)
    {
        if (method == null) throw new MissingMethodException("A bypassed native method is missing.");
        if (!HasOnlyOwners(method, allowed))
            throw new InvalidOperationException(method.DeclaringType?.Name + "." + method.Name + " has an owner whose behavior would be bypassed.");
    }
    internal static bool HasOnlyOwners(System.Collections.Generic.IEnumerable<string> owners, string[] allowed) => owners.All(owner => allowed.Contains(owner));

    internal static void RejectDuplicateCache(MethodBase method, string owner)
    {
        if (HarmonyLib.Harmony.GetPatchInfo(method)?.Owners.Contains(owner) == true)
            throw new InvalidOperationException("Existing " + owner + " cache owns this path; duplicate caching was not installed.");
    }
}
