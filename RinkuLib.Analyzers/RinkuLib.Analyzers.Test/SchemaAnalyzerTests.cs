using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using VerifyAdd = RinkuLib.Analyzers.Test.CSharpAnalyzerVerifier<RinkuLib.Analyzers.AddBasedOnAnalyzer>;
using VerifyActions = RinkuLib.Analyzers.Test.CSharpAnalyzerVerifier<RinkuLib.Analyzers.BasedOnAnalyzer>;
using VerifySync = RinkuLib.Analyzers.Test.CSharpAnalyzerVerifier<RinkuLib.Analyzers.SyncBasedOnAnalyzer>;

namespace RinkuLib.Analyzers.Test;

[TestClass]
public sealed class SchemaAnalyzerTests {
    [TestMethod]
    public async Task PositionalAndNamedArgumentsReportTheSameSchemaChange() {
        foreach (string arguments in new[] { "typeof(AlbumSchema), \"2026-08-21T14:00Z\"", "typeof(AlbumSchema), lastUpdated: \"2026-08-21T14:00Z\"", "lastUpdated: \"2026-08-21T14:00Z\", declaringType: typeof(AlbumSchema)" }) {
            string source = $$"""
                [Schema("2026-08-22T09:30Z")]
                public record AlbumSchema(int Id, string Title);

                [{|#0:BasedOn({{arguments}})|}]
                public record AlbumDto(int Id);
                """;
            await VerifySync.VerifyAnalyzerAsync(source, VerifySync.Diagnostic(SyncBasedOnAnalyzer.DiagnosticId).WithLocation(0).WithArguments("AlbumDto", "AlbumSchema"));
            await CSharpAnalyzerVerifier<MatchConstructorAnalyzer>.VerifyAnalyzerAsync(source, CSharpAnalyzerVerifier<MatchConstructorAnalyzer>.Diagnostic(MatchConstructorAnalyzer.DiagnosticId).WithLocation(0).WithArguments("AlbumDto", "AlbumSchema"));
        }
    }

    [TestMethod]
    public async Task MethodSelectionIsIndependentOfArgumentNamingAndOrder() {
        foreach (string arguments in new[] { "typeof(Schemas), \"2026-08-21T14:00Z\", nameof(Schemas.Read)", "typeof(Schemas), methodName: nameof(Schemas.Read), lastUpdated: \"2026-08-21T14:00Z\"" }) {
            string source = $$"""
                public static class Schemas {
                    [Schema("2026-08-22T09:30Z")]
                    public static void Read(int Id) { }
                }
                [{|#0:BasedOn({{arguments}})|}]
                public record AlbumDto(int Id);
                """;
            await VerifySync.VerifyAnalyzerAsync(source, VerifySync.Diagnostic(SyncBasedOnAnalyzer.DiagnosticId).WithLocation(0).WithArguments("AlbumDto", "void Schemas.Read(int Id)"));
        }
    }

    [TestMethod]
    public async Task SchemaMakesLinkActionAvailable() {
        const string source = """
            [Schema("2026-08-21T14:00Z")]
            public record AlbumSchema(int Id, string Title);

            public record {|#0:AlbumDto|}(int Id, string Title);
            """;

        var expected = VerifyAdd.Diagnostic(AddBasedOnAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("AlbumDto");

        await VerifyAdd.VerifyAnalyzerAsync(source, expected);
    }

    [TestMethod]
    public async Task NoSchemaMeansNoLinkSuggestion() {
        const string source = """
            public record AlbumDto(int Id, string Title);
            """;

        await VerifyAdd.VerifyAnalyzerAsync(source);
    }

    [TestMethod]
    public async Task ExistingLinkSuppressesAddLinkSuggestion() {
        const string source = """
            [Schema("2026-08-21T14:00Z")]
            public record AlbumSchema(int Id, string Title);

            [BasedOn(typeof(AlbumSchema), lastUpdated: "2026-08-21T14:00Z")]
            public record AlbumDto(int Id, string Title);
            """;

        await VerifyAdd.VerifyAnalyzerAsync(source);
    }

    [TestMethod]
    public async Task BasedOnLinkExposesActions() {
        const string source = """
            [Schema("2026-08-21T14:00Z")]
            public record AlbumSchema(int Id, string Title);

            [{|#0:BasedOn(typeof(AlbumSchema), lastUpdated: "2026-08-21T14:00Z")|}]
            public record AlbumDto(int Id, string Title);
            """;

        var expected = VerifyActions.Diagnostic(BasedOnAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("AlbumSchema");

        await VerifyActions.VerifyAnalyzerAsync(source, expected);
    }

    [TestMethod]
    public async Task UnversionedLinkHasNoAcknowledgementWarning() {
        const string source = """
            [Schema("2026-08-21T14:00Z")]
            public record AlbumSchema(int Id, string Title);

            [{|#0:BasedOn(typeof(AlbumSchema))|}]
            public record AlbumDto(int Id, string Title);
            """;

        await VerifySync.VerifyAnalyzerAsync(source);
    }

    [TestMethod]
    public async Task OlderAcknowledgementIsOutOfDate() {
        const string source = """
            [Schema("2026-08-22T09:30Z")]
            public record AlbumSchema(int Id, string Title, int? ReleaseYear);

            [{|#0:BasedOn(typeof(AlbumSchema), lastUpdated: "2026-08-21T14:00Z")|}]
            public record AlbumDto(int Id, string Title);
            """;

        var expected = VerifySync.Diagnostic(SyncBasedOnAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("AlbumDto", "AlbumSchema");

        await VerifySync.VerifyAnalyzerAsync(source, expected);
    }

    [TestMethod]
    public async Task CurrentAcknowledgementPasses() {
        const string source = """
            [Schema("2026-08-21T14:00Z")]
            public record AlbumSchema(int Id, string Title);

            [BasedOn(typeof(AlbumSchema), lastUpdated: "2026-08-21T14:00Z")]
            public record AlbumDto(int Id, string Title);
            """;

        await VerifySync.VerifyAnalyzerAsync(source);
    }

    [TestMethod]
    public async Task EachBasedOnReferenceIsCheckedIndependently() {
        const string source = """
            [Schema("2026-08-21T14:00Z")]
            public record IdSchema(int Id);

            [Schema("2026-08-22T09:30Z")]
            public record TitleSchema(string Title);

            [BasedOn(typeof(IdSchema), lastUpdated: "2026-08-21T14:00Z")]
            [{|#0:BasedOn(typeof(TitleSchema), lastUpdated: "2026-08-21T14:00Z")|}]
            public record AlbumDto(int Id, string Title);
            """;

        var expected = VerifySync.Diagnostic(SyncBasedOnAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("AlbumDto", "TitleSchema");

        await VerifySync.VerifyAnalyzerAsync(source, expected);
    }
}
