using System.Reflection;
using System.Reflection.Emit;
using Rinku.Internal;

namespace Rinku.Querying.Parameters;

/// <summary>
/// On a parameter object, treats a <see cref="string"/> member as present only when it is not null or
/// whitespace. A blank value then counts as absent, so its optional clause drops instead of filtering on an
/// empty string. Valid only on <see cref="string"/> fields or properties.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class NotNullOrWhitespaceAttribute : AccessorEmitterHandler {
    private static readonly MethodConditionEmitter Emitter = new(
        typeof(string).GetMethod(nameof(string.IsNullOrWhiteSpace), [typeof(string)])!, invert: true);

    /// <inheritdoc/>
    public override IAccessorEmitter? GetMemberEmitter(char varChar, int index, Type type, MemberInfo member, Mapper mapper) {
        if (!(member is PropertyInfo p && p.PropertyType == typeof(string)
            || member is FieldInfo f && f.FieldType == typeof(string)))
            throw new RinkuConfigurationException(ErrorCodes.AttributeOnWrongMemberType, $"When using {typeof(NotNullOrWhitespaceAttribute)}, the type must be of type {typeof(string)}");
        if (index < 0)
            return null;
        return Emitter;
    }
}
/// <summary>
/// On a parameter object, treats a member as present only when it is not its type's default (zero, or
/// <see langword="null"/>). A default value then counts as absent and its optional clause drops.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class NotDefaultAttribute : AccessorEmitterHandler {
    /// <inheritdoc/>
    public override IAccessorEmitter? GetMemberEmitter(char varChar, int index, Type type, MemberInfo member, Mapper mapper) {
        if (index < 0)
            return null;
        return NotDefaultEmitter.Instance;
    }
}
internal sealed class NotDefaultEmitter : AccessorEmitterBase {
    internal static readonly NotDefaultEmitter Instance = new();

    protected override void EmitCondition(ILGenerator il, Type type, MemberInfo member) {
        var targetMember = member;
        Type memberType = targetMember is FieldInfo f ? f.FieldType : ((PropertyInfo)targetMember).PropertyType;
        AccessorEmitter.EmitMemberLoad(il, type, targetMember);
        if (!memberType.IsValueType || (memberType.IsPrimitive && memberType != typeof(double) && memberType != typeof(float))) {
            if (memberType.IsValueType)
                il.Emit(OpCodes.Ldc_I4_0);
            else
                il.Emit(OpCodes.Ldnull);

            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ceq);
            return;
        }
        var eqType = typeof(EqualityComparer<>).MakeGenericType(memberType);
        var defaultProp = eqType.GetProperty(nameof(EqualityComparer<>.Default))!;
        var equalsMethod = eqType.GetMethod(nameof(EqualityComparer<>.Equals), [memberType, memberType])!;

        LocalBuilder value = il.DeclareLocal(memberType);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Call, defaultProp.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, value);
        LocalBuilder tempDefault = il.DeclareLocal(memberType);
        il.Emit(OpCodes.Ldloca_S, tempDefault);
        il.Emit(OpCodes.Initobj, memberType);
        il.Emit(OpCodes.Ldloc, tempDefault);
        il.Emit(OpCodes.Callvirt, equalsMethod);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
    }

    protected override void EmitValue(ILGenerator il, Type type, MemberInfo member)
        => AccessorEmitter.EmitMemberValue(il, type, member);
}
