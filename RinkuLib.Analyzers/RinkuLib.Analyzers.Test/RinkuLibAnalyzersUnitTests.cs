using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VerifyCS = RinkuLib.Analyzers.Test.CSharpCodeFixVerifier<
    RinkuLib.Analyzers.SyncBasedOnAnalyzer,
    RinkuLib.Analyzers.BasedOnLastModifiedCodeFixProvider>;

namespace RinkuLib.Analyzers.Test {
    [TestClass]
    public class SyncBasedOnAnalyzerTests {
        private static async Task RunTimeSensitiveCodeFixAsync(string testCode, DiagnosticResult expectedDiagnostic, Func<string, string> generateFixedCode) {
            static string GetTime() => DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mmZ");

            var initialTime = GetTime();
            try {
                await VerifyCS.VerifyCodeFixAsync(testCode, expectedDiagnostic, generateFixedCode(initialTime));
            }
            catch (Exception) {
                var retryTime = GetTime();
                if (initialTime == retryTime)
                    throw;

                await VerifyCS.VerifyCodeFixAsync(testCode, expectedDiagnostic, generateFixedCode(retryTime));
            }
        }

        [TestMethod]
        public async Task When_Dto_Is_Older_Than_Entity_Diagnostic_Is_Thrown_And_Date_Updated() {
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

            static string GenerateFixedCode(string timestamp) => $@"
namespace MyApp.Database
{{
    /// <Schema LastUpdated=""2024-05-10T08:30Z""/>
    public class UserEntity {{ }}
}}

namespace MyApp.Api.Models
{{
    using MyApp.Database;

    /// <BasedOn cref=""UserEntity"" LastUpdated=""{timestamp}""/>
    public class UserDto {{ }}
}}";

            var expected = VerifyCS.Diagnostic("RK0001")
                .WithLocation(0)
                .WithArguments("UserDto", "UserEntity");

            await RunTimeSensitiveCodeFixAsync(testCode, expected, GenerateFixedCode);
        }

        [TestMethod]
        public async Task When_BasedOn_Has_No_Date_It_Defaults_To_Oldest_And_Injects_Current_Date() {
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

            static string GenerateFixedCode(string timestamp) => $@"
namespace MyApp.Database
{{
    /// <Schema LastUpdated=""2024-01-01T00:00Z""/>
    public class OrderEntity {{ }}
}}

namespace MyApp.Api.Models
{{
    using MyApp.Database;

    /// <BasedOn cref=""OrderEntity"" LastUpdated=""{timestamp}""/>
    public class CreateOrderRequest {{ }}
}}";

            var expected = VerifyCS.Diagnostic("RK0001")
                .WithLocation(0)
                .WithArguments("CreateOrderRequest", "OrderEntity");

            await RunTimeSensitiveCodeFixAsync(testCode, expected, GenerateFixedCode);
        }
    }
}