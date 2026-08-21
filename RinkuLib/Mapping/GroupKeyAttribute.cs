using System.Reflection;

namespace Rinku.Mapping;

/// <summary>
/// Marks a grouping value. Several declarations form a combined key.
/// Construction keys are tried before type keys. A rule that returns no boundary allows the next option.
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
