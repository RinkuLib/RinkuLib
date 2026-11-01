using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RinkuLib.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class QueryCommandsAnalyzer : DiagnosticAnalyzer {
    public const string DiagnosticId = "RK0003";
    private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, "Include SQL sources", "SQL inclusion actions are available for '{0}'", "Rinku", DiagnosticSeverity.Hidden, true);
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];
    public override void Initialize(AnalysisContext context) {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(static analysis => {
            if (analysis.Symbol is not INamedTypeSymbol type)
                return;
            foreach (var attribute in type.GetAttributes())
                if (attribute.AttributeClass?.ToDisplayString() == QueryCommandsGenerator.AttributeName && attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string path) {
                    var location = attribute.ApplicationSyntaxReference?.GetSyntax(analysis.CancellationToken).GetLocation();
                    analysis.ReportDiagnostic(Diagnostic.Create(Rule, location, ImmutableDictionary<string, string?>.Empty.Add("Path", path), path));
                }
        }, SymbolKind.NamedType);
    }
}
