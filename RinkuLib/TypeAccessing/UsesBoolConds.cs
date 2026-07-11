using System.Reflection;
using RinkuLib.Tools;

namespace RinkuLib.TypeAccessing;
/// <summary>
/// On a parameter object type, names conditions that are always on whenever an instance is used, without a
/// member for each. Handy for a type that should always switch on the same parts of a query.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public class UsesBoolConds(params string[] CondsToUse) : AccessorEmiterHandler {
    private readonly string[] CondsToUse = CondsToUse;
    /// <inheritdoc/>
    public override void HandleEmit(char varChar, IAccessorEmiter?[] usagePlans, IAccessorEmiter?[] valuePlans, Type type, MemberInfo? member, Mapper mapper) {
        foreach (var cond in CondsToUse) {
            var index = mapper.GetIndex(cond);
            if (index < 0)
                continue;
            usagePlans[index] = BasicValueEmitter.TrueValue;
            valuePlans[index] = BoxedBasicValueEmitter.TrueValue;
        }
    }
}
