namespace Rinku.Mapping;
/// <summary>Applies a custom column reading order rule to a member or parameter.</summary>
public interface IUsageFlagModifier {
    /// <summary>Adjusts the reading-order flags for the member this is on.</summary>
    public void UpdateFlags(object? param, ref UsageFlags usageFlag);
}
/// <summary>
/// The member may look anywhere in the schema to find its column, not only the one following the last
/// consumed. On a complex-typed slot this frees only the subtree's first claimed column. The rest keep
/// the inherited regime. Use <see cref="CanLookAnywhereSubtreeAttribute"/> to free the whole subtree.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class CanLookAnywhereAttribute : Attribute, IUsageFlagModifier {
    /// <inheritdoc/>
    public void UpdateFlags(object? param, ref UsageFlags usageFlag)
        => usageFlag |= UsageFlags.RemoveSequentialRead;
}
/// <summary>
/// The member must <b>not</b> look anywhere and must take only the column following the last consumed.
/// On a complex-typed slot this constrains only the subtree's first claimed column. The rest keep the
/// inherited regime. Use <see cref="CanNotLookAnywhereSubtreeAttribute"/> to constrain the whole subtree.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class CanNotLookAnywhereAttribute : Attribute, IUsageFlagModifier {
    /// <inheritdoc/>
    public void UpdateFlags(object? param, ref UsageFlags usageFlag)
        => usageFlag |= UsageFlags.SequentialRead;
}
/// <summary>
/// Specifies that a column may be read without consuming it, whether or not an earlier slot consumed it.
/// On a complex-typed slot this applies to the subtree's first claimed column. Use
/// <see cref="MayReuseColSubtreeAttribute"/> for the whole subtree.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class MayReuseColAttribute : Attribute, IUsageFlagModifier {
    /// <inheritdoc/>
    public void UpdateFlags(object? param, ref UsageFlags usageFlag)
        => usageFlag |= UsageFlags.CanReuse;
}
/// <summary>
/// The subtree form of <see cref="CanLookAnywhereAttribute"/>. It allows the whole nested value to
/// look anywhere, not just its first column.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class CanLookAnywhereSubtreeAttribute : Attribute, IUsageFlagModifier {
    /// <inheritdoc/>
    public void UpdateFlags(object? param, ref UsageFlags usageFlag)
        => usageFlag |= UsageFlags.RemoveSequentialRead | UsageFlags.Subtree;
}
/// <summary>
/// The subtree form of <see cref="CanNotLookAnywhereAttribute"/>. It requires the whole nested value to use
/// subtree to sequential reading, not just its first column.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class CanNotLookAnywhereSubtreeAttribute : Attribute, IUsageFlagModifier {
    /// <inheritdoc/>
    public void UpdateFlags(object? param, ref UsageFlags usageFlag)
        => usageFlag |= UsageFlags.SequentialRead | UsageFlags.Subtree;
}
/// <summary>
/// The subtree form of <see cref="MayReuseColAttribute"/>. It lets every column used by the nested value
/// subtree reusable and non-consuming, not just its first claim.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class MayReuseColSubtreeAttribute : Attribute, IUsageFlagModifier {
    /// <inheritdoc/>
    public void UpdateFlags(object? param, ref UsageFlags usageFlag)
        => usageFlag |= UsageFlags.CanReuse | UsageFlags.Subtree;
}
