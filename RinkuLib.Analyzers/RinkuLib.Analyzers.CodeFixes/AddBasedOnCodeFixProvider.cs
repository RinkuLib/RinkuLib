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

namespace RinkuLib.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddBasedOnCodeFixProvider)), Shared]
public sealed class AddBasedOnCodeFixProvider : CodeFixProvider {
    public override ImmutableArray<string> FixableDiagnosticIds => [AddBasedOnAnalyzer.DiagnosticId];
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;
    public override async Task RegisterCodeFixesAsync(CodeFixContext context) {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root is null)
            return;

        var declaration = FindTargetDeclaration(root, context.Diagnostics.First());
        if (declaration is null)
            return;

        var candidates = await FindSchemaSymbolsAsync(context.Document.Project, context.CancellationToken);

        if (candidates.Count == 0)
            return;

        var nestedActions = candidates
            .Select(symbol => CodeAction.Create(
                title: GetDisplayName(symbol),
                createChangedDocument: ct => ApplyFixAsync(context.Document, declaration.SpanStart, symbol, ct),
                equivalenceKey: $"basedon-{symbol.ToDisplayString()}"))
            .Cast<CodeAction>()
            .ToImmutableArray();

        context.RegisterCodeFix(
            CodeAction.Create("Add BasedOn Link...", nestedActions, false),
            context.Diagnostics.First());
    }
    private static async Task<List<ISymbol>> FindSchemaSymbolsAsync(Project project, CancellationToken ct) {
        var compilation = await project.GetCompilationAsync(ct);
        if (compilation is null)
            return [];

        var result = new List<ISymbol>();

        foreach (var tree in compilation.SyntaxTrees) {
            ct.ThrowIfCancellationRequested();

            var root = await tree.GetRootAsync(ct);
            var model = compilation.GetSemanticModel(tree);

            foreach (var member in root.DescendantNodes().OfType<MemberDeclarationSyntax>()) {
                if (!BasedOnHelper.HasTag(member, "Schema"))
                    continue;

                ISymbol? symbol = member switch {
                    TypeDeclarationSyntax type => model.GetDeclaredSymbol(type, ct),
                    MethodDeclarationSyntax method => model.GetDeclaredSymbol(method, ct),
                    _ => null
                };

                if (symbol != null)
                    result.Add(symbol);
            }
        }

        return result;
    }
    private static async Task<Document> ApplyFixAsync(Document document, int declarationStart, ISymbol symbol, CancellationToken ct) {
        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null)
            return document;

        var declaration = root
            .FindToken(declarationStart)
            .Parent?
            .AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();

        if (declaration is null)
            return document;

        var updated = InsertBasedOnTag(declaration, symbol);
        var newRoot = root.ReplaceNode(declaration, updated);

        return document.WithSyntaxRoot(newRoot);
    }
    private static TypeDeclarationSyntax InsertBasedOnTag(TypeDeclarationSyntax declaration, ISymbol symbol) {
        var leadingTrivia = declaration.GetLeadingTrivia();
        var lastTrivia = leadingTrivia.LastOrDefault();
        var indent = lastTrivia.IsKind(SyntaxKind.WhitespaceTrivia) ? lastTrivia.ToString() : "";
        var newCommentText = $"{indent}/// <BasedOn cref=\"{GetCref(symbol)}\" />\r\n";
        var newTrivia = SyntaxFactory.ParseLeadingTrivia(newCommentText);
        int insertIndex = leadingTrivia.Count;
        if (lastTrivia.IsKind(SyntaxKind.WhitespaceTrivia))
            insertIndex--;
        var updatedTrivia = leadingTrivia.InsertRange(insertIndex, newTrivia);
        return declaration.WithLeadingTrivia(updatedTrivia);
    }
    private static TypeDeclarationSyntax? FindTargetDeclaration(SyntaxNode root, Diagnostic diagnostic) {
        return root
            .FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();
    }
    private static string GetDisplayName(ISymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    private static string GetCref(ISymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
}