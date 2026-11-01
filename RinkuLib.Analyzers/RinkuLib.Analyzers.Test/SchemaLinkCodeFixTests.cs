using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using VerifyAddFix = RinkuLib.Analyzers.Test.CSharpCodeFixVerifier<RinkuLib.Analyzers.AddBasedOnAnalyzer, RinkuLib.Analyzers.AddBasedOnCodeFixProvider>;

namespace RinkuLib.Analyzers.Test;

[TestClass]
public sealed class SchemaLinkCodeFixTests {
    [TestMethod]
    public async Task SchemaWarningOffersACompilingFixForPositionalNamedAndConstantTimestamps() {
        foreach (string arguments in new[] { "typeof(CustomerSchema), \"2016-08-10T09:00Z\"", "typeof(CustomerSchema), lastUpdated: \"2016-08-10T09:00Z\"", "lastUpdated: Dates.Old, declaringType: typeof(CustomerSchema)" }) {
            string source = $$"""
                public static class Dates { public const string Old = "2016-08-10T09:00Z"; }
                [Schema("2026-08-11T10:00Z")]
                public record CustomerSchema(int Id, string Name);

                [BasedOn({{arguments}})]
                public record CustomerDto(int Id);
                """;
            using var workspace = new AdhocWorkspace();
            var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "Test", "Test", LanguageNames.CSharp, parseOptions: CSharpParseOptions.Default, compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));
            project = project.AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            project = project.AddDocument("RinkuSchemaAttributes.g.cs", SourceText.From(CSharpVerifierHelper.SchemaAttributes)).Project;
            Assert.IsTrue(workspace.TryApplyChanges(project.Solution));
            var document = workspace.AddDocument(project.Id, "Test.cs", SourceText.From(source));
            var compilation = (await document.Project.GetCompilationAsync())!;
            var diagnostics = await compilation.WithAnalyzers([new SyncBasedOnAnalyzer()]).GetAnalyzerDiagnosticsAsync();
            var warning = diagnostics.Single(diagnostic => diagnostic.Id == SyncBasedOnAnalyzer.DiagnosticId);
            var provider = new BasedOnLastModifiedCodeFixProvider();
            Assert.IsTrue(provider.FixableDiagnosticIds.Contains(warning.Id));
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, warning, (action, _) => actions.Add(action), CancellationToken.None));
            Assert.AreEqual(1, actions.Count);
            var operation = (ApplyChangesOperation)(await actions[0].GetOperationsAsync(CancellationToken.None)).Single();
            var changed = operation.ChangedSolution.GetDocument(document.Id)!;
            string result = (await changed.GetTextAsync()).ToString();
            StringAssert.Contains(result, "\"2026-08-11T10:00:00.000Z\"");
            var updatedCompilation = (await changed.Project.GetCompilationAsync())!;
            Assert.IsFalse(updatedCompilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            Assert.AreEqual(0, (await updatedCompilation.WithAnalyzers([new SyncBasedOnAnalyzer(), new MatchConstructorAnalyzer()]).GetAnalyzerDiagnosticsAsync()).Length);
        }
    }

    [TestMethod]
    public async Task AcknowledgeActionUsesTheReferencedSchemaTimestampOnce() {
        const string source = """
            [Schema("2026-08-11T10:00Z")]
            public record CustomerSchema(int Id);

            [BasedOn(typeof(CustomerSchema), "2026-08-10T09:00Z")]
            public record CustomerDto(int Id);
            """;
        const string fixedSource = """
            [Schema("2026-08-11T10:00Z")]
            public record CustomerSchema(int Id);

            [BasedOn(typeof(CustomerSchema), "2026-08-11T10:00:00.000Z")]
            public record CustomerDto(int Id);
            """;

        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Test", LanguageNames.CSharp);
        var document = workspace.AddDocument(project.Id, "Test.cs", SourceText.From(source));
        var actions = await GetAcknowledgeActionsAsync(document);

        Assert.AreEqual(1, actions.Count);
        var operation = (ApplyChangesOperation)(await actions[0].GetOperationsAsync(CancellationToken.None)).Single();
        var changed = operation.ChangedSolution.GetDocument(document.Id)!;
        Assert.AreEqual(fixedSource, (await changed.GetTextAsync()).ToString());
        Assert.AreEqual(0, (await GetAcknowledgeActionsAsync(changed)).Count);
    }

    [TestMethod]
    public async Task AcknowledgeActionAddsMissingTimestampEvenWhenTheShapeMatches() {
        const string source = """
            [Schema("2026-08-11T10:00Z")]
            public record CustomerSchema(int Id);

            [BasedOn(typeof(CustomerSchema))]
            public record CustomerDto(int Id);
            """;

        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Test", LanguageNames.CSharp);
        var document = workspace.AddDocument(project.Id, "Test.cs", SourceText.From(source));
        var actions = await GetAcknowledgeActionsAsync(document);

        Assert.AreEqual(1, actions.Count);
        var operation = (ApplyChangesOperation)(await actions[0].GetOperationsAsync(CancellationToken.None)).Single();
        var changed = operation.ChangedSolution.GetDocument(document.Id)!;
        StringAssert.Contains((await changed.GetTextAsync()).ToString(), "[BasedOn(typeof(CustomerSchema), \"2026-08-11T10:00:00.000Z\")]");
    }

    [TestMethod]
    public async Task BasedOnLinkStartsAtTheCurrentSchemaTimestamp() {
        const string source = """
            [Schema("2026-08-11T10:00Z")]
            public record CustomerSchema(int Id);

            public class {|#0:CustomerDto|} { }
            """;
        string actual = await ApplyBasedOnCodeFixAsync(source);

        StringAssert.Contains(actual, "[BasedOn(typeof(global::CustomerSchema), \"2026-08-11T10:00:00.000Z\")]");
    }

    [TestMethod]
    public void AcknowledgingMethodSchemaKeepsSelectorInTheThirdPosition() {
        foreach (string arguments in new[] { "typeof(Schemas), methodName: nameof(Schemas.Read)", "methodName: nameof(Schemas.Read), declaringType: typeof(Schemas)", "typeof(Schemas), null, nameof(Schemas.Read)" }) {
            var attribute = SyntaxFactory.ParseCompilationUnit($"[BasedOn({arguments})] class Row {{ }}").DescendantNodes().OfType<AttributeSyntax>().Single();
            var updated = BasedOnLastModifiedCodeFixProvider.WithTimestamp(attribute, DateTimeOffset.Parse("2026-08-11T10:00Z", System.Globalization.CultureInfo.InvariantCulture));
            Assert.AreEqual("BasedOn(typeof(Schemas), \"2026-08-11T10:00:00.000Z\", nameof(Schemas.Read))", updated.NormalizeWhitespace().ToFullString());
        }
    }

    private static async Task<string> ApplyBasedOnCodeFixAsync(string source) {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "Test", "Test", LanguageNames.CSharp, parseOptions: CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Diagnose), compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));
        project = project.AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        project = project.AddDocument("RinkuSchemaAttributes.g.cs", SourceText.From(CSharpVerifierHelper.SchemaAttributes)).Project;
        Assert.IsTrue(workspace.TryApplyChanges(project.Solution));
        var document = workspace.AddDocument(project.Id, "Test.cs", SourceText.From(source));
        var root = await document.GetSyntaxRootAsync();
        var declaration = root!.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var diagnostic = Diagnostic.Create(new DiagnosticDescriptor(AddBasedOnAnalyzer.DiagnosticId, "Add schema link", "Add a schema link to '{0}'", "Rinku", DiagnosticSeverity.Hidden, true), declaration.Identifier.GetLocation(), "CustomerDto");
        var actions = new List<CodeAction>();
        await new AddBasedOnCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        var choice = actions.Single().NestedActions.Single().NestedActions.Single();
        var operation = (ApplyChangesOperation)(await choice.GetOperationsAsync(CancellationToken.None)).Single();
        return (await operation.ChangedSolution.GetDocument(document.Id)!.GetTextAsync()).ToString();
    }

    private static async Task<List<CodeAction>> GetAcknowledgeActionsAsync(Document document) {
        var root = await document.GetSyntaxRootAsync();
        var tag = root!.DescendantNodes(descendIntoTrivia: true)
            .OfType<AttributeSyntax>()
            .Single(node => DocumentationTags.GetAttributeName(node) == DocumentationTags.BasedOn);
        var descriptor = new DiagnosticDescriptor(
            BasedOnAnalyzer.DiagnosticId,
            "BasedOn actions available",
            "Actions are available for '{0}'",
            "Rinku",
            DiagnosticSeverity.Hidden,
            isEnabledByDefault: true);
        var properties = ImmutableDictionary<string, string?>.Empty.Add(
            BasedOnAnalyzer.SchemaTimestampProperty,
            "2026-08-11T10:00:00.0000000+00:00");
        var diagnostic = Diagnostic.Create(descriptor, tag.GetLocation(), properties, "CustomerSchema");
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);
        await new BasedOnLastModifiedCodeFixProvider().RegisterCodeFixesAsync(context);
        return actions;
    }
}
