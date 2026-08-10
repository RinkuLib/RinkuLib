using Rinku.Internal;

namespace Rinku.Mapping;

/// <summary>
/// The group boundary rule held by a type mapping or a construction path, the un-negotiated key. In the emit step
/// it is negotiated over the result's columns, resolving alternates, nesting, and generics, to produce the
/// <see cref="GroupingBoundary"/> that emits the resolved key. The rule and the emitter are two things: the rule
/// says what the key is, the boundary is the code that reads and compares it.
/// </summary>
public interface IGroupingRule {
    /// <summary>
    /// Negotiates the key over <paramref name="columns"/> under the same name-matching context
    /// <paramref name="colModifier"/> the ordinary slots negotiated in, and lowers it through
    /// <paramref name="build"/> into the boundary that emits it. The columns the key reads are reused, not
    /// consumed, so the ordinary slots claim them first.
    /// </summary>
    GroupingBoundary MakeBoundary(Type spanningType, ColumnInfo[] columns, ColModifier colModifier, IBoundaryBuild build);
}

/// <summary>A type mapping whose group boundary can be read and replaced.</summary>
public interface ICanUpdateGroupKey {
    /// <summary>The rule that builds the type's group boundary, or <see langword="null"/> when it declares none.</summary>
    IGroupingRule? GroupKey { get; set; }
}
