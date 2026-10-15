using System;
using System.Linq.Expressions;
using HarmonyLib;

namespace StellarPerformance;

// Bind once during guarded module initialization; never reflect in per-part loops.
internal static class NativeAccess
{
    internal static Func<TOwner, TField> Read<TOwner, TField>(string name)
    {
        var owner = Expression.Parameter(typeof(TOwner), "owner");
        var field = AccessTools.Field(typeof(TOwner), name) ?? throw new MissingFieldException(typeof(TOwner).FullName, name);
        if (field.FieldType != typeof(TField)) throw new InvalidOperationException(typeof(TOwner).Name + "." + name + " type changed.");
        return Expression.Lambda<Func<TOwner, TField>>(Expression.Field(owner, field), owner).Compile();
    }

    internal static Action<TOwner, TField> Write<TOwner, TField>(string name)
    {
        var owner = Expression.Parameter(typeof(TOwner), "owner");
        var value = Expression.Parameter(typeof(TField), "value");
        var field = AccessTools.Field(typeof(TOwner), name) ?? throw new MissingFieldException(typeof(TOwner).FullName, name);
        if (field.FieldType != typeof(TField)) throw new InvalidOperationException(typeof(TOwner).Name + "." + name + " type changed.");
        return Expression.Lambda<Action<TOwner, TField>>(Expression.Assign(Expression.Field(owner, field), value), owner, value).Compile();
    }

    internal static Action<TOwner> Call<TOwner>(string name)
    {
        var owner = Expression.Parameter(typeof(TOwner), "owner");
        var method = AccessTools.Method(typeof(TOwner), name, Type.EmptyTypes) ?? throw new MissingMethodException(typeof(TOwner).FullName, name);
        return Expression.Lambda<Action<TOwner>>(Expression.Call(owner, method), owner).Compile();
    }
}
