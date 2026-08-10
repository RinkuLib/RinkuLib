namespace Rinku.Mapping.Defaults;

/// <summary>The shipped metadata factory used by the mapping registry.</summary>
public sealed class DefaultTypeParsingInfoFactory : ITypeParsingInfoFactory {
    /// <inheritdoc/>
    public TypeParsingInfo Scalar => BaseTypeInfo.Instance;
    /// <inheritdoc/>
    public TypeParsingInfo Array => MultiRowTypeParsingInfo.ForArray;
    /// <inheritdoc/>
    public TypeParsingInfo Create(Type type) => new DefaultTypeParsingInfo(type);
}
