using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RinkuLib.Analyzers.Test;

[TestClass]
public sealed class AnalyzerInfrastructureTests {
    [TestMethod]
    public void DocumentationTagFindsItsContainingType() {
        const string source = """
            public class Source { }
            [BasedOn(typeof(Source))]
            public class Target { }
            """;
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var tag = root.DescendantNodes().OfType<AttributeSyntax>().Single();

        var target = DocumentationTags.FindContainingType(root, tag.Span);

        Assert.IsNotNull(target);
        Assert.AreEqual("Target", target!.Identifier.ValueText);
        Assert.AreSame(target, root.DescendantNodes().OfType<ClassDeclarationSyntax>().Last());
        Assert.AreSame(tag, DocumentationTags.FindTag(root, tag.Span, DocumentationTags.BasedOn));
    }

    [TestMethod]
    public async Task TimestampFixPreservesDeclarationsAndUsesPositionalMetadata() {
        const string source = """
            [Schema("2026-08-11T10:00Z")]
            public record CustomerSchema(int Id);

            [BasedOn(typeof(CustomerSchema), lastUpdated: "2026-08-10T09:00Z")]
            public record CustomerDto(int Id);
            """;
        const string expected = """
            [Schema("2026-08-11T10:00Z")]
            public record CustomerSchema(int Id);

            [BasedOn(typeof(CustomerSchema), "2026-08-11T10:00:00.000Z")]
            public record CustomerDto(int Id);
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Test", LanguageNames.CSharp);
        var document = workspace.AddDocument(project.Id, "Test.cs", SourceText.From(source));
        var root = await document.GetSyntaxRootAsync();
        var tag = root!.DescendantNodes().OfType<AttributeSyntax>()
            .Single(node => DocumentationTags.GetAttributeName(node) == DocumentationTags.BasedOn);

        var changed = await BasedOnLastModifiedCodeFixProvider.ApplyAsync(
            document,
            tag.Span,
            DateTimeOffset.Parse("2026-08-11T10:00Z"),
            CancellationToken.None);
        var actual = (await changed.GetTextAsync()).ToString();

        Assert.AreEqual(expected.TrimEnd(), actual.TrimEnd(), $"Actual code points {string.Join(",", actual.Select(c => (int)c))}");
    }
}
