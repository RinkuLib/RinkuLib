namespace RinkuLib.DbParsing;

/// <summary>
/// Adds another name a column may go by when matching this parameter, property, or field. Put it where the
/// column name differs from the member name, or apply it more than once for several alternatives.
/// </summary>

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public sealed class AltAttribute(string AlternativeName) : Attribute {
    /// <summary>The extra name a column may use to match this member.</summary>
    public readonly string AlternativeName = AlternativeName;
}
/// <summary>
/// Adds an alternative name that also consumes a set number of name segments, for reaching a member nested
/// that many levels down when a column name is flattened.
/// </summary>

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public sealed class AltSkippingSegmentsAttribute(string AlternativeName, int NbSegmentSpan = 1) : Attribute, INameComparerMaker {
    /// <summary>The extra name a column may use to match this member.</summary>
    public readonly string AlternativeName = AlternativeName;
    /// <summary>How many name segments this alternative spans.</summary>
    public readonly int NbSegmentSpan = NbSegmentSpan;

    /// <inheritdoc/>
    public INameComparer MakeComparer(Type type, ref INameComparer defaultComparer, object[] attributes, object? param)
        => new NameMultiSpan(AlternativeName, NbSegmentSpan);
}
/// <summary>
/// Adds an alternative name that consumes segments up to a given key, for reaching a member across a
/// flattened name whose depth is named rather than counted.
/// </summary>

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public sealed class AltUpToAttribute(string AlternativeName, string KeyUpTo) : Attribute, INameComparerMaker {
    /// <summary>The extra name a column may use to match this member.</summary>
    public readonly string AlternativeName = AlternativeName;
    /// <summary>The key the alternative spans up to, skipping the segments before it.</summary>
    public readonly string KeyUpTo = KeyUpTo;

    /// <inheritdoc/>
    public INameComparer MakeComparer(Type type, ref INameComparer defaultComparer, object[] attributes, object? param)
        => new NameMultiSpanKey(AlternativeName, KeyUpTo);
}
/// <summary>
/// Builds the name matcher for a member from an attribute, the seam a custom matching attribute implements.
/// </summary>
public interface INameComparerMaker {
    /// <summary>
    /// Builds the matcher for a member or parameter from its reflection metadata.
    /// </summary>
    public INameComparer MakeComparer(Type type, ref INameComparer defaultComparer, object[] attributes, object? param);
}
/// <summary>
/// Drops a member's own name from matching, so it matches only through its columns or alternatives rather
/// than by its identifier. Used for a nested value that has no name of its own in the result.
/// </summary>

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class NoNameAttribute : Attribute;