using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Tools;

namespace RinkuLib.TypeAccessing;

/// <summary>
/// On a parameter object, treats a <see cref="string"/> member as present only when it is not null or
/// whitespace. A blank value then counts as absent, so its optional clause drops instead of filtering on an
/// empty string. Valid only on <see cref="string"/> fields or properties.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class NotNullOrWhitespaceAttribute : AccessorEmiterHandler {
    /// <inheritdoc/>
    public override void HandleEmit(char varChar, IAccessorEmiter?[] usagePlans, IAccessorEmiter?[] valuePlans, Type type, MemberInfo? member, Mapper mapper) {
        ArgumentNullException.ThrowIfNull(member);
        if (!(member is PropertyInfo p && p.PropertyType == typeof(string)
            || member is FieldInfo f && f.FieldType == typeof(string)))
            throw new Exception($"When using {typeof(NotNullOrWhitespaceAttribute)}, the type must be of type {typeof(string)}");
        var index = mapper.GetIndex(varChar, member.Name);
        if (index < 0)
            return;
        usagePlans[index] = new StringUsageEmitter(type, member);
        valuePlans[index] = new MemberValueEmitter(type, member);
    }
}
/// <summary>
/// On a parameter object, treats a member as present only when it is not its type's default (zero, or
/// <see langword="null"/>). A default value then counts as absent and its optional clause drops.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class NotDefaultAttribute : AccessorEmiterHandler {
    /// <inheritdoc/>
    public override void HandleEmit(char varChar, IAccessorEmiter?[] usagePlans, IAccessorEmiter?[] valuePlans, Type type, MemberInfo? member, Mapper mapper) {
        ArgumentNullException.ThrowIfNull(member);

        var index = mapper.GetIndex(varChar, member.Name);
        if (index < 0)
            return;

        usagePlans[index] = new NotDefaultUsageEmitter(type, member);
        valuePlans[index] = new MemberValueEmitter(type, member);
    }
}
/// <summary>Generate the IL emit to check if the value is not null of whitespace</summary>
public class StringUsageEmitter(Type targetType, MemberInfo member) : IAccessorEmiter {
    private readonly Type TargetType = targetType;
    private readonly MemberInfo _member = member;

    private static readonly MethodInfo IsNullOrWhiteSpaceMethod =
        typeof(string).GetMethod(nameof(string.IsNullOrWhiteSpace), [typeof(string)])!; 

    /// <inheritdoc/>
    public void Emit(ILGenerator il) {
        IAccessorEmiter.EmitMemberLoad(il, TargetType, _member);
        il.Emit(OpCodes.Call, IsNullOrWhiteSpaceMethod);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
    }
}
/// <summary>Generate the IL emit to check if the value is not default</summary>
public class NotDefaultUsageEmitter(Type targetType, MemberInfo member) : IAccessorEmiter {
    private readonly Type TargetType = targetType;
    private readonly MemberInfo _member = member;

    /// <inheritdoc/>
    public void Emit(ILGenerator il) {
        Type mType = _member is FieldInfo f ? f.FieldType : ((PropertyInfo)_member).PropertyType;
        IAccessorEmiter.EmitMemberLoad(il, TargetType, _member);
        if (!mType.IsValueType || (mType.IsPrimitive && mType != typeof(double) && mType != typeof(float))) {
            if (mType.IsValueType)
                il.Emit(OpCodes.Ldc_I4_0);
            else
                il.Emit(OpCodes.Ldnull);

            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ceq);
            return;
        }
        var eqType = typeof(EqualityComparer<>).MakeGenericType(mType);
        var defaultProp = eqType.GetProperty(nameof(EqualityComparer<>.Default))!;
        var equalsMethod = eqType.GetMethod(nameof(EqualityComparer<>.Equals), [mType, mType])!;

        LocalBuilder tempDefault = il.DeclareLocal(mType);
        il.Emit(OpCodes.Ldloca_S, tempDefault);
        il.Emit(OpCodes.Initobj, mType);
        il.Emit(OpCodes.Ldloc, tempDefault);

        il.Emit(OpCodes.Call, defaultProp.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, tempDefault);
        il.Emit(OpCodes.Callvirt, equalsMethod);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
    }
}