using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RinkuLib.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MethodInvocationCompletionAnalyzer : DiagnosticAnalyzer {
    public const string DiagnosticId = "RK0002";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Method invocation generation available",
        "Generate invocation for '{0}'",
        "Rinku",
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context) {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(
            AnalyzeInvocationCandidate,
            SyntaxKind.IdentifierName,
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxKind.MemberBindingExpression);
    }

    private static void AnalyzeInvocationCandidate(SyntaxNodeAnalysisContext context) {
        if (context.Node is not ExpressionSyntax expression)
            return;

        if (!IsRelevantExpression(expression)
         || !IsInsideMethodLikeBody(expression)
         || IsAlreadyInvoked(expression))
            return;

        IMethodSymbol? method = ResolveMethodSymbol(context, expression);
        if (method is null)
            return;

        if (IsDelegateConversion(context, expression))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, expression.GetLocation(), method.Name));
    }

    private static bool IsRelevantExpression(ExpressionSyntax expression) {
        if (expression.Ancestors(ascendOutOfTrivia: true).Any(n => n is DocumentationCommentTriviaSyntax))
            return false;
        if (expression.Parent is MemberAccessExpressionSyntax parentAccess && parentAccess.Name == expression)
            return false;

        if (expression.Parent is MemberBindingExpressionSyntax parentBinding && parentBinding.Name == expression)
            return false;

        if (expression.Parent is MemberAccessExpressionSyntax leftAccess && leftAccess.Expression == expression)
            return false;

        return true;
    }

    private static bool IsInsideMethodLikeBody(ExpressionSyntax expression)
        => expression.Ancestors().Any(static n =>
            n is MethodDeclarationSyntax
            or ConstructorDeclarationSyntax
            or LocalFunctionStatementSyntax);

    private static IMethodSymbol? ResolveMethodSymbol(SyntaxNodeAnalysisContext context, ExpressionSyntax expression) {
        SymbolInfo symbolInfo = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken);

        IMethodSymbol? method = symbolInfo.Symbol as IMethodSymbol
            ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

        if (method is null)
            return null;

        return method.MethodKind is MethodKind.Ordinary or MethodKind.ReducedExtension
            ? method : null;
    }

    private static bool IsDelegateConversion(SyntaxNodeAnalysisContext context, ExpressionSyntax expression) {
        var typeInfo = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);

        if (typeInfo.ConvertedType?.TypeKind != TypeKind.Delegate)
            return false;
        if (IsAssignedToVar(expression))
            return false;

        return true;
    }

    private static bool IsAssignedToVar(ExpressionSyntax expression) {
        if (expression.Parent is EqualsValueClauseSyntax equalsClause &&
            equalsClause.Parent is VariableDeclaratorSyntax declarator &&
            declarator.Parent is VariableDeclarationSyntax varDecl) {
            return varDecl.Type.IsVar;
        }

        return false;
    }

    private static bool IsAlreadyInvoked(ExpressionSyntax expression) {
        for (SyntaxNode? current = expression; current?.Parent != null; current = current.Parent) {
            if (current.Parent is InvocationExpressionSyntax invocation &&
                invocation.Expression == current) {
                return true;
            }

            if (current is StatementSyntax)
                break;
        }

        return false;
    }
}