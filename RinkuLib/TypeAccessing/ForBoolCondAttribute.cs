using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Tools;

namespace RinkuLib.TypeAccessing;

/// <summary>
/// Identifies a member as a boolean condition for SQL templates rather than a variable.
/// </summary>
/// <remarks>
/// <b>Note:</b> Only valid on <see cref="bool"/> fields or properties. 
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ForBoolCondAttribute : AccessorEmiterHandler {
    /// <inheritdoc/>
    public override void HandleEmit(char varChar, IAccessorEmiter?[] usagePlans, IAccessorEmiter?[] valuePlans, Type type, MemberInfo member, Mapper mapper) {
        if (!(member is PropertyInfo p && p.PropertyType == typeof(bool)
            || member is FieldInfo f && f.FieldType == typeof(bool)))
            throw new Exception($"When using {typeof(ForBoolCondAttribute)}, the type must be of type {typeof(bool)}");
        var index = mapper.GetIndex(member.Name);
        if (index < 0)
            return;
        usagePlans[index] = new MemberCondUsageEmitter(type, member);
        valuePlans[index] = TrueValueEmitter.Instance;
    }
}

/// <summary>Generate the IL emit to get the usage of a condition at a specific index (field / prop)</summary>
public class MemberCondUsageEmitter(Type targetType, MemberInfo member) : IAccessorEmiter {
    private readonly Type TargetType = targetType;
    private readonly MemberInfo _member = member;

    /// <inheritdoc/>
    public void Emit(ILGenerator il)
        => IAccessorEmiter.EmitMemberLoad(il, TargetType, _member);
}
/// <summary>Generate the IL emit to return a simple true</summary>
public class TrueValueEmitter : IAccessorEmiter {
    /// <inheritdoc/>
    public static readonly TrueValueEmitter Instance = new();
    private TrueValueEmitter() { }
    /// <inheritdoc/>
    public void Emit(ILGenerator il)
        => il.Emit(OpCodes.Ldc_I4_1);
}