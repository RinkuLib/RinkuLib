using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace RinkuLib.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MethodInvocationCodeFixProvider)), Shared]
public sealed class MethodInvocationCodeFixProvider : CodeFixProvider {
    public override ImmutableArray<string> FixableDiagnosticIds
        => [MethodInvocationCompletionAnalyzer.DiagnosticId];

    public override FixAllProvider GetFixAllProvider()
        => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context) {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
            return;

        var diagnostic = context.Diagnostics.First();
        var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        if (node is not ExpressionSyntax expression)
            return;

        var info = model.GetSymbolInfo(expression, context.CancellationToken);
        var methods = info.Symbol is IMethodSymbol single
            ? [single]
            : info.CandidateSymbols.OfType<IMethodSymbol>().ToImmutableArray();

        foreach (var method in methods) {
            var title = $"Invoke {method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}";

            context.RegisterCodeFix(
                CodeAction.Create(
                    title,
                    ct => ApplyFixForMethodAsync(context.Document, expression, method, ct),
                    equivalenceKey: title),
                diagnostic);
        }
    }
    private static async Task<Document> ApplyFixForMethodAsync(Document document, ExpressionSyntax expression, IMethodSymbol method, CancellationToken ct) {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);

        if (root is null || model is null)
            return document;

        ExpressionSyntax targetExpression = expression;
        if (expression.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == expression)
            targetExpression = memberAccess;
        else if (expression.Parent is MemberBindingExpressionSyntax memberBinding && memberBinding.Name == expression)
            targetExpression = memberBinding;

        var caller = FindCaller(targetExpression);
        if (caller is null)
            return document;

        var available = CollectAvailableSymbols(model, targetExpression);
        bool callerIsStatic = IsStaticContext(caller, model, ct);
        var callerType = GetCallerType(caller, model, ct);

        var arguments = new List<ArgumentSyntax>();
        var missing = new List<IParameterSymbol>();

        foreach (var parameter in method.Parameters) {
            var match =
                TryDirectMatch(parameter, available)
                ?? TryCallerMember(parameter, callerType, callerIsStatic)
                ?? TryLocalObjectMember(parameter, available);

            ArgumentSyntax argument;
            if (match is not null)
                argument = SyntaxFactory.Argument(match);
            else {
                argument = SyntaxFactory.Argument(SyntaxFactory.IdentifierName(parameter.Name));
                missing.Add(parameter);
            }

            if (parameter.RefKind == RefKind.Out)
                argument = argument.WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.OutKeyword));
            else if (parameter.RefKind == RefKind.Ref)
                argument = argument.WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword));
            else if (parameter.RefKind == RefKind.In)
                argument = argument.WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.InKeyword));

            arguments.Add(argument);
        }

        var invocation = SyntaxFactory.InvocationExpression(
            targetExpression.WithoutTrivia(),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments))
        ).WithTriviaFrom(targetExpression);

        var updatedCaller = caller.ReplaceNode(targetExpression, invocation);
        updatedCaller = AddMissingParameters(updatedCaller, missing);

        var newRoot = root.ReplaceNode(caller, updatedCaller.WithAdditionalAnnotations(Formatter.Annotation));
        return document.WithSyntaxRoot(newRoot);
    }
    private static IMethodSymbol? ResolveMethod(SemanticModel model, ExpressionSyntax expression, CancellationToken ct) {
        var info = model.GetSymbolInfo(expression, ct);

        return info.Symbol as IMethodSymbol
            ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
    }

    private static SyntaxNode? FindCaller(SyntaxNode node)
        => node.Ancestors().FirstOrDefault(n =>
            n is MethodDeclarationSyntax
            or ConstructorDeclarationSyntax
            or LocalFunctionStatementSyntax);

    private static bool IsStaticContext(SyntaxNode caller, SemanticModel model, CancellationToken ct) {
        var symbol = model.GetDeclaredSymbol(caller, ct);
        return symbol is IMethodSymbol m && m.IsStatic;
    }

    private static INamedTypeSymbol? GetCallerType(SyntaxNode caller, SemanticModel model, CancellationToken ct) {
        var symbol = model.GetDeclaredSymbol(caller, ct);
        return symbol?.ContainingType;
    }

    private static List<ISymbol> CollectAvailableSymbols(SemanticModel model, ExpressionSyntax expression) {
        return model.LookupSymbols(expression.SpanStart)
            .Where(s =>
                s is ILocalSymbol
                or IParameterSymbol
                or IFieldSymbol
                or IPropertySymbol)
            .ToList();
    }

    private static ExpressionSyntax? TryDirectMatch(IParameterSymbol parameter, List<ISymbol> available) {
        foreach (var s in available) {
            if (NameMatches(s, parameter) && TypeMatches(s, parameter)) {
                return SyntaxFactory.IdentifierName(s.Name);
            }
        }
        return null;
    }

    private static ExpressionSyntax? TryCallerMember(IParameterSymbol parameter, INamedTypeSymbol? callerType, bool isStatic) {
        if (callerType is null)
            return null;

        foreach (var member in callerType.GetMembers()) {
            if (member is not (IFieldSymbol or IPropertySymbol))
                continue;

            if (isStatic && !member.IsStatic)
                continue;

            if (!NameMatches(member, parameter) || !TypeMatches(member, parameter))
                continue;

            return member.IsStatic
                ? SyntaxFactory.IdentifierName(member.Name)
                : SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ThisExpression(),
                    SyntaxFactory.IdentifierName(member.Name));
        }
        return null;
    }

    private static ExpressionSyntax? TryLocalObjectMember(IParameterSymbol parameter, List<ISymbol> available) {
        foreach (var symbol in available) {
            var type = GetSymbolType(symbol);
            if (type is null)
                continue;

            foreach (var member in type.GetMembers().OfType<ISymbol>()) {
                if (member is not (IFieldSymbol or IPropertySymbol))
                    continue;

                if (!NameMatches(member, parameter) || !TypeMatches(member, parameter))
                    continue;

                return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(symbol.Name),
                    SyntaxFactory.IdentifierName(member.Name));
            }
        }
        return null;
    }

    private static SyntaxNode AddMissingParameters(SyntaxNode caller, List<IParameterSymbol> missing) {
        if (missing.Count == 0)
            return caller;

        var paramList = caller switch {
            MethodDeclarationSyntax m => m.ParameterList,
            ConstructorDeclarationSyntax c => c.ParameterList,
            LocalFunctionStatementSyntax l => l.ParameterList,
            _ => null
        };

        if (paramList is null)
            return caller;

        var additions = new List<ParameterSyntax>();

        foreach (var p in missing) {
            bool exists = paramList.Parameters.Any(existing =>
                string.Equals(existing.Identifier.ValueText, p.Name, StringComparison.Ordinal));

            if (exists)
                continue;

            var newParam = SyntaxFactory.Parameter(SyntaxFactory.Identifier(p.Name))
                .WithType(SyntaxFactory.ParseTypeName(p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));

            if (p.RefKind == RefKind.Out)
                newParam = newParam.AddModifiers(SyntaxFactory.Token(SyntaxKind.OutKeyword));
            else if (p.RefKind == RefKind.Ref)
                newParam = newParam.AddModifiers(SyntaxFactory.Token(SyntaxKind.RefKeyword));
            else if (p.RefKind == RefKind.In)
                newParam = newParam.AddModifiers(SyntaxFactory.Token(SyntaxKind.InKeyword));

            if (p.IsParams)
                newParam = newParam.AddModifiers(SyntaxFactory.Token(SyntaxKind.ParamsKeyword));

            additions.Add(newParam);
        }

        if (additions.Count == 0)
            return caller;

        var updated = paramList.AddParameters(additions.ToArray());

        return caller switch {
            MethodDeclarationSyntax m => m.WithParameterList(updated),
            ConstructorDeclarationSyntax c => c.WithParameterList(updated),
            LocalFunctionStatementSyntax l => l.WithParameterList(updated),
            _ => caller
        };
    }

    private static bool NameMatches(ISymbol s, IParameterSymbol p)
        => string.Equals(s.Name, p.Name, StringComparison.OrdinalIgnoreCase);

    private static bool TypeMatches(ISymbol s, IParameterSymbol p)
        => SymbolEqualityComparer.Default.Equals(GetSymbolType(s), p.Type);

    private static ITypeSymbol? GetSymbolType(ISymbol s)
        => s switch {
            ILocalSymbol l => l.Type,
            IParameterSymbol p => p.Type,
            IFieldSymbol f => f.Type,
            IPropertySymbol p => p.Type,
            _ => null
        };
}