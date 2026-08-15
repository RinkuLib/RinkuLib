using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RinkuLib.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SyncBasedOnAnalyzer : DiagnosticAnalyzer {
    public const string DiagnosticId = "RK0100";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "BasedOn link is out of date",
        "'{0}' may no longer match '{1}'",
        "Rinku",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A referenced schema changed after the BasedOn link was acknowledged.");

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
            DocumentationTags.BasedOn,
            context.Compilation,
            context.CancellationToken)) {
            var schemaTimestamp = DocumentationTags.GetLatestSchemaTimestamp(reference.Symbol, context.CancellationToken);
            if (!schemaTimestamp.HasValue
                || reference.LastUpdated.HasValue && schemaTimestamp <= reference.LastUpdated)
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                reference.Tag.GetLocation(),
                type.Name,
                reference.Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }
}
