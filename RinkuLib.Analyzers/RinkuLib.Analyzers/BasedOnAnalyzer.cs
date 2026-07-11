using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RinkuLib.Analyzers {

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class BasedOnAnalyzer : DiagnosticAnalyzer {
        public const string DiagnosticId = "RK0000";

        private static readonly DiagnosticDescriptor Rule =
            new(DiagnosticId, "BasedOn code generation available", "Referencing symbol '{0}'", "Rinku", DiagnosticSeverity.Hidden, true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

        public override void Initialize(AnalysisContext context) {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
        }
        private static void AnalyzeType(SymbolAnalysisContext context) {
            if (context.Symbol is not INamedTypeSymbol type || type.Locations.Length == 0)
                return;
            foreach (var (symbol, _) in BasedOnHelper.GetBasedOnSymbols(type, context.Compilation, context.CancellationToken))
                context.ReportDiagnostic(Diagnostic.Create(Rule, type.Locations[0], symbol.Name));
        }
    }
}
