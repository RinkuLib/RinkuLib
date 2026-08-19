using System.Reflection;
using System.Reflection.Emit;
using Rinku.Internal;

namespace Rinku.Querying.Parameters;

/// <summary>
/// Treats a <see cref="string"/> member as present only when it is not null or whitespace.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class NotNullOrWhitespaceAttribute : AccessorEmitterHandler {
    private static readonly MethodConditionEmitter Emitter = new(
        typeof(string).GetMethod(nameof(string.IsNullOrWhiteSpace), [typeof(string)])!, invert: true);

    /// <inheritdoc/>
    public override IAccessorEmitter? GetMemberEmitter(char varChar, int index, Type type, MemberInfo member, Mapper mapper) {
        if (!(member is PropertyInfo p && p.PropertyType == typeof(string)
            || member is FieldInfo f && f.FieldType == typeof(string)))
            throw new RinkuConfigurationException(ErrorCodes.AttributeOnWrongMemberType,
                $"When using {typeof(NotNullOrWhitespaceAttribute)}, the member must be {typeof(string)}.");
        return index < 0 ? null : Emitter;
    }
}

/// <summary>Treats a member as present only when it is not its type's default value.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class NotDefaultAttribute : AccessorEmitterHandler {
    /// <inheritdoc/>
    public override IAccessorEmitter? GetMemberEmitter(char varChar, int index, Type type, MemberInfo member, Mapper mapper)
        => index < 0 ? null : NotDefaultEmitter.Instance;
}

internal sealed class NotDefaultEmitter : PathAccessorEmitterBase {
    internal static readonly NotDefaultEmitter Instance = new();

    protected override void EmitCondition(ILGenerator il, ParameterMemberAccess member) {
        Type memberType = member.MemberType;
        if (!memberType.IsValueType) {
            member.EmitLoad(il);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Cgt_Un);
            return;
        }

        Type? nullableType = Nullable.GetUnderlyingType(memberType);
        if (nullableType is not null) {
            LocalBuilder value = il.DeclareLocal(memberType);
            member.EmitLoad(il);
            il.Emit(OpCodes.Stloc, value);
            il.Emit(OpCodes.Ldloca, value);
            il.Emit(OpCodes.Call, memberType.GetProperty(nameof(Nullable<int>.HasValue))!.GetMethod!);
            return;
        }

        var comparerType = typeof(EqualityComparer<>).MakeGenericType(memberType);
        MethodInfo equals = comparerType.GetMethod(nameof(EqualityComparer<int>.Equals), [memberType, memberType])!;
        LocalBuilder current = il.DeclareLocal(memberType);
        LocalBuilder defaultValue = il.DeclareLocal(memberType);
        member.EmitLoad(il);
        il.Emit(OpCodes.Stloc, current);
        il.Emit(OpCodes.Ldloca, defaultValue);
        il.Emit(OpCodes.Initobj, memberType);
        il.Emit(OpCodes.Call, comparerType.GetProperty(nameof(EqualityComparer<int>.Default))!.GetMethod!);
        il.Emit(OpCodes.Ldloc, current);
        il.Emit(OpCodes.Ldloc, defaultValue);
        il.Emit(OpCodes.Callvirt, equals);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
    }

    protected override void EmitValue(ILGenerator il, ParameterMemberAccess member)
        => member.EmitLoad(il);
}
