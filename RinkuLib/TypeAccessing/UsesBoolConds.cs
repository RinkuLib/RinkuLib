using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Tools;

namespace RinkuLib.TypeAccessing;
/// <summary>
/// On a parameter object type, names conditions that are always on whenever an instance is used, without a
/// member for each. Handy for a type that should always switch on the same parts of a query.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public class UsesBoolConds(params string[] CondsToUse) : AccessorEmitterHandler {
    private readonly string[] CondsToUse = CondsToUse;
    /// <inheritdoc/>
    public override ITypeAccessorEmitter? GetTypeEmitter(char varChar, int index, Type type, Mapper mapper) {
        foreach (var cond in CondsToUse) {
            if (mapper.GetIndex(cond) != index)
                continue;
            return AlwaysOnConditionEmitter.Instance;
        }
        return null;
    }
}

internal sealed class AlwaysOnConditionEmitter : TypeAccessorEmitterBase {
    internal static readonly AlwaysOnConditionEmitter Instance = new();

    protected override void EmitCondition(ILGenerator il, Type type)
        => il.Emit(OpCodes.Ldc_I4_1);

    protected override void EmitValue(ILGenerator il, Type type) {
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, typeof(bool));
    }
}
