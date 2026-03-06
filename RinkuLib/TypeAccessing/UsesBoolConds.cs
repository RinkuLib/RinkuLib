using System.Reflection;
using RinkuLib.Tools;

namespace RinkuLib.TypeAccessing;
/// <summary>To staticaly indicate bool conditions to be always used</summary>
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
