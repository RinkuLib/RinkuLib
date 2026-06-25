using System;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RinkuLib.Analyzers {
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SyncBasedOnAnalyzer : DiagnosticAnalyzer {
        public const string DiagnosticId = "RK0001";

        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.SyncBasedOnTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.SyncBasedOnMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.SyncBasedOnDescription), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Rinku";

        private static readonly DiagnosticDescriptor Rule = new(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

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

            if (!BasedOnHelper.TryGetTag(type, "BasedOn", cancellationToken, out var basedOnTag) ||
                !BasedOnHelper.TryGetBasedOnType(type, compilation, cancellationToken, out var basedOnType))
                return;

            if (!BasedOnHelper.TryGetTag(basedOnType!, "Schema", cancellationToken, out var schemaTag))
                return;

            if (!BasedOnHelper.TryGetAttribute(schemaTag!, "LastUpdated", out var schemaTimestampText) ||
                !DateTimeOffset.TryParse(schemaTimestampText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var schemaTimestamp))
                return;

            DateTimeOffset basedOnTimestamp = DateTimeOffset.MinValue;
            if (BasedOnHelper.TryGetAttribute(basedOnTag!, "LastUpdated", out var basedOnTimestampText))
                DateTimeOffset.TryParse(basedOnTimestampText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out basedOnTimestamp);

            if (schemaTimestamp > basedOnTimestamp && type.Locations.Length > 0)
                context.ReportDiagnostic(Diagnostic.Create(Rule, type.Locations[0], type.Name, basedOnType!.Name));
        }
    }
}