namespace RinkuLib.DbParsing;

/// <summary>
/// Defines an alternative name for a parameter, property, or field during database column matching.
/// </summary>

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public sealed class AltAttribute(string AlternativeName) : Attribute {
    /// <summary>The additional name to check during negotiation.</summary>
    public readonly string AlternativeName = AlternativeName;
}
/// <summary>
/// Defines an alternative name for a parameter, property, or field during database column matching.
/// </summary>

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public sealed class AltSkippingSegmentsAttribute(string AlternativeName, int NbSegmentSpan = 1) : Attribute, INameComparerMaker {
    /// <summary>The additional name to check during negotiation.</summary>
    public readonly string AlternativeName = AlternativeName;
    /// <summary>The amount of segments that spans for</summary>
    public readonly int NbSegmentSpan = NbSegmentSpan;

    /// <inheritdoc/>
    public INameComparer MakeComparer(Type type, ref INameComparer defaultComparer, object[] attributes, object? param)
        => new NameMultiSpan(AlternativeName, NbSegmentSpan);
}
/// <summary>
/// Defines an alternative name for a parameter, property, or field during database column matching.
/// </summary>

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public sealed class AltUpToAttribute(string AlternativeName, string KeyUpTo) : Attribute, INameComparerMaker {
    /// <summary>The additional name to check during negotiation.</summary>
    public readonly string AlternativeName = AlternativeName;
    /// <summary>The key up to where to go, that will skip the segments</summary>
    public readonly string KeyUpTo = KeyUpTo;

    /// <inheritdoc/>
    public INameComparer MakeComparer(Type type, ref INameComparer defaultComparer, object[] attributes, object? param)
        => new NameMultiSpanKey(AlternativeName, KeyUpTo);
}
/// <summary>
/// An interface that can build an INameComparer
/// </summary>
/// <summary>
/// Defines a factory for creating <see cref="INameComparer"/> instances based on reflection metadata.
/// </summary>
public interface INameComparerMaker {
    /// <summary>
    /// Creates a name comparer for a specific member or parameter.
    /// </summary>
    public INameComparer MakeComparer(Type type, ref INameComparer defaultComparer, object[] attributes, object? param);
}
/// <summary>
/// Defines an alternative name for a parameter, property, or field during database column matching.
/// </summary>

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class NoNameAttribute : Attribute;