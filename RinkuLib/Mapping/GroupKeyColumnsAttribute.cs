using System.Reflection;

namespace Rinku.Mapping;

/// <summary>
/// Groups a spanning type by named columns directly, no member required. The boundary reads each named column as
/// whatever type it carries, so nothing is stored for the key. It is the type's group key, the same as a marked
/// member, and a convenience over the <see cref="IGroupingRuleMaker"/> seam a rule of your own would use.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class GroupKeyColumnsAttribute(params string[] columns) : Attribute, IGroupingRuleMaker {
    private readonly string[] Columns = columns;
    /// <summary>A column-name key is the only group key on its type.</summary>
    public bool Composes(ICustomAttributeProvider carrier) => false;
    /// <inheritdoc/>
    public IGroupingRule MakeRule(IReadOnlyList<ICustomAttributeProvider> carriers) => new EqualityGroupingRule(Columns);
}
