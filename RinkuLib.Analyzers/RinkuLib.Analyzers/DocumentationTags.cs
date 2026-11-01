using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace RinkuLib.Analyzers;

internal static class DocumentationTags {
    public const string BasedOn = "BasedOn";
    public const string Schema = "Schema";
    public const string LastUpdatedParameter = "lastUpdated";
    public const string DeclaringTypeParameter = "declaringType";
    public const string MethodNameParameter = "methodName";

    private const string SchemaAttributeMetadataName = "Rinku.Mapping.SchemaAttribute";
    private const string GeneratedSchemaAttributeMetadataName = "RinkuPowerTools.Generated.SchemaAttribute";
    private const string BasedOnAttributeMetadataName = "Rinku.Mapping.BasedOnAttribute";
    public static bool HasTag(MemberDeclarationSyntax declaration, string tagName)
        => declaration.AttributeLists.SelectMany(list => list.Attributes).Any(attribute => GetAttributeName(attribute) == tagName);

    public static bool HasTag(ISymbol symbol, string tagName)
        => symbol.GetAttributes().Any(attribute => IsTag(attribute, tagName));

    public static bool HasSchema(Compilation compilation, CancellationToken cancellationToken) {
        foreach (SyntaxTree tree in compilation.SyntaxTrees) {
            var root = tree.GetRoot(cancellationToken);
            foreach (var declaration in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
                if (HasTag(declaration, Schema))
                    return true;
        }
        return false;
    }

    public static ImmutableArray<DocumentationReference> GetReferences(INamedTypeSymbol type, string tagName, Compilation compilation, CancellationToken cancellationToken) {
        var references = ImmutableArray.CreateBuilder<DocumentationReference>();
        foreach (AttributeData attribute in type.GetAttributes()) {
            if (!IsTag(attribute, tagName))
                continue;
            ImmutableArray<ISymbol> symbols = ResolveAttribute(attribute);
            if (symbols.Length != 0)
                references.Add(new DocumentationReference(attribute, symbols, GetTimestamp(attribute)));
        }
        return references.ToImmutable();
    }

    public static ImmutableArray<ISymbol> ResolveAttribute(AttributeData attribute) {
        if (attribute.ConstructorArguments.Length == 0 || attribute.ConstructorArguments[0].Value is not ITypeSymbol declaringType)
            return [];

        if (attribute.ConstructorArguments.Length < 3 || attribute.ConstructorArguments[2].IsNull)
            return [declaringType];

        if (attribute.ConstructorArguments[2].Kind != TypedConstantKind.Array
            && attribute.ConstructorArguments[2].Value is string methodName
            && declaringType is INamedTypeSymbol namedType)
            return [.. namedType.GetMembers(methodName).OfType<IMethodSymbol>()];

        return [];
    }

    public static DateTimeOffset? GetTimestamp(AttributeData attribute) {
        int index = IsTag(attribute, Schema) ? 0 : 1;
        return attribute.ConstructorArguments.Length > index && attribute.ConstructorArguments[index].Value is string value && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp) ? timestamp : null;
    }

    public static AttributeArgumentSyntax? GetTimestampArgument(AttributeSyntax attribute) {
        var arguments = attribute.ArgumentList?.Arguments ?? default;
        return arguments.FirstOrDefault(argument => argument.NameColon?.Name.Identifier.ValueText == LastUpdatedParameter) ?? (arguments.Count > 1 && arguments[1].NameColon is null ? arguments[1] : null);
    }

    public static DateTimeOffset? GetLatestSchemaTimestamp(ISymbol symbol, CancellationToken cancellationToken) {
        DateTimeOffset? latest = null;
        foreach (AttributeData attribute in symbol.GetAttributes()) {
            if (!IsTag(attribute, Schema) || GetTimestamp(attribute) is not { } timestamp)
                continue;
            if (!latest.HasValue || timestamp > latest)
                latest = timestamp;
        }
        return latest;
    }

    public static DateTimeOffset? GetLatestSchemaTimestamp(ImmutableArray<ISymbol> symbols, CancellationToken cancellationToken) {
        DateTimeOffset? latest = null;
        foreach (ISymbol symbol in symbols) {
            DateTimeOffset? timestamp = GetLatestSchemaTimestamp(symbol, cancellationToken);
            if (timestamp.HasValue && (!latest.HasValue || timestamp > latest))
                latest = timestamp;
        }
        return latest;
    }

    public static AttributeSyntax? FindTag(SyntaxNode root, TextSpan span, string tagName)
        => root.DescendantNodes().OfType<AttributeSyntax>().FirstOrDefault(attribute => GetAttributeName(attribute) == tagName && attribute.FullSpan.Contains(span.Start));

    public static TypeDeclarationSyntax? FindContainingType(SyntaxNode root, TextSpan span)
        => root.DescendantNodes().OfType<TypeDeclarationSyntax>().Where(type => type.FullSpan.Contains(span.Start)).OrderBy(type => type.FullSpan.Length).FirstOrDefault();

    public static Location GetLocation(AttributeData attribute, CancellationToken cancellationToken)
        => attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is AttributeSyntax syntax
            ? syntax.GetLocation()
            : Location.None;

    public static string GetAttributeName(AttributeSyntax attribute) {
        string name = attribute.Name.ToString();
        int separator = name.LastIndexOf('.');
        if (separator >= 0)
            name = name.Substring(separator + 1);
        return name.EndsWith("Attribute", StringComparison.Ordinal) ? name.Substring(0, name.Length - 9) : name;
    }

    private static bool IsTag(AttributeData attribute, string tagName) {
        string? metadataName = GetMetadataName(attribute.AttributeClass);
        return tagName switch {
            Schema => metadataName is SchemaAttributeMetadataName or GeneratedSchemaAttributeMetadataName,
            BasedOn => metadataName is BasedOnAttributeMetadataName,
            _ => false
        };
    }

    private static string? GetMetadataName(INamedTypeSymbol? type) {
        if (type is null)
            return null;
        string namespaceName = type.ContainingNamespace.IsGlobalNamespace ? string.Empty : type.ContainingNamespace.ToDisplayString() + ".";
        return namespaceName + type.MetadataName;
    }
}

internal readonly struct DocumentationReference(AttributeData attribute, ImmutableArray<ISymbol> symbols, DateTimeOffset? lastUpdated) {
    public AttributeData Attribute { get; } = attribute;
    public ImmutableArray<ISymbol> Symbols { get; } = symbols;
    public ISymbol Symbol => Symbols[0];
    public DateTimeOffset? LastUpdated { get; } = lastUpdated;
    public Location Location(CancellationToken cancellationToken) => DocumentationTags.GetLocation(Attribute, cancellationToken);
}
