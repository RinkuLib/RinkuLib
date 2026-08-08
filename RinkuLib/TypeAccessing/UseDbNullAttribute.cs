using System.Data;
using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Queries;
using RinkuLib.Tools;

namespace RinkuLib.TypeAccessing;

/// <summary>
/// On a parameter member, or every member of a parameter type, keeps a null value supplied and sends it to
/// the database as <see cref="DBNull.Value"/>. A member attribute takes priority over this type-level rule.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface |
                AttributeTargets.Field | AttributeTargets.Property)]
public sealed class UseDbNullAttribute : AccessorEmitterHandler {
    /// <inheritdoc/>
    public override IAccessorEmitter? GetMemberEmitter(char varChar, int index, Type type, MemberInfo member, Mapper mapper)
        => index < 0 ? null : UseDbNullEmitter.Instance;
}

/// <summary>
/// Deliberately implements the raw interface instead of <see cref="AccessorEmitterBase"/>: this rule has no
/// usability test. It always marks the key used and converts only a null value to <see cref="DBNull.Value"/>.
/// </summary>
internal sealed class UseDbNullEmitter : IAccessorEmitter {
    internal static readonly UseDbNullEmitter Instance = new();

    private static readonly MethodInfo SpanItem = typeof(Span<bool>).GetProperty("Item")!.GetMethod!;
    private static readonly MethodInfo DbParamUse = typeof(DbParamInfo).GetMethod(
        nameof(DbParamInfo.Use), [typeof(string), typeof(IDbCommand), typeof(object)])!;
    private static readonly MethodInfo ToDbValueMethod = typeof(UseDbNullEmitter).GetMethod(
        nameof(ToDbValue), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly FieldInfo UsageMarker = typeof(AccessorUsageMarker).GetField(
        nameof(AccessorUsageMarker.Value), BindingFlags.Static | BindingFlags.NonPublic)!;

    public void Validate(Type type, MemberInfo member) { }

    public void Emit(ILGenerator il, int index, string key, Type type, MemberInfo member,
        LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue) {
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Call, SpanItem);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stind_I1);

        if (!bindValue)
            return;

        if (handlerValue) {
            il.Emit(OpCodes.Ldloc, handlerValues!);
            il.Emit(OpCodes.Ldc_I4, handlerIndex);
            EmitDbValue(il, type, member);
            il.Emit(OpCodes.Stelem_Ref);
            return;
        }

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldstr, key);
        il.Emit(OpCodes.Ldarg_1);
        EmitDbValue(il, type, member);
        il.Emit(OpCodes.Callvirt, DbParamUse);
        il.Emit(OpCodes.Pop);
    }

    public void EmitUseWith(ILGenerator il, int index, Type type, MemberInfo member, bool bindValue) {
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, index);
        if (bindValue)
            EmitDbValue(il, type, member);
        else
            il.Emit(OpCodes.Ldsfld, UsageMarker);
        il.Emit(OpCodes.Stelem_Ref);
    }

    private static void EmitDbValue(ILGenerator il, Type type, MemberInfo member) {
        AccessorEmitter.EmitMemberValue(il, type, member);
        il.Emit(OpCodes.Call, ToDbValueMethod);
    }

    private static object ToDbValue(object? value) => value ?? DBNull.Value;
}
