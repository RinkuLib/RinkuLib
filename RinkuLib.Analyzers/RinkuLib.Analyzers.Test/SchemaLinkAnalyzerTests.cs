using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using VerifyAdd = RinkuLib.Analyzers.Test.CSharpAnalyzerVerifier<RinkuLib.Analyzers.AddBasedOnAnalyzer>;
using VerifyBasedOn = RinkuLib.Analyzers.Test.CSharpAnalyzerVerifier<RinkuLib.Analyzers.BasedOnAnalyzer>;
using VerifyMatch = RinkuLib.Analyzers.Test.CSharpAnalyzerVerifier<RinkuLib.Analyzers.MatchConstructorAnalyzer>;
using VerifySync = RinkuLib.Analyzers.Test.CSharpAnalyzerVerifier<RinkuLib.Analyzers.SyncBasedOnAnalyzer>;

namespace RinkuLib.Analyzers.Test;

[TestClass]
public sealed class SchemaLinkAnalyzerTests {
    [TestMethod]
    public async Task LinkSuggestionStaysQuietWithoutASchema() {
        await VerifyAdd.VerifyAnalyzerAsync("public class CustomerDto { }");
    }

    [TestMethod]
    public async Task LinkSuggestionReportsConsumerTypesOnly() {
        const string source = """
            [Schema("2026-08-11T10:00Z")]
            public record CustomerSchema(int Id);

            public class {|#0:CustomerDto|} { }
            """;

        var expected = VerifyAdd.Diagnostic(AddBasedOnAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("CustomerDto");
        await VerifyAdd.VerifyAnalyzerAsync(source, expected);
    }

    [TestMethod]
    public async Task EitherLinkKindSuppressesTheSuggestion() {
        const string source = """
            [Schema("2026-08-11T10:00Z")]
            public record CustomerSchema(int Id);

            [BasedOn(typeof(CustomerSchema), lastUpdated: "2026-08-11T10:00Z")]
            public record TrackedCustomer(int Id);

            [BasedOn(typeof(CustomerSchema))]
            public record MatchedCustomer(int Id);
            """;

        await VerifyAdd.VerifyAnalyzerAsync(source);
    }

    [TestMethod]
    public async Task BasedOnLinkWithoutVersioningStillExposesMemberActions() {
        const string source = """
            public record CustomerSchema(int Id);

            [{|#0:BasedOn(typeof(CustomerSchema))|}]
            public record CustomerDto(int Id);
            """;

        var expected = VerifyBasedOn.Diagnostic(BasedOnAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("CustomerSchema");
        await VerifyBasedOn.VerifyAnalyzerAsync(source, expected);
    }

    [TestMethod]
    public async Task VersionedSourceWithoutLinkTimestampStillExposesAcknowledgement() {
        const string source = """
            [Schema("2026-08-11T10:00Z")]
            public record CustomerSchema(int Id);

            [{|#0:BasedOn(typeof(CustomerSchema))|}]
            public record CustomerDto(int Id);
            """;

        var expected = VerifyBasedOn.Diagnostic(BasedOnAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("CustomerSchema");
        await VerifyBasedOn.VerifyAnalyzerAsync(source, expected);
    }

    [TestMethod]
    public async Task OlderLinkReportsAWarning() {
        const string source = """
            [Schema("2026-08-11T10:00Z")]
            public record CustomerSchema(int Id);

            [{|#0:BasedOn(typeof(CustomerSchema), lastUpdated: "2026-08-10T10:00Z")|}]
            public record CustomerDto(int Id);
            """;

        var expected = VerifySync.Diagnostic(SyncBasedOnAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("CustomerDto", "CustomerSchema");
        await VerifySync.VerifyAnalyzerAsync(source, expected);
    }

    [TestMethod]
    public async Task MissingLinkTimestampUsesConstructorMatching() {
        const string source = """
            [Schema("2026-08-11T10:00Z")]
            public record CustomerSchema(int Id);

            [{|#0:BasedOn(typeof(CustomerSchema))|}]
            public record CustomerDto(long Id);
            """;

        var expected = VerifyMatch.Diagnostic(MatchConstructorAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("CustomerDto", "CustomerSchema");
        await VerifyMatch.VerifyAnalyzerAsync(source, expected);
    }

    [TestMethod]
    public async Task CurrentOrNewerLinkDoesNotReport() {
        const string source = """
            [Schema("2026-08-11T10:00Z")]
            public record CustomerSchema(int Id);

            [BasedOn(typeof(CustomerSchema), lastUpdated: "2026-08-11T10:00Z")]
            public record CurrentCustomer(int Id);

            [BasedOn(typeof(CustomerSchema), lastUpdated: "2026-08-12T10:00Z")]
            public record NewerCustomer(int Id);
            """;

        await VerifySync.VerifyAnalyzerAsync(source);
    }

    [TestMethod]
    public async Task ReferenceWithoutSchemaTimestampDoesNotReport() {
        const string source = """
            public record CustomerSchema(int Id);

            [BasedOn(typeof(CustomerSchema))]
            public record CustomerDto(int Id);
            """;

        await VerifySync.VerifyAnalyzerAsync(source);
    }
}
