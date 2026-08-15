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
        context.RegisterSyntaxNodeAction(AnalyzeName, SyntaxKind.IdentifierName, SyntaxKind.GenericName);
    }

    private static void AnalyzeName(SyntaxNodeAnalysisContext context) {
        if (context.Node is not SimpleNameSyntax name
            || !TryGetMethodExpression(name, out var expression)
            || !IsInsideMethodBody(expression)
            || IsAlreadyInvoked(expression)
            || IsInsideNameof(expression)
            || IsDelegateConversion(context, expression))
            return;

        var method = ResolveMethod(context, expression);
        if (method is not null)
            context.ReportDiagnostic(Diagnostic.Create(Rule, expression.GetLocation(), method.Name));
    }

    private static bool TryGetMethodExpression(SimpleNameSyntax name, out ExpressionSyntax expression) {
        expression = name;
        if (name.Ancestors(ascendOutOfTrivia: true).Any(node => node is DocumentationCommentTriviaSyntax))
            return false;

        if (name.Parent is MemberAccessExpressionSyntax access) {
            if (access.Expression == name)
                return false;
            if (access.Name == name)
                expression = access;
        }
        else if (name.Parent is MemberBindingExpressionSyntax binding && binding.Name == name)
            expression = binding;

        return true;
    }

    private static bool IsInsideMethodBody(ExpressionSyntax expression)
        => expression.Ancestors().Any(static node =>
            node is MethodDeclarationSyntax
            or ConstructorDeclarationSyntax
            or LocalFunctionStatementSyntax);

    private static IMethodSymbol? ResolveMethod(SyntaxNodeAnalysisContext context, ExpressionSyntax expression) {
        var info = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken);
        var method = info.Symbol as IMethodSymbol
            ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        return method?.MethodKind is MethodKind.Ordinary or MethodKind.ReducedExtension
            ? method
            : null;
    }

    private static bool IsDelegateConversion(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
        => context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).ConvertedType?.TypeKind == TypeKind.Delegate;

    private static bool IsInsideNameof(ExpressionSyntax expression) {
        for (SyntaxNode? current = expression.Parent; current is not null; current = current.Parent) {
            if (current is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } })
                return true;
            if (current is StatementSyntax)
                return false;
        }
        return false;
    }

    private static bool IsAlreadyInvoked(ExpressionSyntax expression) {
        for (SyntaxNode? current = expression; current?.Parent is not null; current = current.Parent) {
            if (current.Parent is InvocationExpressionSyntax invocation && invocation.Expression == current)
                return true;
            if (current is StatementSyntax)
                return false;
        }
        return false;
    }
}
