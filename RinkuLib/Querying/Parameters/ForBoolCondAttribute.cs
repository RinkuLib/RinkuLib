using System.Reflection;
using System.Reflection.Emit;
using Rinku.Internal;

namespace Rinku.Querying.Parameters;

/// <summary>
/// On a parameter object, marks a <see cref="bool"/> member as a condition toggle rather than a bound value.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ForBoolCondAttribute : AccessorEmitterHandler {
    /// <inheritdoc/>
    public override IAccessorEmitter? GetMemberEmitter(char varChar, int index, Type type, MemberInfo member, Mapper mapper) {
        if (!(member is PropertyInfo p && p.PropertyType == typeof(bool)
            || member is FieldInfo f && f.FieldType == typeof(bool)))
            throw new RinkuConfigurationException(ErrorCodes.AttributeOnWrongMemberType,
                $"When using {typeof(ForBoolCondAttribute)}, the member must be {typeof(bool)}.");
        return index < 0 ? null : BoolConditionEmitter.Instance;
    }
}

internal sealed class BoolConditionEmitter : PathAccessorEmitterBase {
    internal static readonly BoolConditionEmitter Instance = new();

    protected override void EmitCondition(ILGenerator il, ParameterMemberAccess member)
        => member.EmitLoad(il);

    protected override void EmitValue(ILGenerator il, ParameterMemberAccess member)
        => il.Emit(OpCodes.Ldc_I4_1);
}
