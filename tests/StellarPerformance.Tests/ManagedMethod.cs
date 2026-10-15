using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace StellarPerformance.Tests;

internal static class ManagedMethod
{
    internal static List<CodeInstruction> Instructions(MethodInfo method, ILGenerator? generator = null) => PatchProcessor.GetOriginalInstructions(method, generator ?? Create(method).GetILGenerator());

    internal static Delegate Clone(MethodInfo method, Func<IEnumerable<CodeInstruction>, ILGenerator, IEnumerable<CodeInstruction>> transform)
    {
        var replacement = Create(method);
        var generator = replacement.GetILGenerator();
        var instructions = transform(Instructions(method, generator), generator);
        foreach (var code in instructions)
        {
            foreach (var block in code.blocks.Where(block => block.blockType != ExceptionBlockType.EndExceptionBlock))
            {
                switch (block.blockType)
                {
                    case ExceptionBlockType.BeginExceptionBlock: generator.BeginExceptionBlock(); break;
                    case ExceptionBlockType.BeginCatchBlock: generator.BeginCatchBlock(block.catchType); break;
                    case ExceptionBlockType.BeginFinallyBlock: generator.BeginFinallyBlock(); break;
                    case ExceptionBlockType.BeginFaultBlock: generator.BeginFaultBlock(); break;
                    case ExceptionBlockType.BeginExceptFilterBlock: generator.BeginExceptFilterBlock(); break;
                    default: throw new NotSupportedException(block.blockType.ToString());
                }
            }
            foreach (var label in code.labels) generator.MarkLabel(label);
            Emit(generator, code);
            foreach (var block in code.blocks.Where(block => block.blockType == ExceptionBlockType.EndExceptionBlock)) generator.EndExceptionBlock();
        }
        return replacement.CreateDelegate(Expression.GetDelegateType(Signature(method).Concat(new[] { method.ReturnType }).ToArray()));
    }

    private static Type[] Signature(MethodInfo method) => (method.IsStatic ? Array.Empty<Type>() : new[] { method.DeclaringType! }).Concat(method.GetParameters().Select(parameter => parameter.ParameterType)).ToArray();
    private static DynamicMethod Create(MethodInfo method) => new DynamicMethod("Offline_" + method.Name, method.ReturnType, Signature(method), method.Module, true);

    private static void Emit(ILGenerator generator, CodeInstruction code)
    {
        switch (code.operand)
        {
            case null: generator.Emit(code.opcode); break;
            case MethodInfo method: generator.Emit(code.opcode, method); break;
            case ConstructorInfo constructor: generator.Emit(code.opcode, constructor); break;
            case FieldInfo field: generator.Emit(code.opcode, field); break;
            case Type type: generator.Emit(code.opcode, type); break;
            case LocalBuilder local: generator.Emit(code.opcode, local); break;
            case Label label: generator.Emit(code.opcode, label); break;
            case Label[] labels: generator.Emit(code.opcode, labels); break;
            case string text: generator.Emit(code.opcode, text); break;
            case byte value: generator.Emit(code.opcode, value); break;
            case sbyte value: generator.Emit(code.opcode, value); break;
            case short value: generator.Emit(code.opcode, value); break;
            case int value: generator.Emit(code.opcode, value); break;
            case long value: generator.Emit(code.opcode, value); break;
            case float value: generator.Emit(code.opcode, value); break;
            case double value: generator.Emit(code.opcode, value); break;
            default: throw new NotSupportedException(code.operand.GetType().FullName);
        }
    }
}
