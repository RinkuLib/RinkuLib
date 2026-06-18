using System;
using System.Collections.Immutable;
using System.Composition;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;

namespace RinkuLib.Analyzers {

    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SyncBasedOnCodeFixProvider)), Shared]
    public class SyncBasedOnCodeFixProvider : CodeFixProvider {
        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(SyncBasedOnAnalyzer.DiagnosticId);
        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context) {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            var declaration = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();

            if (declaration == null)
                return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: CodeFixResources.AknowledgeSyncSchema,
                    createChangedSolution: c => MakeUppercaseAsync(context.Document, declaration, c),//UpdateLastUpdatedAsync(context.Document, declaration, c),
                    equivalenceKey: nameof(CodeFixResources.AknowledgeSyncSchema)),
                diagnostic);
        }

        private async Task<Document> UpdateLastUpdatedAsync(Document document, TypeDeclarationSyntax typeDecl, CancellationToken cancellationToken) {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

            // 1. Dig into the leading trivia to find the specific SyntaxTrivia node holding the XML Documentation
            var triviaList = typeDecl.GetLeadingTrivia();
            var docTrivia = triviaList.FirstOrDefault(t => t.HasStructure && t.GetStructure() is DocumentationCommentTriviaSyntax);

            // If we didn't find XML docs, abort
            if (docTrivia.Kind() == SyntaxKind.None)
                return document;

            var docComment = (DocumentationCommentTriviaSyntax)docTrivia.GetStructure();

            // 2. Find the <BasedOn /> element
            var basedOnElement = docComment.Content
                .OfType<XmlEmptyElementSyntax>()
                .FirstOrDefault(e => e.Name.LocalName.Text == "BasedOn");

            if (basedOnElement == null)
                return document;

            // 3. Find the LastUpdated attribute
            var lastUpdatedAttr = basedOnElement.Attributes
                .OfType<XmlTextAttributeSyntax>()
                .FirstOrDefault(a => a.Name.LocalName.Text == "LastUpdated");

            if (lastUpdatedAttr == null)
                return document;

            // 4. Generate the new timestamp string
            var newTimestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

            // 5. Create new text tokens for the attribute
            var newTextTokens = SyntaxFactory.TokenList(
                SyntaxFactory.XmlTextLiteral(
                    SyntaxFactory.TriviaList(),
                    newTimestamp,
                    newTimestamp,
                    SyntaxFactory.TriviaList()));

            // --- 6. Rebuild the tree from the inside out ---

            // Swap tokens in the attribute
            var newLastUpdatedAttr = lastUpdatedAttr.WithTextTokens(newTextTokens);

            // Swap attribute in the element
            var newBasedOnElement = basedOnElement.ReplaceNode(lastUpdatedAttr, newLastUpdatedAttr);

            // Swap element in the doc comment
            var newDocComment = docComment.ReplaceNode(basedOnElement, newBasedOnElement);

            // Wrap the newly modified doc comment back into a trivia node
            var newTrivia = SyntaxFactory.Trivia(newDocComment);

            // Swap the old trivia for the new trivia in the class/record's trivia list
            var newTriviaList = triviaList.Replace(docTrivia, newTrivia);
            var newTypeDecl = typeDecl.WithLeadingTrivia(newTriviaList);

            // Finally, swap the entire class/record declaration out in the root of the document
            var newRoot = root.ReplaceNode(typeDecl, newTypeDecl);

            return document.WithSyntaxRoot(newRoot);
        }

        private async Task<Solution> MakeUppercaseAsync(Document document, TypeDeclarationSyntax typeDecl, CancellationToken cancellationToken) {
            // Compute new uppercase name.
            var identifierToken = typeDecl.Identifier;
            var newName = identifierToken.Text.ToUpperInvariant();

            // Get the symbol representing the type to be renamed.
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);

            // Produce a new solution that has all references to that type renamed, including the declaration.
            var originalSolution = document.Project.Solution;
            var optionSet = originalSolution.Workspace.Options;
            var newSolution = await Renamer.RenameSymbolAsync(document.Project.Solution, typeSymbol, newName, optionSet, cancellationToken).ConfigureAwait(false);

            // Return the new solution with the now-uppercase type name.
            return newSolution;
        }
    }
}