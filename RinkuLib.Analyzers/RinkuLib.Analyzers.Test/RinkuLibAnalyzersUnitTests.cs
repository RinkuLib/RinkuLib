using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using VerifySync = RinkuLib.Analyzers.Test.CSharpAnalyzerVerifier<
    RinkuLib.Analyzers.SyncBasedOnAnalyzer>;

using VerifyFix = RinkuLib.Analyzers.Test.CSharpCodeFixVerifier<
    RinkuLib.Analyzers.BasedOnAnalyzer,
    RinkuLib.Analyzers.BasedOnLastModifiedCodeFixProvider>;

namespace RinkuLib.Analyzers.Test {

    [TestClass]
    public class SyncBasedOnAnalyzerTests {

        [TestMethod]
        public async Task When_Dto_Is_Older_Than_Entity_Warning_Is_Reported() {
            var testCode = @"
namespace MyApp.Database
{
    /// <Schema LastUpdated=""2024-05-10T08:30Z""/>
    public class UserEntity { }
}

namespace MyApp.Api.Models
{
    using MyApp.Database;

    /// <BasedOn cref=""UserEntity"" LastUpdated=""2023-11-01T14:15Z""/>
    public class {|#0:UserDto|} { }
}";

            var expected = VerifySync.Diagnostic(SyncBasedOnAnalyzer.DiagnosticId)
                .WithLocation(0)
                .WithArguments("UserDto", "UserEntity");

            await VerifySync.VerifyAnalyzerAsync(testCode, expected);
        }

        [TestMethod]
        public async Task When_BasedOn_Has_No_Date_Warning_Is_Reported() {
            var testCode = @"
namespace MyApp.Database
{
    /// <Schema LastUpdated=""2024-01-01T00:00Z""/>
    public class OrderEntity { }
}

namespace MyApp.Api.Models
{
    using MyApp.Database;

    /// <BasedOn cref=""OrderEntity""/>
    public class {|#0:CreateOrderRequest|} { }
}";

            var expected = VerifySync.Diagnostic(SyncBasedOnAnalyzer.DiagnosticId)
                .WithLocation(0)
                .WithArguments("CreateOrderRequest", "OrderEntity");

            await VerifySync.VerifyAnalyzerAsync(testCode, expected);
        }
    }

    [TestClass]
    public class BasedOnCodeFixTests {
        private static async Task RunTimeSensitiveCodeFixAsync(string testCode, DiagnosticResult expectedDiagnostic, Func<string, string> generateFixedCode) {
            static string GetTime() => DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mmZ");

            var initialTime = GetTime();

            try {
                await RunCodeFixAsync(testCode, expectedDiagnostic, generateFixedCode(initialTime));
            }
            catch (Exception) {
                var retryTime = GetTime();

                if (initialTime == retryTime)
                    throw;

                await RunCodeFixAsync(testCode, expectedDiagnostic, generateFixedCode(retryTime));
            }
        }

        // RK0000 is the always-on codegen anchor, so it legitimately remains after the fix;
        // declare it in the fixed state. Negative iteration counts are upper bounds: inserting the
        // attribute shifts the diagnostic's span, which the framework counts as a second pass.
        private static async Task RunCodeFixAsync(string testCode, DiagnosticResult expectedDiagnostic, string fixedCode) {
            var test = new VerifyFix.Test {
                TestCode = testCode,
                FixedCode = fixedCode,
                NumberOfIncrementalIterations = -2,
                NumberOfFixAllIterations = -2,
            };
            test.ExpectedDiagnostics.Add(expectedDiagnostic);
            test.FixedState.ExpectedDiagnostics.Add(expectedDiagnostic);
            await test.RunAsync();
        }

        [TestMethod]
        public async Task CodeFix_Updates_Existing_LastUpdated() {
            var testCode = @"
namespace MyApp.Database
{
    public class UserEntity { }
}

namespace MyApp.Api.Models
{
    using MyApp.Database;

    /// <BasedOn cref=""UserEntity"" LastUpdated=""2023-11-01T14:15Z""/>
    public class {|#0:UserDto|} { }
}";

            static string GenerateFixedCode(string timestamp) => $@"
namespace MyApp.Database
{{
    public class UserEntity {{ }}
}}

namespace MyApp.Api.Models
{{
    using MyApp.Database;

    /// <BasedOn cref=""UserEntity"" LastUpdated=""{timestamp}""/>
    public class {{|#0:UserDto|}} {{ }}
}}";

            var expected = VerifyFix.Diagnostic(BasedOnAnalyzer.DiagnosticId)
                .WithLocation(0)
                .WithArguments("UserEntity");

            await RunTimeSensitiveCodeFixAsync(testCode, expected, GenerateFixedCode);
        }

        [TestMethod]
        public async Task CodeFix_Inserts_Missing_LastUpdated() {
            var testCode = @"
namespace MyApp.Database
{
    public class OrderEntity { }
}

namespace MyApp.Api.Models
{
    using MyApp.Database;

    /// <BasedOn cref=""OrderEntity""/>
    public class {|#0:CreateOrderRequest|} { }
}";

            static string GenerateFixedCode(string timestamp) => $@"
namespace MyApp.Database
{{
    public class OrderEntity {{ }}
}}

namespace MyApp.Api.Models
{{
    using MyApp.Database;

    /// <BasedOn cref=""OrderEntity"" LastUpdated=""{timestamp}""/>
    public class {{|#0:CreateOrderRequest|}} {{ }}
}}";

            var expected = VerifyFix.Diagnostic(BasedOnAnalyzer.DiagnosticId)
                .WithLocation(0)
                .WithArguments("OrderEntity");

            await RunTimeSensitiveCodeFixAsync(testCode, expected, GenerateFixedCode);
        }
    }
}