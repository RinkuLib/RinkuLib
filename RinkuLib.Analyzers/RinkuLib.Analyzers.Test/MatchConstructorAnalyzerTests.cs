using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using VerifyMatch = RinkuLib.Analyzers.Test.CSharpAnalyzerVerifier<RinkuLib.Analyzers.MatchConstructorAnalyzer>;

namespace RinkuLib.Analyzers.Test;

[TestClass]
public sealed class MatchConstructorAnalyzerTests {
    [TestMethod]
    public async Task MatchingRecordConstructorPasses() {
        const string source = """
            #nullable enable
            public record CustomerSchema(int Id, string? Name);

            /// <MatchConstructor cref="CustomerSchema" />
            public record CustomerDto(int Id, string? Name);
            """;

        await VerifyMatch.VerifyAnalyzerAsync(source);
    }

    [TestMethod]
    public async Task AnyReferencedTypeConstructorMayMatch() {
        const string source = """
            public class CustomerSchema {
                public CustomerSchema(int id) { }
                public CustomerSchema(int id, string name) { }
            }

            /// <MatchConstructor cref="CustomerSchema" />
            public class CustomerDto {
                public CustomerDto(int id, string name) { }
            }
            """;

        await VerifyMatch.VerifyAnalyzerAsync(source);
    }

    [TestMethod]
    public async Task ImplicitParameterlessConstructorsMatch() {
        const string source = """
            public class CustomerSchema { }

            /// <MatchConstructor cref="CustomerSchema" />
            public class CustomerDto { }
            """;

        await VerifyMatch.VerifyAnalyzerAsync(source);
    }

    [TestMethod]
    public async Task ReferencedMethodParametersMayMatch() {
        const string source = """
            public static class Schemas {
                public static object Create(int id, ref string name, params object[] values) => new();
            }

            /// <MatchConstructor cref="Schemas.Create" />
            public class CustomerDto {
                public CustomerDto(int id, ref string name, params object[] values) { }
            }
            """;

        await VerifyMatch.VerifyAnalyzerAsync(source);
    }

    [TestMethod]
    public async Task ParameterTypeMismatchReports() {
        const string source = """
            public record CustomerSchema(int Id);

            /// {|#0:<MatchConstructor cref="CustomerSchema" />|}
            public record CustomerDto(long Id);
            """;

        var expected = VerifyMatch.Diagnostic(MatchConstructorAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("CustomerDto", "CustomerSchema");
        await VerifyMatch.VerifyAnalyzerAsync(source, expected);
    }

    [TestMethod]
    public async Task ParameterNameAndOrderMismatchReports() {
        const string source = """
            public record CustomerSchema(int Id, string Name);

            /// {|#0:<MatchConstructor cref="CustomerSchema" />|}
            public record CustomerDto(string Name, int CustomerId);
            """;

        var expected = VerifyMatch.Diagnostic(MatchConstructorAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("CustomerDto", "CustomerSchema");
        await VerifyMatch.VerifyAnalyzerAsync(source, expected);
    }

    [TestMethod]
    public async Task NullabilityMismatchReports() {
        const string source = """
            #nullable enable
            public record CustomerSchema(string? Name);

            /// {|#0:<MatchConstructor cref="CustomerSchema" />|}
            public record CustomerDto(string Name);
            """;

        var expected = VerifyMatch.Diagnostic(MatchConstructorAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("CustomerDto", "CustomerSchema");
        await VerifyMatch.VerifyAnalyzerAsync(source, expected);
    }

    [TestMethod]
    public async Task RefKindAndParamsMismatchReports() {
        const string source = """
            public static class Schemas {
                public static object Create(ref int id, params string[] names) => new();
            }

            /// {|#0:<MatchConstructor cref="Schemas.Create" />|}
            public class CustomerDto {
                public CustomerDto(int id, string[] names) { }
            }
            """;

        var expected = VerifyMatch.Diagnostic(MatchConstructorAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("CustomerDto", "object Schemas.Create(ref int id, params string[] names)");
        await VerifyMatch.VerifyAnalyzerAsync(source, expected);
    }

    [TestMethod]
    public async Task AttributesAndDefaultValuesDoNotAffectTheMatch() {
        const string source = """
            using System;

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class SlotAttribute : Attribute { }

            public class CustomerSchema {
                public CustomerSchema([Slot] int id = 1) { }
            }

            /// <MatchConstructor cref="CustomerSchema" />
            public class CustomerDto {
                public CustomerDto(int id = 2) { }
            }
            """;

        await VerifyMatch.VerifyAnalyzerAsync(source);
    }

    [TestMethod]
    public async Task EachReferenceIsCheckedIndependently() {
        const string source = """
            public record FirstSchema(int Id);
            public record SecondSchema(string Name);

            /// <MatchConstructor cref="FirstSchema" />
            /// {|#0:<MatchConstructor cref="SecondSchema" />|}
            public record CustomerDto(int Id);
            """;

        var expected = VerifyMatch.Diagnostic(MatchConstructorAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("CustomerDto", "SecondSchema");
        await VerifyMatch.VerifyAnalyzerAsync(source, expected);
    }
}
