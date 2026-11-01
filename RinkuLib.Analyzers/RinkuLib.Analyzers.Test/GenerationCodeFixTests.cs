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
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using VerifyConstructorFix = RinkuLib.Analyzers.Test.CSharpCodeFixVerifier<RinkuLib.Analyzers.MatchConstructorAnalyzer, RinkuLib.Analyzers.GenerateCtorCodeFixProvider>;
using VerifyInvocationFix = RinkuLib.Analyzers.Test.CSharpCodeFixVerifier<RinkuLib.Analyzers.MethodInvocationCompletionAnalyzer, RinkuLib.Analyzers.MethodInvocationCodeFixProvider>;

namespace RinkuLib.Analyzers.Test;

[TestClass]
public sealed class GenerationCodeFixTests {
    [TestMethod]
    public async Task StrictLinkCanAddAMatchingConstructor() {
        const string source = """
            #nullable enable
            public class CustomerSchema {
                public CustomerSchema(int id, string? name) { }
            }

            [BasedOn(typeof(CustomerSchema))]
            public class CustomerDto { }
            """;
        string actual = await ApplyConstructorCodeFixAsync(source, "CustomerDto", "CustomerSchema");
        StringAssert.Contains(actual, "public CustomerDto(int id, string? name)");
    }

    [TestMethod]
    public async Task ConstructorFixCopiesRefKindsParamsNullabilityAndAttributes() {
        const string source = """
            #nullable enable
            using System;

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class SlotAttribute(string name) : Attribute {
                public bool Required { get; set; }
            }

            public static class Schemas {
                public static void Read([Slot("id", Required = true)] ref int id, params string?[] names) { }
            }

            [BasedOn(typeof(Schemas), null, nameof(Schemas.Read))]
            public class CustomerDto { }
            """;
        string actual = await ApplyConstructorCodeFixAsync(source, "CustomerDto", "void Schemas.Read(ref int id, params string?[] names)");
        StringAssert.Contains(actual, "public CustomerDto([Slot(\"id\", Required = true)] ref int id, params string?[] names)");
    }

    [TestMethod]
    public async Task ConstructorFixIsNotOfferedWhenItWouldDuplicateASignature() {
        const string source = """
            public class CustomerSchema {
                public CustomerSchema(int id) { }
            }

            [{|#0:BasedOn(typeof(CustomerSchema))|}]
            public class CustomerDto {
                public CustomerDto(int customerId) { }
            }
            """;

        var diagnostic = VerifyConstructorFix.Diagnostic(MatchConstructorAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("CustomerDto", "CustomerSchema");
        var test = new VerifyConstructorFix.Test {
            TestCode = source,
            FixedCode = source,
            NumberOfIncrementalIterations = 0
        };
        test.ExpectedDiagnostics.Add(diagnostic);
        await test.RunAsync();
    }

    [TestMethod]
    public async Task ConstructorAndPropertiesFixIsNotOfferedForAnOutParameter() {
        const string source = """
            public static class Schemas {
                public static void Read(out int id) => id = 0;
            }

            [BasedOn(typeof(Schemas), null, nameof(Schemas.Read))]
            public class CustomerDto { }
            """;

        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            "Test",
            "Test",
            LanguageNames.CSharp,
            parseOptions: CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Diagnose),
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));
        project = project.AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        project = project.AddDocument("RinkuSchemaAttributes.g.cs", SourceText.From(CSharpVerifierHelper.SchemaAttributes)).Project;
        Assert.IsTrue(workspace.TryApplyChanges(project.Solution));
        var document = workspace.AddDocument(project.Id, "Test.cs", SourceText.From(source));
        var root = await document.GetSyntaxRootAsync();
        var tag = root!.DescendantNodes(descendIntoTrivia: true)
            .OfType<AttributeSyntax>()
            .Single(node => DocumentationTags.GetAttributeName(node) == DocumentationTags.BasedOn);
        var descriptor = new DiagnosticDescriptor(
            MatchConstructorAnalyzer.DiagnosticId,
            "Constructor does not match",
            "'{0}' needs a constructor matching '{1}'",
            "Rinku",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
        var diagnostic = Diagnostic.Create(descriptor, tag.GetLocation(), "CustomerDto", "Schemas.Read");
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);

        await new GenerateCtorCodeFixProvider().RegisterCodeFixesAsync(context);

        Assert.AreEqual(1, actions.Count);
        Assert.IsTrue(actions[0].Title.StartsWith("Add constructor from", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task InvocationFixUsesMatchingArgumentsInScope() {
        const string source = """
            class Commands {
                int Save(int id) => id;
                int Build(int id) => {|#0:Save|};
            }
            """;
        const string fixedSource = """
            class Commands {
                int Save(int id) => id;
                int Build(int id) => Save(id);
            }
            """;

        var diagnostic = VerifyInvocationFix.Diagnostic(MethodInvocationCompletionAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("Save");
        var test = new VerifyInvocationFix.Test {
            TestCode = source,
            FixedCode = fixedSource,
            CompilerDiagnostics = CompilerDiagnostics.None,
            CodeActionIndex = 0
        };
        test.ExpectedDiagnostics.Add(diagnostic);
        await test.RunAsync();
    }

    [TestMethod]
    public async Task InvocationFixThreadsMissingArgumentsThroughTheCaller() {
        const string source = """
            class Commands {
                int Save(int id) => id;
                int Build() => {|#0:Save|};
            }
            """;
        const string fixedSource = """
            class Commands {
                int Save(int id) => id;
                int Build(int id) => Save(id);
            }
            """;

        var diagnostic = VerifyInvocationFix.Diagnostic(MethodInvocationCompletionAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("Save");
        var test = new VerifyInvocationFix.Test {
            TestCode = source,
            FixedCode = fixedSource,
            CompilerDiagnostics = CompilerDiagnostics.None,
            CodeActionIndex = 0
        };
        test.ExpectedDiagnostics.Add(diagnostic);
        await test.RunAsync();
    }

    private static async Task<string> ApplyConstructorCodeFixAsync(string source, string targetName, string referenceName) {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "Test", "Test", LanguageNames.CSharp, parseOptions: CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Diagnose), compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));
        project = project.AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        project = project.AddDocument("RinkuSchemaAttributes.g.cs", SourceText.From(CSharpVerifierHelper.SchemaAttributes)).Project;
        Assert.IsTrue(workspace.TryApplyChanges(project.Solution));
        var document = workspace.AddDocument(project.Id, "Test.cs", SourceText.From(source));
        var root = await document.GetSyntaxRootAsync();
        var tag = root!.DescendantNodes().OfType<AttributeSyntax>().Single(node => DocumentationTags.GetAttributeName(node) == DocumentationTags.BasedOn);
        var descriptor = new DiagnosticDescriptor(MatchConstructorAnalyzer.DiagnosticId, "Constructor does not match", "'{0}' needs a constructor matching '{1}'", "Rinku", DiagnosticSeverity.Warning, true);
        var diagnostic = Diagnostic.Create(descriptor, tag.GetLocation(), targetName, referenceName);
        var actions = new List<CodeAction>();
        await new GenerateCtorCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        CodeAction action = actions.Single(item => item.Title.StartsWith("Add constructor from", StringComparison.Ordinal));
        var operation = (ApplyChangesOperation)(await action.GetOperationsAsync(CancellationToken.None)).Single();
        return (await operation.ChangedSolution.GetDocument(document.Id)!.GetTextAsync()).ToString();
    }
}
