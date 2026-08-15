using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RinkuLib.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AddBasedOnAnalyzer : DiagnosticAnalyzer {
    public const string DiagnosticId = "RK0001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Schema link available",
        "Link '{0}' to a generated schema",
        "Rinku",
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context) {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(startContext => {
            if (!DocumentationTags.HasSchema(startContext.Compilation, startContext.CancellationToken))
                return;
            startContext.RegisterSyntaxNodeAction(
                AnalyzeType,
                SyntaxKind.ClassDeclaration,
                SyntaxKind.RecordDeclaration,
                SyntaxKind.RecordStructDeclaration,
                SyntaxKind.StructDeclaration);
        });
    }

    private static void AnalyzeType(SyntaxNodeAnalysisContext context) {
        if (context.Node is not TypeDeclarationSyntax declaration
            || DocumentationTags.HasTag(declaration, DocumentationTags.Schema)
            || DocumentationTags.HasTag(declaration, DocumentationTags.BasedOn)
            || DocumentationTags.HasTag(declaration, DocumentationTags.MatchConstructor))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            declaration.Identifier.GetLocation(),
            declaration.Identifier.ValueText));
    }
}
