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
        Rinku.Mapping.TypeExtensions.Require(typeof(string).GetMethod(nameof(string.IsNullOrWhiteSpace), [typeof(string)]), nameof(string.IsNullOrWhiteSpace)), invert: true);

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
            var hasValue = Rinku.Mapping.TypeExtensions.Require(memberType.GetProperty(nameof(Nullable<>.HasValue)), "Nullable.HasValue");
            il.Emit(OpCodes.Call, Rinku.Mapping.TypeExtensions.Require(hasValue.GetMethod, "Nullable.HasValue.GetMethod"));
            return;
        }

        var comparerType = typeof(EqualityComparer<>).MakeGenericType(memberType);
        MethodInfo equals = Rinku.Mapping.TypeExtensions.Require(comparerType.GetMethod(nameof(EqualityComparer<>.Equals), [memberType, memberType]), "EqualityComparer.Equals");
        LocalBuilder current = il.DeclareLocal(memberType);
        LocalBuilder defaultValue = il.DeclareLocal(memberType);
        member.EmitLoad(il);
        il.Emit(OpCodes.Stloc, current);
        il.Emit(OpCodes.Ldloca, defaultValue);
        il.Emit(OpCodes.Initobj, memberType);
        var defaultProperty = Rinku.Mapping.TypeExtensions.Require(comparerType.GetProperty(nameof(EqualityComparer<>.Default)), "EqualityComparer.Default");
        il.Emit(OpCodes.Call, Rinku.Mapping.TypeExtensions.Require(defaultProperty.GetMethod, "EqualityComparer.Default.GetMethod"));
        il.Emit(OpCodes.Ldloc, current);
        il.Emit(OpCodes.Ldloc, defaultValue);
        il.Emit(OpCodes.Callvirt, equals);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
    }

    protected override void EmitValue(ILGenerator il, ParameterMemberAccess member)
        => member.EmitLoad(il);
}
