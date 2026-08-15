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

namespace RinkuLib.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddBasedOnCodeFixProvider)), Shared]
public sealed class AddBasedOnCodeFixProvider : CodeFixProvider {
    public override ImmutableArray<string> FixableDiagnosticIds => [AddBasedOnAnalyzer.DiagnosticId];
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context) {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics.First();
        var declaration = DocumentationTags.FindContainingType(root, diagnostic.Location.SourceSpan);
        if (declaration is null)
            return;

        var schemas = await FindSchemasAsync(context.Document.Project, context.CancellationToken).ConfigureAwait(false);
        if (schemas.Length == 0)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Add schema link",
                [
                    CreateLinkChoice(context.Document, declaration.SpanStart, DocumentationTags.BasedOn, "Track schema changes", schemas),
                    CreateLinkChoice(context.Document, declaration.SpanStart, DocumentationTags.MatchConstructor, "Require a matching constructor", schemas)
                ],
                isInlinable: false),
            diagnostic);
    }

    private static CodeAction CreateLinkChoice(
        Document document,
        int declarationStart,
        string tagName,
        string title,
        ImmutableArray<ISymbol> schemas) {
        var actions = schemas.Select(symbol => (CodeAction)CodeAction.Create(
            symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            cancellationToken => ApplyAsync(document, declarationStart, symbol, tagName, cancellationToken),
            $"Add-{tagName}-{symbol.ToDisplayString()}"));
        return CodeAction.Create(title, actions.ToImmutableArray(), isInlinable: false);
    }

    private static async Task<ImmutableArray<ISymbol>> FindSchemasAsync(Project project, CancellationToken cancellationToken) {
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (compilation is null)
            return [];

        var symbols = ImmutableArray.CreateBuilder<ISymbol>();
        foreach (var tree in compilation.SyntaxTrees) {
            cancellationToken.ThrowIfCancellationRequested();
            var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in root.DescendantNodes().OfType<MemberDeclarationSyntax>()) {
                if (!DocumentationTags.HasTag(declaration, DocumentationTags.Schema))
                    continue;

                ISymbol? symbol = declaration switch {
                    TypeDeclarationSyntax type => model.GetDeclaredSymbol(type, cancellationToken),
                    MethodDeclarationSyntax method => model.GetDeclaredSymbol(method, cancellationToken),
                    _ => null
                };
                if (symbol is not null)
                    symbols.Add(symbol);
            }
        }

        return symbols
            .OrderBy(symbol => symbol.ToDisplayString(), StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static async Task<Document> ApplyAsync(
        Document document,
        int declarationStart,
        ISymbol schema,
        string tagName,
        CancellationToken cancellationToken) {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
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

        var timestamp = tagName == DocumentationTags.BasedOn
            ? DocumentationTags.GetLatestSchemaTimestamp(schema, cancellationToken)
            : null;
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var newLine = text.Lines.Count > 1
            ? text.ToString(Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(text.Lines[0].End, text.Lines[1].Start))
            : Environment.NewLine;
        var updated = AddTag(declaration, schema, tagName, timestamp, newLine);
        return document.WithSyntaxRoot(root.ReplaceNode(declaration, updated));
    }

    private static TypeDeclarationSyntax AddTag(
        TypeDeclarationSyntax declaration,
        ISymbol symbol,
        string tagName,
        DateTimeOffset? timestamp,
        string newLine) {
        var leadingTrivia = declaration.GetLeadingTrivia();
        var lastTrivia = leadingTrivia.LastOrDefault();
        var indent = lastTrivia.IsKind(SyntaxKind.WhitespaceTrivia) ? lastTrivia.ToString() : string.Empty;
        var lastUpdated = timestamp.HasValue
            ? $" LastUpdated=\"{timestamp.Value.UtcDateTime:yyyy-MM-ddTHH:mmZ}\""
            : string.Empty;
        var comment = SyntaxFactory.ParseLeadingTrivia(
            $"{indent}/// <{tagName} cref=\"{GetCref(symbol)}\"{lastUpdated} />{newLine}");
        var index = lastTrivia.IsKind(SyntaxKind.WhitespaceTrivia)
            ? leadingTrivia.Count - 1
            : leadingTrivia.Count;
        return declaration.WithLeadingTrivia(leadingTrivia.InsertRange(index, comment));
    }

    private static string GetCref(ISymbol symbol)
        => symbol.ToDisplayString().Replace('<', '{').Replace('>', '}');
}
