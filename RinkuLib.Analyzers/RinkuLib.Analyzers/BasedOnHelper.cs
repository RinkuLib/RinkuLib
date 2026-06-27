using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RinkuLib.Analyzers; 
public static class BasedOnHelper {
    public static bool HasTag(MemberDeclarationSyntax declaration, string tagName) {
        foreach (var trivia in declaration.GetLeadingTrivia()) {
            if (trivia.GetStructure() is not DocumentationCommentTriviaSyntax doc)
                continue;

            foreach (var node in doc.Content) {
                if (node is XmlEmptyElementSyntax empty &&
                    empty.Name.LocalName.ValueText == tagName)
                    return true;

                if (node is XmlElementSyntax element &&
                    element.StartTag.Name.LocalName.ValueText == tagName)
                    return true;
            }

            return false;
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
        DocumentationCommentTriviaSyntax? documentation = null;
        foreach (var trivia in declaration.GetLeadingTrivia()) {
            if (trivia.GetStructure() is DocumentationCommentTriviaSyntax doc) {
                documentation = doc;
                break;
            }
        }

        if (documentation == null)
            yield break;

        foreach (var node in documentation.Content) {
            if (node is XmlEmptyElementSyntax empty && empty.Name.LocalName.ValueText == tagName)
                yield return empty;
            else if (node is XmlElementSyntax element && element.StartTag.Name.LocalName.ValueText == tagName)
                yield return element;
        }
    }
    public static IEnumerable<ISymbol> GetBasedOnSymbols(INamedTypeSymbol type, Compilation compilation, CancellationToken cancellationToken) {
        foreach (var tag in GetTags(type, "BasedOn", cancellationToken)) {
            var attributes = tag switch {
                XmlEmptyElementSyntax e => e.Attributes,
                XmlElementSyntax e => e.StartTag.Attributes,
                _ => default
            };
            foreach (var attribute in attributes) {
                if (attribute is not XmlCrefAttributeSyntax crefAttribute)
                    continue;
                var semanticModel = compilation.GetSemanticModel(tag.SyntaxTree);
                var symbolInfo = semanticModel.GetSymbolInfo(crefAttribute.Cref, cancellationToken);

                if (symbolInfo.Symbol != null)
                    yield return symbolInfo.Symbol;
                foreach (var item in symbolInfo.CandidateSymbols)
                    yield return item;
            }
        }
    }
    public static IEnumerable<string> GetAttributes(XmlNodeSyntax tag, string attributeName) {
        var attributes = tag switch {
            XmlEmptyElementSyntax e => e.Attributes,
            XmlElementSyntax e => e.StartTag.Attributes,
            _ => default
        };
        foreach (var attribute in attributes)
            if (attribute is XmlTextAttributeSyntax textAttribute && textAttribute.Name.LocalName.ValueText == attributeName)
                yield return textAttribute.TextTokens.ToString();
    }
}