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

        foreach (var reference in DocumentationTags.GetReferences(
            type,
            DocumentationTags.MatchConstructor,
            context.Compilation,
            context.CancellationToken)) {
            if (ConstructorContract.HasMatch(type, reference.Symbol))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                reference.Tag.GetLocation(),
                type.Name,
                reference.Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }
}
