using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RinkuLib.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AddBasedOnAnalyzer : DiagnosticAnalyzer {
    public const string DiagnosticId = "RK0001";

    private static readonly DiagnosticDescriptor Rule = 
        new(DiagnosticId, "Missing BasedOn documentation", "Type '{0}' is missing the <BasedOn> documentation tag", "Rinku", DiagnosticSeverity.Hidden, true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context) {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeNode,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.RecordDeclaration,
            SyntaxKind.StructDeclaration);
    }

    private static void AnalyzeNode(SyntaxNodeAnalysisContext context) {
        if (context.Node is not TypeDeclarationSyntax declaration)
            return;
        if (!BasedOnHelper.GetTags(declaration, "BasedOn").Any())
            context.ReportDiagnostic(Diagnostic.Create(Rule,
                declaration.Identifier.GetLocation(),
                declaration.Identifier.ValueText));
    }
}