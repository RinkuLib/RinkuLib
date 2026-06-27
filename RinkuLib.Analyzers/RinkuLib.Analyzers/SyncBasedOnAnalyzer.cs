using System;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RinkuLib.Analyzers {
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SyncBasedOnAnalyzer : DiagnosticAnalyzer {
        public const string DiagnosticId = "RK0100";

        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.SyncBasedOnTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.SyncBasedOnMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.SyncBasedOnDescription), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Rinku";

        private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

        public override void Initialize(AnalysisContext context) {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        }
        private static void AnalyzeNamedType(SymbolAnalysisContext context) {
            if (context.Symbol is not INamedTypeSymbol type)
                return;

            var cancellationToken = context.CancellationToken;
            var compilation = context.Compilation;
            foreach (var basedOnTag in BasedOnHelper.GetTags(type, "BasedOn", cancellationToken)) {
                DateTimeOffset basedOnTimestamp = DateTimeOffset.MinValue;
                foreach (var item in BasedOnHelper.GetAttributes(basedOnTag!, "LastUpdated")) {
                    if (!DateTimeOffset.TryParse(item, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                        continue;
                    if (basedOnTimestamp == DateTimeOffset.MinValue || dt < basedOnTimestamp)
                        basedOnTimestamp = dt;
                }

                var basedOnSymbols = BasedOnHelper.GetBasedOnSymbols(type, compilation, cancellationToken);

                foreach (var basedOnSymbol in basedOnSymbols) {
                    foreach (var schemaTag in BasedOnHelper.GetTags(basedOnSymbol, "Schema", cancellationToken)) {
                        foreach (var item in BasedOnHelper.GetAttributes(schemaTag!, "LastUpdated")) {
                            if (!DateTimeOffset.TryParse(item, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                                continue;
                            if (dt > basedOnTimestamp && type.Locations.Length > 0) {
                                context.ReportDiagnostic(Diagnostic.Create(Rule, type.Locations[0], type.Name, basedOnSymbol.Name));
                                return;
                            }
                        }
                    }
                }
            }
        }
    }
}