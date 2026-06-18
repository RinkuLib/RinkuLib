using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;

namespace RinkuLib.Analyzers {

    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TestCodeFixProvider)), Shared]
    public class TestCodeFixProvider : CodeFixProvider {

        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(SyncBasedOnAnalyzer.DiagnosticId);

        public sealed override FixAllProvider GetFixAllProvider() {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context) {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            // We just grab the diagnostic to attach the fix to it. No node searching at all.
            var diagnostic = context.Diagnostics.First();

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "TEST: Add dummy comment",
                    createChangedDocument: c => AddDummyCommentAsync(context.Document, root, c),
                    equivalenceKey: "TestFix"),
                diagnostic);
        }

        private Task<Document> AddDummyCommentAsync(Document document, SyntaxNode root, CancellationToken cancellationToken) {
            // The safest possible syntax change: append a comment to the end of the entire file.
            var newRoot = root.WithTrailingTrivia(root.GetTrailingTrivia().Add(SyntaxFactory.Comment("\n// TEST FIX WORKED")));
            return Task.FromResult(document.WithSyntaxRoot(newRoot));
        }
    }
}