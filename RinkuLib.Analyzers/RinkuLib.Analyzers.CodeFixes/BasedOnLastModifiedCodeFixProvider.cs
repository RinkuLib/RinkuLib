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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BasedOnLastModifiedCodeFixProvider)), Shared]
public sealed class BasedOnLastModifiedCodeFixProvider : CodeFixProvider {
    public override ImmutableArray<string> FixableDiagnosticIds => [BasedOnAnalyzer.DiagnosticId];
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;
    public override async Task RegisterCodeFixesAsync(CodeFixContext context) {
        var document = context.Document;
        var cancellationToken = context.CancellationToken;

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics.First();

        var declaration = root
            .FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();

        if (declaration is null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Update BasedOn timestamp to now",
                createChangedDocument: c => ApplyTimestampAsync(document, declaration, c),
                equivalenceKey: "SyncBasedOnTimestamp"),
            diagnostic);
    }
    public static async Task<Document> ApplyTimestampAsync(Document document, TypeDeclarationSyntax declaration, CancellationToken ct) {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
            return document;
        var updatedDeclaration = WithUpdatedTimestamp(declaration);
        return document.WithSyntaxRoot(root.ReplaceNode(declaration, updatedDeclaration));
    }
    public static TypeDeclarationSyntax WithUpdatedTimestamp(TypeDeclarationSyntax declaration) {
        var tag = BasedOnHelper.GetTags(declaration, "BasedOn").FirstOrDefault();
        if (tag is null)
            return declaration;

        var updatedTag = UpdateTag(tag);
        return declaration.ReplaceNode(tag, updatedTag);
    }
    public static XmlNodeSyntax UpdateTag(XmlNodeSyntax tag) {
        var now = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mmZ");
        var newAttribute = CreateLastUpdatedAttribute(now);

        return tag switch {
            XmlEmptyElementSyntax empty => UpdateEmptyElement(empty, newAttribute),
            XmlElementSyntax element => UpdateElement(element, newAttribute),
            _ => tag
        };
    }
    private static XmlEmptyElementSyntax UpdateEmptyElement(XmlEmptyElementSyntax tag, XmlTextAttributeSyntax newAttribute) {
        var existing = tag.Attributes
            .OfType<XmlTextAttributeSyntax>()
            .FirstOrDefault(a => a.Name.LocalName.Text == "LastUpdated");

        return existing is null
            ? tag.AddAttributes(newAttribute)
            : tag.ReplaceNode(existing, newAttribute);
    }
    private static XmlElementSyntax UpdateElement(XmlElementSyntax tag, XmlTextAttributeSyntax newAttribute) {
        var startTag = tag.StartTag;
        var existing = startTag.Attributes
            .OfType<XmlTextAttributeSyntax>()
            .FirstOrDefault(a => a.Name.LocalName.Text == "LastUpdated");
        var newStartTag = existing is null
            ? startTag.AddAttributes(newAttribute)
            : startTag.ReplaceNode(existing, newAttribute);
        return tag.WithStartTag(newStartTag);
    }

    public static XmlTextAttributeSyntax CreateLastUpdatedAttribute(string value) {
        return SyntaxFactory.XmlTextAttribute(
                SyntaxFactory.XmlName("LastUpdated"),
                SyntaxFactory.Token(SyntaxKind.EqualsToken),
                SyntaxFactory.Token(SyntaxKind.DoubleQuoteToken),
                SyntaxFactory.TokenList(
                    SyntaxFactory.XmlTextLiteral(
                        SyntaxFactory.TriviaList(),
                        value,
                        value,
                        SyntaxFactory.TriviaList())),
                SyntaxFactory.Token(SyntaxKind.DoubleQuoteToken))
            .WithLeadingTrivia(SyntaxFactory.Whitespace(" "));
    }
}