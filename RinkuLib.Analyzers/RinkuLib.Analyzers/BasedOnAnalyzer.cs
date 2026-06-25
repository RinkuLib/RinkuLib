using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RinkuLib.Analyzers {

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class BasedOnAnalyzer : DiagnosticAnalyzer {
        public const string DiagnosticId = "RK0000";

        private static readonly DiagnosticDescriptor Rule =
            new(DiagnosticId, "BasedOn code generation available", "Generate members from '{0}'", "Rinku", DiagnosticSeverity.Hidden, true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context) {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
        }

        private static void AnalyzeType(SymbolAnalysisContext context) {
            if (context.Symbol is not INamedTypeSymbol type || !BasedOnHelper.TryGetBasedOnType(type, context.Compilation, context.CancellationToken, out var basedOnType))
                return;
            context.ReportDiagnostic(Diagnostic.Create(Rule, type.Locations[0], basedOnType!.Name)); 
        }
    }
}