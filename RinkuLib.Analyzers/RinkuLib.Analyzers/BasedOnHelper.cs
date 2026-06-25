using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RinkuLib.Analyzers {
    public static class BasedOnHelper {
        public static bool TryGetTag(INamedTypeSymbol type, string tagName, CancellationToken cancellationToken, out XmlEmptyElementSyntax? tag) {
            tag = null;

            foreach (var syntaxReference in type.DeclaringSyntaxReferences) {
                if (syntaxReference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax declaration)
                    continue;

                if (!TryGetTag(declaration, tagName, out tag))
                    continue;

                return true;
            }

            return false;
        }

        public static bool TryGetTag(TypeDeclarationSyntax declaration, string tagName, out XmlEmptyElementSyntax? tag) {
            tag = null;

            var documentation = GetDocumentation(declaration);

            if (documentation == null)
                return false;

            foreach (var node in documentation.Content) {
                if (node is not XmlEmptyElementSyntax element)
                    continue;

                if (element.Name.LocalName.Text != tagName)
                    continue;

                tag = element;
                return true;
            }

            return false;
        }

        public static bool TryGetBasedOnType(INamedTypeSymbol type, Compilation compilation, CancellationToken cancellationToken, out INamedTypeSymbol? basedOnType) {
            basedOnType = null;

            if (!TryGetTag(type, "BasedOn", cancellationToken, out var tag))
                return false;

            foreach (var attribute in tag!.Attributes) {
                if (attribute is not XmlCrefAttributeSyntax crefAttribute)
                    continue;

                var semanticModel = compilation.GetSemanticModel(tag.SyntaxTree);
                var symbolInfo = semanticModel.GetSymbolInfo(crefAttribute.Cref, cancellationToken);

                var symbol = symbolInfo.Symbol;

                if (symbol == null && symbolInfo.CandidateSymbols.Length == 1)
                    symbol = symbolInfo.CandidateSymbols[0];

                basedOnType = symbol as INamedTypeSymbol;
                return basedOnType != null;
            }

            return false;
        }

        public static bool TryGetAttribute(XmlEmptyElementSyntax tag, string attributeName, out string? value) {
            value = null;

            foreach (var attribute in tag.Attributes) {
                if (attribute is not XmlTextAttributeSyntax textAttribute)
                    continue;

                if (textAttribute.Name.LocalName.Text != attributeName)
                    continue;

                value = textAttribute.TextTokens.ToString();
                return true;
            }

            return false;
        }

        private static DocumentationCommentTriviaSyntax? GetDocumentation(TypeDeclarationSyntax declaration) {
            foreach (var trivia in declaration.GetLeadingTrivia())
                if (trivia.GetStructure() is DocumentationCommentTriviaSyntax documentation)
                    return documentation;

            return null;
        }
    }
}