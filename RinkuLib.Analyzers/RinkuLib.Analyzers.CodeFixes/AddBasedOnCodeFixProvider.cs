using System;
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
                [CreateLinkChoice(context.Document, declaration.SpanStart, schemas)],
                isInlinable: false),
            diagnostic);
    }

    private static CodeAction CreateLinkChoice(Document document, int declarationStart, ImmutableArray<ISymbol> schemas) {
        var actions = schemas.Select(symbol => (CodeAction)CodeAction.Create(
            symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            cancellationToken => ApplyAsync(document, declarationStart, symbol, cancellationToken),
            $"Add-{DocumentationTags.BasedOn}-{symbol.ToDisplayString()}"));
        return CodeAction.Create("Track schema changes", [.. actions], isInlinable: false);
    }

    private static async Task<ImmutableArray<ISymbol>> FindSchemasAsync(Project project, CancellationToken cancellationToken) {
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (compilation is null)
            return [];

        var symbols = ImmutableArray.CreateBuilder<ISymbol>();
        foreach (SyntaxTree tree in compilation.SyntaxTrees) {
            cancellationToken.ThrowIfCancellationRequested();
            var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in root.DescendantNodes().OfType<MemberDeclarationSyntax>()) {
                if (!DocumentationTags.HasTag(declaration, DocumentationTags.Schema)
                    || model.GetDeclaredSymbol(declaration, cancellationToken) is not { } symbol)
                    continue;
                symbols.Add(symbol);
            }
        }

        return [.. symbols.OrderBy(symbol => symbol.ToDisplayString(), StringComparer.Ordinal)];
    }

    private static async Task<Document> ApplyAsync(Document document, int declarationStart, ISymbol schema, CancellationToken cancellationToken) {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var declaration = root.FindToken(declarationStart).Parent?.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (declaration is null)
            return document;

        var timestamp = DocumentationTags.GetLatestSchemaTimestamp(schema, cancellationToken);
        var updated = AddAttribute(declaration, schema, timestamp);
        return document.WithSyntaxRoot(root.ReplaceNode(declaration, updated));
    }

    private static TypeDeclarationSyntax AddAttribute(TypeDeclarationSyntax declaration, ISymbol symbol, DateTimeOffset? timestamp) {
        var arguments = CreateSelectorArguments(symbol);
        if (timestamp.HasValue) {
            var value = SyntaxFactory.AttributeArgument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(timestamp.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'"))));
            arguments = arguments.Length == 1 ? arguments.Add(value) : arguments.SetItem(1, value);
        }
        var attribute = SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(DocumentationTags.BasedOn));
        if (arguments.Length > 0)
            attribute = attribute.WithArgumentList(SyntaxFactory.AttributeArgumentList(SyntaxFactory.SeparatedList(arguments)));
        return declaration.AddAttributeLists(SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute)));
    }

    private static ImmutableArray<AttributeArgumentSyntax> CreateSelectorArguments(ISymbol symbol) {
        if (symbol is IMethodSymbol method) {
            var arguments = ImmutableArray.CreateBuilder<AttributeArgumentSyntax>();
            arguments.Add(SyntaxFactory.AttributeArgument(TypeOf(method.ContainingType)));
            if (method.MethodKind != MethodKind.Constructor) {
                arguments.Add(SyntaxFactory.AttributeArgument(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)));
                arguments.Add(SyntaxFactory.AttributeArgument(SyntaxFactory.ParseExpression($"nameof({method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{method.Name})")));
            }
            return arguments.ToImmutable();
        }

        return [SyntaxFactory.AttributeArgument(TypeOf((ITypeSymbol)symbol))];
    }

    private static TypeOfExpressionSyntax TypeOf(ITypeSymbol type)
        => SyntaxFactory.TypeOfExpression(SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
}
