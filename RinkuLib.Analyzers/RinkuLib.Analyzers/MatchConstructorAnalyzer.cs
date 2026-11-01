using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RinkuLib.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MatchConstructorAnalyzer : DiagnosticAnalyzer {
    public const string DiagnosticId = "RK0101";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Constructor does not match",
        "'{0}' needs a constructor matching '{1}'",
        "Rinku",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The target type must keep a constructor with the same parameters as the referenced type or method.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context) {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context) {
        if (context.Symbol is not INamedTypeSymbol type)
            return;

        foreach (var reference in DocumentationTags.GetReferences(type, DocumentationTags.BasedOn, context.Compilation, context.CancellationToken)) {
            DateTimeOffset? schemaTimestamp = DocumentationTags.GetLatestSchemaTimestamp(reference.Symbols, context.CancellationToken);
            if (schemaTimestamp.HasValue && reference.LastUpdated.HasValue && reference.LastUpdated >= schemaTimestamp)
                continue;
            bool hasMatch = false;
            foreach (ISymbol symbol in reference.Symbols) {
                if (!ConstructorContract.HasMatch(type, symbol))
                    continue;
                hasMatch = true;
                break;
            }
            if (hasMatch)
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                reference.Location(context.CancellationToken),
                type.Name,
                reference.Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }

    }
}
