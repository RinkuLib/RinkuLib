using Rinku.Internal;

namespace Rinku.Mapping;

/// <summary>
/// Creates the group boundary for a spanning mapping.
/// Implement this interface when the built in group key attributes cannot express the required rule.
/// </summary>
public interface IGroupingRule {
    /// <summary>
    /// Creates a boundary for the supplied result columns.
    /// Key columns must be read with reuse so normal members may also use them.
    /// </summary>
    /// <returns>The boundary, or <see langword="null"/> to try the next grouping rule.</returns>
    GroupingBoundary? MakeBoundary(Type spanningType, ColumnInfo[] columns, ColModifier colModifier, IBoundaryBuild build);
}

/// <summary>A type mapping whose group boundary can be read and replaced.</summary>
public interface ICanUpdateGroupKey {
    /// <summary>The type-level grouping option, or <see langword="null"/> when none is configured.</summary>
    IGroupingRule? GroupKey { get; set; }
}
