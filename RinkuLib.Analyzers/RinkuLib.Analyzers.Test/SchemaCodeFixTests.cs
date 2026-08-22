using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RinkuLib.Analyzers.Test;

[TestClass]
public sealed class SchemaCodeFixTests {
    [TestMethod]
    public async Task AddSchemaLinkOffersBothLinkKindsAndSourceSchemas() {
        const string source = """
            /// <Schema LastUpdated="2026-08-21T14:00Z" />
            public record AlbumSchema(int Id, string Title);

            public static class Schemas {
                /// <Schema LastUpdated="2026-08-21T15:00Z" />
                public static object ReadSchema(int id, string title) => new();
            }

            public record AlbumDto(int Id, string Title);
            """;

        using var workspace = new AdhocWorkspace();
        Document document = CreateDocument(workspace, source);
        CodeAction action = await GetAddSchemaLinkActionAsync(document, "AlbumDto");

        Assert.AreEqual("Add schema link", action.Title);
        Assert.AreEqual(2, action.NestedActions.Length);
        Assert.AreEqual("Track schema changes", action.NestedActions[0].Title);
        Assert.AreEqual("Require a matching constructor", action.NestedActions[1].Title);

        foreach (CodeAction linkKind in action.NestedActions) {
            Assert.AreEqual(2, linkKind.NestedActions.Length);
            Assert.IsTrue(linkKind.NestedActions.Any(x => x.Title.Contains("AlbumSchema", StringComparison.Ordinal)));
            Assert.IsTrue(linkKind.NestedActions.Any(x => x.Title.Contains("ReadSchema", StringComparison.Ordinal)));
        }
    }

    [TestMethod]
    public async Task TrackSchemaChangesWritesBasedOnTimestamp() {
        const string source = """
            /// <Schema LastUpdated="2026-08-21T14:00Z" />
            public record AlbumSchema(int Id, string Title);

            public record AlbumDto(int Id, string Title);
            """;

        using var workspace = new AdhocWorkspace();
        Document document = CreateDocument(workspace, source);
        CodeAction action = await GetAddSchemaLinkActionAsync(document, "AlbumDto");
        CodeAction leaf = action.NestedActions
            .Single(x => x.Title == "Track schema changes")
            .NestedActions.Single();

        string actual = await ApplyAsync(document, leaf);

        StringAssert.Contains(
            actual,
            "/// <BasedOn cref=\"AlbumSchema\" LastUpdated=\"2026-08-21T14:00Z\" />");
    }

    [TestMethod]
    public async Task MatchingConstructorLinkOmitsTimestamp() {
        const string source = """
            /// <Schema LastUpdated="2026-08-21T14:00Z" />
            public record AlbumSchema(int Id, string Title);

            public record AlbumDto(int Id, string Title);
            """;

        using var workspace = new AdhocWorkspace();
        Document document = CreateDocument(workspace, source);
        CodeAction action = await GetAddSchemaLinkActionAsync(document, "AlbumDto");
        CodeAction leaf = action.NestedActions
            .Single(x => x.Title == "Require a matching constructor")
            .NestedActions.Single();

        string actual = await ApplyAsync(document, leaf);

        StringAssert.Contains(actual, "/// <MatchConstructor cref=\"AlbumSchema\" />");
        Assert.IsFalse(actual.Contains("<MatchConstructor cref=\"AlbumSchema\" LastUpdated=", StringComparison.Ordinal));
    }

    private static Document CreateDocument(AdhocWorkspace workspace, string source) {
        Project project = workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            "Test",
            "Test",
            LanguageNames.CSharp,
            parseOptions: CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Diagnose),
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));

        project = project.AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        Assert.IsTrue(workspace.TryApplyChanges(project.Solution));

        return workspace.AddDocument(project.Id, "Test.cs", SourceText.From(source));
    }

    private static async Task<CodeAction> GetAddSchemaLinkActionAsync(Document document, string targetName) {
        SyntaxNode root = (await document.GetSyntaxRootAsync())!;
        TypeDeclarationSyntax target = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Single(x => x.Identifier.ValueText == targetName);

        var descriptor = new DiagnosticDescriptor(
            AddBasedOnAnalyzer.DiagnosticId,
            "Schema link available",
            "Schema link available for '{0}'",
            "Rinku",
            DiagnosticSeverity.Hidden,
            isEnabledByDefault: true);
        Diagnostic diagnostic = Diagnostic.Create(descriptor, target.Identifier.GetLocation(), targetName);

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);

        await new AddBasedOnCodeFixProvider().RegisterCodeFixesAsync(context);

        Assert.AreEqual(1, actions.Count);
        return actions[0];
    }

    private static async Task<string> ApplyAsync(Document originalDocument, CodeAction action) {
        IEnumerable<CodeActionOperation> operations = await action.GetOperationsAsync(CancellationToken.None);
        ApplyChangesOperation apply = operations.OfType<ApplyChangesOperation>().Single();
        Document changed = apply.ChangedSolution.GetDocument(originalDocument.Id)!;
        return (await changed.GetTextAsync()).ToString();
    }
}
