using System.Reflection;

namespace Rinku.Mapping;

/// <summary>
/// Marks the value that separates objects in a spanning mapping.
/// Apply it to several members or constructor parameters for a combined key.
/// Apply it to a static boundary method for a custom comparison.
/// A key on the selected constructor or factory takes priority over a key on the type.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class GroupKeyAttribute : Attribute, IGroupingRuleMaker {
    /// <summary>Allows members and parameters to form a combined key. A boundary method must stand alone.</summary>
    public bool Composes(ICustomAttributeProvider carrier) => carrier is not MethodBase;
    /// <inheritdoc/>
    public IGroupingRule MakeRule(IReadOnlyList<ICustomAttributeProvider> carriers) => carriers[0] switch {
        MethodInfo method => new MethodGroupingRule(method),
        ParameterInfo => new EqualityGroupingRule([.. carriers.Cast<ParameterInfo>()]),
        _ => new EqualityGroupingRule([.. carriers.Cast<MemberInfo>()]),
    };
}
