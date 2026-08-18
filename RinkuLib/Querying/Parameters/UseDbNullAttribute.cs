using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Querying.Parameters;

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

internal sealed class UseDbNullEmitter : PathAccessorEmitterBase {
    internal static readonly UseDbNullEmitter Instance = new();
    private static readonly MethodInfo ToDbValueMethod = typeof(UseDbNullEmitter).GetMethod(
        nameof(ToDbValue), BindingFlags.Static | BindingFlags.NonPublic)!;

    protected override void EmitCondition(ILGenerator il, ParameterMemberAccess member)
        => il.Emit(OpCodes.Ldc_I4_1);

    protected override void EmitValue(ILGenerator il, ParameterMemberAccess member)
        => member.EmitLoad(il);

    protected override void EmitParameterValue(ILGenerator il, ParameterMemberAccess member) {
        member.EmitValue(il);
        il.Emit(OpCodes.Call, ToDbValueMethod);
    }

    private static object ToDbValue(object? value) => value ?? DBNull.Value;
}
