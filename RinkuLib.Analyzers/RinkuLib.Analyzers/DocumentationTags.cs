using System;
using System.Collections.Generic;
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
    public const string MatchConstructor = "MatchConstructor";
    public const string Schema = "Schema";
    public const string LastUpdated = "LastUpdated";

    public static bool HasTag(MemberDeclarationSyntax declaration, string tagName)
        => GetTags(declaration, tagName).Any();

    public static bool HasSchema(Compilation compilation, CancellationToken cancellationToken) {
        foreach (var tree in compilation.SyntaxTrees) {
            var root = tree.GetRoot(cancellationToken);
            foreach (var declaration in root.DescendantNodes().OfType<MemberDeclarationSyntax>()) {
                if (HasTag(declaration, Schema))
                    return true;
            }
        }
        return false;
    }

    public static IEnumerable<XmlNodeSyntax> GetTags(ISymbol symbol, string tagName, CancellationToken cancellationToken) {
        foreach (var syntaxReference in symbol.DeclaringSyntaxReferences) {
            if (syntaxReference.GetSyntax(cancellationToken) is not MemberDeclarationSyntax declaration)
                continue;
            foreach (var tag in GetTags(declaration, tagName))
                yield return tag;
        }
    }

    public static IEnumerable<XmlNodeSyntax> GetTags(MemberDeclarationSyntax declaration, string tagName) {
        foreach (var trivia in declaration.GetLeadingTrivia()) {
            if (trivia.GetStructure() is not DocumentationCommentTriviaSyntax documentation)
                continue;
            foreach (var node in documentation.Content) {
                if (GetTagName(node) == tagName)
                    yield return node;
            }
        }
    }

    public static ImmutableArray<DocumentationReference> GetReferences(
        INamedTypeSymbol type,
        string tagName,
        Compilation compilation,
        CancellationToken cancellationToken) {
        var references = ImmutableArray.CreateBuilder<DocumentationReference>();
        foreach (var tag in GetTags(type, tagName, cancellationToken)) {
            var symbols = ResolveCref(tag, compilation, cancellationToken);
            foreach (var symbol in symbols)
                references.Add(new DocumentationReference(tag, symbol, GetTimestamp(tag)));
        }
        return references.ToImmutable();
    }

    public static ImmutableArray<ISymbol> ResolveCref(
        XmlNodeSyntax tag,
        Compilation compilation,
        CancellationToken cancellationToken) {
        var cref = GetAttributes(tag).OfType<XmlCrefAttributeSyntax>().FirstOrDefault();
        if (cref is null)
            return [];

        var info = compilation.GetSemanticModel(tag.SyntaxTree).GetSymbolInfo(cref.Cref, cancellationToken);
        if (info.Symbol is not null)
            return [info.Symbol];

        var symbols = ImmutableArray.CreateBuilder<ISymbol>();
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var candidate in info.CandidateSymbols) {
            if (seen.Add(candidate))
                symbols.Add(candidate);
        }
        return symbols.ToImmutable();
    }

    public static DateTimeOffset? GetTimestamp(XmlNodeSyntax tag) {
        foreach (var attribute in GetAttributes(tag).OfType<XmlTextAttributeSyntax>()) {
            if (attribute.Name.LocalName.ValueText != LastUpdated)
                continue;
            if (DateTimeOffset.TryParse(
                attribute.TextTokens.ToString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
                return timestamp;
        }
        return null;
    }

    public static DateTimeOffset? GetLatestSchemaTimestamp(ISymbol symbol, CancellationToken cancellationToken) {
        DateTimeOffset? latest = null;
        foreach (var tag in GetTags(symbol, Schema, cancellationToken)) {
            var timestamp = GetTimestamp(tag);
            if (timestamp.HasValue && (!latest.HasValue || timestamp > latest))
                latest = timestamp;
        }
        return latest;
    }

    public static XmlNodeSyntax? FindTag(SyntaxNode root, TextSpan span, string tagName) {
        return root
            .DescendantNodes(descendIntoTrivia: true)
            .OfType<XmlNodeSyntax>()
            .FirstOrDefault(node => GetTagName(node) == tagName && node.FullSpan.Contains(span.Start));
    }

    public static TypeDeclarationSyntax? FindContainingType(SyntaxNode root, TextSpan span)
        => root
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(type => type.FullSpan.Contains(span.Start))
            .OrderBy(type => type.FullSpan.Length)
            .FirstOrDefault();

    public static SyntaxList<XmlAttributeSyntax> GetAttributes(XmlNodeSyntax tag) => tag switch {
        XmlEmptyElementSyntax empty => empty.Attributes,
        XmlElementSyntax element => element.StartTag.Attributes,
        _ => default
    };

    private static string? GetTagName(XmlNodeSyntax node) => node switch {
        XmlEmptyElementSyntax empty => empty.Name.LocalName.ValueText,
        XmlElementSyntax element => element.StartTag.Name.LocalName.ValueText,
        _ => null
    };
}

internal readonly struct DocumentationReference(
    XmlNodeSyntax tag,
    ISymbol symbol,
    DateTimeOffset? lastUpdated) {
    public XmlNodeSyntax Tag { get; } = tag;
    public ISymbol Symbol { get; } = symbol;
    public DateTimeOffset? LastUpdated { get; } = lastUpdated;
}
