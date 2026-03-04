using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Tools;

namespace RinkuLib.TypeAccessing;

/// <summary>
/// Confirm that the string value is not null or whitespace.
/// </summary>
/// <remarks>
/// <b>Note:</b> Only valid on <see cref="string"/> fields or properties. 
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class NotNullOrWhitespaceAttribute : AccessorEmiterHandler {
    /// <inheritdoc/>
    public override void HandleEmit(char varChar, IAccessorEmiter?[] usagePlans, IAccessorEmiter?[] valuePlans, Type type, MemberInfo member, Mapper mapper) {
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