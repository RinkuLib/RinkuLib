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

namespace RinkuLib.Analyzers {
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BasedOnLastModifiedCodeFixProvider)), Shared]
    public sealed class BasedOnLastModifiedCodeFixProvider : CodeFixProvider {
        public override ImmutableArray<string> FixableDiagnosticIds => [
            BasedOnAnalyzer.DiagnosticId, SyncBasedOnAnalyzer.DiagnosticId
];

        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context) {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;
            var declaration = root?.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();

            if (declaration == null)
                return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Update BasedOn timestamp to now",
                    createChangedDocument: c => UpdateTimestampAsync(context.Document, declaration, c),
                    equivalenceKey: "SyncBasedOnTimestamp"),
                diagnostic);
        }

        private async Task<Document> UpdateTimestampAsync(Document document, TypeDeclarationSyntax declaration, CancellationToken cancellationToken) {
            if (!BasedOnHelper.TryGetTag(declaration, "BasedOn", out var existingTag))
                return document;

            var nowString = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mmZ");

            var newAttribute = SyntaxFactory.XmlTextAttribute(
                SyntaxFactory.XmlName(SyntaxFactory.Identifier("LastUpdated")),
                SyntaxFactory.Token(SyntaxKind.EqualsToken),
                SyntaxFactory.Token(SyntaxKind.DoubleQuoteToken),
                SyntaxFactory.TokenList(SyntaxFactory.XmlTextLiteral(
                    SyntaxFactory.TriviaList(),
                    nowString,
                    nowString,
                    SyntaxFactory.TriviaList())),
                SyntaxFactory.Token(SyntaxKind.DoubleQuoteToken));

            newAttribute = newAttribute.WithLeadingTrivia(SyntaxFactory.Whitespace(" "));

            var existingAttribute = existingTag!.Attributes.OfType<XmlTextAttributeSyntax>()
                .FirstOrDefault(a => a.Name.LocalName.Text == "LastUpdated");

            XmlEmptyElementSyntax newTag;

            if (existingAttribute != null) 
                newTag = existingTag.ReplaceNode(existingAttribute, newAttribute);
            else 
                newTag = existingTag.AddAttributes(newAttribute);

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var newRoot = root?.ReplaceNode(existingTag, newTag);

            return newRoot is null ? document : document.WithSyntaxRoot(newRoot);
        }
    }
}