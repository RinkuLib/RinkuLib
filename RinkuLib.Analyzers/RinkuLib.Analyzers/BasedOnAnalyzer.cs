using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RinkuLib.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BasedOnAnalyzer : DiagnosticAnalyzer {
    public const string DiagnosticId = "RK0000";
    internal const string SchemaTimestampProperty = "SchemaTimestamp";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "BasedOn actions available",
        "Actions are available for '{0}'",
        "Rinku",
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: true);

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
            var properties = schemaTimestamp.HasValue
                ? ImmutableDictionary<string, string?>.Empty.Add(
                    SchemaTimestampProperty,
                    schemaTimestamp.Value.ToString("O", CultureInfo.InvariantCulture))
                : null;
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                reference.Tag.GetLocation(),
                properties,
                reference.Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }
}
