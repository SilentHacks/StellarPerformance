using System;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using Players.Interface.Services;
using Players.Services;
using Tools.Build;
using Tools.Interface.Model;
using Tools.Interface.Services;
using Tools.Recycler;

namespace StellarPerformance;

internal static class ToolRefsCache
{
    static ToolRefsCache() { }

    private static readonly Func<LocalSelectedToolTracker, IInteractableTool?> CurrentToolOf = BindCurrentTool();
    private static LocalSelectedToolTracker? _tracker;
    private static Action<BuildToolLocalClient?>? _setBuild;
    private static Action<RecyclerTool?>? _setRecycler;

    private static Func<LocalSelectedToolTracker, IInteractableTool?> BindCurrentTool()
    {
        var tracker = Expression.Parameter(typeof(LocalSelectedToolTracker), "tracker");
        var field = AccessTools.Field(typeof(LocalSelectedToolTracker), "_currentTool") ?? throw new MissingFieldException("LocalSelectedToolTracker._currentTool");
        if (field.FieldType != typeof(IInteractableTool)) throw new InvalidOperationException("LocalSelectedToolTracker._currentTool type changed.");
        return Expression.Lambda<Func<LocalSelectedToolTracker, IInteractableTool?>>(Expression.Field(tracker, field), tracker).Compile();
    }

    private static Action<T?> BindStaticSetter<T>(Type ownerType, string fieldName) where T : class
    {
        var field = AccessTools.Field(ownerType, fieldName) ?? throw new MissingFieldException(ownerType.FullName, fieldName);
        if (field.FieldType != typeof(T)) throw new InvalidOperationException(ownerType.Name + "." + fieldName + " type changed.");
        var value = Expression.Parameter(typeof(T), "value");
        return Expression.Lambda<Action<T?>>(Expression.Assign(Expression.Field(null, field), value), value).Compile();
    }

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var sdModKit = Assembly.Load("SDModKit");
        var toolRefsType = sdModKit.GetType("SDModKit.Game.ToolRefs", true) ?? throw new TypeLoadException("SDModKit.Game.ToolRefs missing.");
        var gameRefsType = sdModKit.GetType("SDModKit.Game.GameRefs", true) ?? throw new TypeLoadException("SDModKit.Game.GameRefs missing.");
        _setBuild = BindStaticSetter<BuildToolLocalClient>(toolRefsType, "_build");
        _setRecycler = BindStaticSetter<RecyclerTool>(toolRefsType, "_recycler");
        _tracker = null;

        var setup = AccessTools.Method(typeof(LocalSelectedToolTracker), nameof(LocalSelectedToolTracker.Setup)) ?? throw new MissingMethodException("LocalSelectedToolTracker.Setup");
        var unsheathe = AccessTools.Method(typeof(LocalSelectedToolTracker), "UnsheatheTool") ?? throw new MissingMethodException("LocalSelectedToolTracker.UnsheatheTool");
        var remove = AccessTools.Method(typeof(LocalSelectedToolTracker), "RemoveCurrentTool", Type.EmptyTypes) ?? throw new MissingMethodException("LocalSelectedToolTracker.RemoveCurrentTool");
        var invalidate = AccessTools.Method(gameRefsType, "Invalidate", Type.EmptyTypes) ?? throw new MissingMethodException("SDModKit.Game.GameRefs.Invalidate");
        var getBuild = AccessTools.PropertyGetter(toolRefsType, "Build") ?? throw new MissingMethodException("SDModKit.Game.ToolRefs.get_Build");
        var getRecycler = AccessTools.PropertyGetter(toolRefsType, "Recycler") ?? throw new MissingMethodException("SDModKit.Game.ToolRefs.get_Recycler");

        PatchSafety.RequireOriginalBody(getBuild, harmony.Id);
        PatchSafety.RequireOriginalBody(getRecycler, harmony.Id);
        foreach (var lifecycle in new[] { setup, unsheathe, remove })
            harmony.Patch(lifecycle, postfix: new HarmonyMethod(typeof(ToolRefsCache), nameof(AfterToolStateChanged)));
        harmony.Patch(invalidate, postfix: new HarmonyMethod(typeof(ToolRefsCache), nameof(OnWorldInvalidate)));
        harmony.Patch(getBuild, prefix: new HarmonyMethod(typeof(ToolRefsCache), nameof(BeforeGetBuild)));
        harmony.Patch(getRecycler, prefix: new HarmonyMethod(typeof(ToolRefsCache), nameof(BeforeGetRecycler)));
    }

    internal static void AfterToolStateChanged(LocalSelectedToolTracker __instance)
    {
        _tracker = __instance;
        var tool = !ReferenceEquals(__instance, null) ? CurrentToolOf(__instance) : null;
        _setBuild?.Invoke(tool as BuildToolLocalClient);
        _setRecycler?.Invoke(tool as RecyclerTool);
    }

    internal static void OnWorldInvalidate()
    {
        _tracker = null;
        _setBuild?.Invoke(null);
        _setRecycler?.Invoke(null);
    }

    internal static bool BeforeGetBuild(ref BuildToolLocalClient? __result)
    {
        var tool = !ReferenceEquals(_tracker, null) ? CurrentToolOf(_tracker) : null;
        var build = tool as BuildToolLocalClient;
        _setBuild?.Invoke(build);
        __result = build;
        return false;
    }

    internal static bool BeforeGetRecycler(ref RecyclerTool? __result)
    {
        var tool = !ReferenceEquals(_tracker, null) ? CurrentToolOf(_tracker) : null;
        var recycler = tool as RecyclerTool;
        _setRecycler?.Invoke(recycler);
        __result = recycler;
        return false;
    }
}
