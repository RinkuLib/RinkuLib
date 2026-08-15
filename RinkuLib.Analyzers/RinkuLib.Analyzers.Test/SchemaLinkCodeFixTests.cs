using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using VerifyAddFix = RinkuLib.Analyzers.Test.CSharpCodeFixVerifier<RinkuLib.Analyzers.AddBasedOnAnalyzer, RinkuLib.Analyzers.AddBasedOnCodeFixProvider>;

namespace RinkuLib.Analyzers.Test;

[TestClass]
public sealed class SchemaLinkCodeFixTests {
    [TestMethod]
    public async Task AcknowledgeActionUsesTheReferencedSchemaTimestampOnce() {
        const string source = """
            /// <Schema LastUpdated="2026-08-11T10:00Z" />
            public record CustomerSchema(int Id);

            /// <BasedOn cref="CustomerSchema" LastUpdated="2026-08-10T09:00Z" />
            public record CustomerDto(int Id);
            """;
        const string fixedSource = """
            /// <Schema LastUpdated="2026-08-11T10:00Z" />
            public record CustomerSchema(int Id);

            /// <BasedOn cref="CustomerSchema" LastUpdated="2026-08-11T10:00Z" />
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
    public async Task BasedOnLinkStartsAtTheCurrentSchemaTimestamp() {
        const string source = """
            /// <Schema LastUpdated="2026-08-11T10:00Z" />
            public record CustomerSchema(int Id);

            public class {|#0:CustomerDto|} { }
            """;
        const string fixedSource = """
            /// <Schema LastUpdated="2026-08-11T10:00Z" />
            public record CustomerSchema(int Id);

            /// <BasedOn cref="CustomerSchema" LastUpdated="2026-08-11T10:00Z" />
            public class CustomerDto { }
            """;

        var diagnostic = VerifyAddFix.Diagnostic(AddBasedOnAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("CustomerDto");
        var test = new VerifyAddFix.Test {
            TestCode = source,
            FixedCode = fixedSource,
            CodeActionEquivalenceKey = "Add-BasedOn-CustomerSchema"
        };
        test.ExpectedDiagnostics.Add(diagnostic);
        await test.RunAsync();
    }

    [TestMethod]
    public async Task StrictLinkHasNoTimestamp() {
        const string source = """
            /// <Schema LastUpdated="2026-08-11T10:00Z" />
            public record CustomerSchema(int Id);

            public class {|#0:CustomerDto|} { }
            """;
        const string fixedSource = """
            /// <Schema LastUpdated="2026-08-11T10:00Z" />
            public record CustomerSchema(int Id);

            /// <MatchConstructor cref="CustomerSchema" />
            public class CustomerDto { }
            """;

        var diagnostic = VerifyAddFix.Diagnostic(AddBasedOnAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("CustomerDto");
        var test = new VerifyAddFix.Test {
            TestCode = source,
            FixedCode = fixedSource,
            CodeActionEquivalenceKey = "Add-MatchConstructor-CustomerSchema"
        };
        test.ExpectedDiagnostics.Add(diagnostic);
        await test.RunAsync();
    }

    private static async Task<List<CodeAction>> GetAcknowledgeActionsAsync(Document document) {
        var root = await document.GetSyntaxRootAsync();
        var tag = root!.DescendantNodes(descendIntoTrivia: true)
            .OfType<XmlEmptyElementSyntax>()
            .Single(node => node.Name.LocalName.ValueText == DocumentationTags.BasedOn);
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
