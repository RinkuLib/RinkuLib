using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RinkuLib.Analyzers {

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SyncBasedOnAnalyzer : DiagnosticAnalyzer {
        public const string DiagnosticId = "RK0001";

        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.SyncBasedOnTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.SyncBasedOnMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.SyncBasedOnDescription), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Rinku";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context) {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        }

        private static void AnalyzeNamedType(SymbolAnalysisContext context) {
            var type = (INamedTypeSymbol)context.Symbol;

            if (TryGetTag(type, "BasedOn", context.Compilation, context.CancellationToken, out var itemTimestamp, out var cref)
                && TryGetTag(cref, "Schema", context.Compilation, context.CancellationToken, out var schemaTimestamp, out _)
                && schemaTimestamp > itemTimestamp) {
                if (type.Locations.Length > 0) {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, type.Locations[0], type.Name, cref.Name));
                }
            }
        }

        private static bool TryGetTag(INamedTypeSymbol type, string tagName, Compilation compilation, CancellationToken cancellationToken, out DateTimeOffset timestamp, out INamedTypeSymbol crefType) {
            crefType = default;
            timestamp = default;

            foreach (var syntaxReference in type.DeclaringSyntaxReferences) {
                if (!(syntaxReference.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration))
                    continue;

                var documentation = GetDocumentation(declaration);

                if (documentation == null)
                    continue;

                XmlEmptyElementSyntax element = null;

                foreach (var node in documentation.Content) {
                    if (!(node is XmlEmptyElementSyntax xmlElement)
                     || xmlElement.Name.LocalName.Text != tagName)
                        continue;

                    element = xmlElement;
                    break;
                }

                if (element == null)
                    continue;

                string timestampText = null;

                foreach (var attribute in element.Attributes) {
                    if (attribute is XmlTextAttributeSyntax textAttribute) {
                        if (textAttribute.Name.LocalName.Text == "LastUpdated")
                            timestampText = textAttribute.TextTokens.ToString();
                        continue;
                    }

                    if (attribute is XmlCrefAttributeSyntax crefAttribute) {
                        var semanticModel = compilation.GetSemanticModel(declaration.SyntaxTree);
                        var symbolInfo = semanticModel.GetSymbolInfo(crefAttribute.Cref, cancellationToken);

                        var symbol = symbolInfo.Symbol;

                        if (symbol == null && symbolInfo.CandidateSymbols.Length == 1)
                            symbol = symbolInfo.CandidateSymbols[0];

                        crefType = symbol as INamedTypeSymbol;
                    }
                }

                return !string.IsNullOrWhiteSpace(timestampText) 
                    && DateTimeOffset.TryParse(timestampText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestamp)
                    && (tagName != "BasedOn" || crefType != null);
            }

            return false;
        }

        private static DocumentationCommentTriviaSyntax GetDocumentation(TypeDeclarationSyntax declaration) {
            foreach (var trivia in declaration.GetLeadingTrivia())
                if (trivia.GetStructure() is DocumentationCommentTriviaSyntax documentation)
                    return documentation;

            return null;
        }
    }
}