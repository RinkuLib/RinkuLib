using System.Reflection;
using System.Reflection.Emit;
using Rinku.Internal;

namespace Rinku.Querying.Parameters;

/// <summary>
/// On a parameter object, marks a <see cref="bool"/> member as a condition toggle rather than a bound value.
/// The member's name switches a conditional part of the query on when it is <see langword="true"/> and off
/// when it is <see langword="false"/>, instead of becoming a <c>@name</c> parameter. Valid only on
/// <see cref="bool"/> fields or properties.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ForBoolCondAttribute : AccessorEmitterHandler {
    /// <inheritdoc/>
    public override IAccessorEmitter? GetMemberEmitter(char varChar, int index, Type type, MemberInfo member, Mapper mapper) {
        if (!(member is PropertyInfo p && p.PropertyType == typeof(bool)
            || member is FieldInfo f && f.FieldType == typeof(bool)))
            throw new RinkuConfigurationException(ErrorCodes.AttributeOnWrongMemberType, $"When using {typeof(ForBoolCondAttribute)}, the type must be of type {typeof(bool)}");
        if (index < 0)
            return null;
        return BoolConditionEmitter.Instance;
    }
}

internal sealed class BoolConditionEmitter : AccessorEmitterBase {
    internal static readonly BoolConditionEmitter Instance = new();

    protected override void EmitCondition(ILGenerator il, Type type, MemberInfo member)
        => AccessorEmitter.EmitMemberLoad(il, type, member);

    protected override void EmitValue(ILGenerator il, Type type, MemberInfo member) {
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, typeof(bool));
    }
}
