using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using VerifyInvocation = RinkuLib.Analyzers.Test.CSharpAnalyzerVerifier<RinkuLib.Analyzers.MethodInvocationCompletionAnalyzer>;

namespace RinkuLib.Analyzers.Test;

[TestClass]
public sealed class MethodInvocationAnalyzerTests {
    [TestMethod]
    public async Task UninvokedMethodReferenceReports() {
        const string source = """
            class Commands {
                int Save() => 1;
                object Build() => {|#0:Save|};
            }
            """;

        var expected = VerifyInvocation.Diagnostic(MethodInvocationCompletionAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("Save");
        var test = new VerifyInvocation.Test {
            TestCode = source,
            CompilerDiagnostics = CompilerDiagnostics.None
        };
        test.ExpectedDiagnostics.Add(expected);
        await test.RunAsync();
    }

    [TestMethod]
    public async Task InvocationsNameofAndDelegateConversionsStayQuiet() {
        const string source = """
            using System;

            class Commands {
                int Save() => 1;

                void Build() {
                    _ = Save();
                    _ = nameof(Save);
                    Func<int> callback = Save;
                    var inferred = Save;
                    _ = callback;
                    _ = inferred;
                }
            }
            """;

        await VerifyInvocation.VerifyAnalyzerAsync(source);
    }

    [TestMethod]
    public async Task MemberAccessReportsOnce() {
        const string source = """
            class Service { public int Save() => 1; }

            class Commands {
                object Build(Service service) => {|#0:service.Save|};
            }
            """;

        var expected = VerifyInvocation.Diagnostic(MethodInvocationCompletionAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("Save");
        var test = new VerifyInvocation.Test {
            TestCode = source,
            CompilerDiagnostics = CompilerDiagnostics.None
        };
        test.ExpectedDiagnostics.Add(expected);
        await test.RunAsync();
    }
}
