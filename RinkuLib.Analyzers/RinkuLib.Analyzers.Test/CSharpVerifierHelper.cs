using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RinkuLib.Analyzers.Test {
    internal static class CSharpVerifierHelper {
        internal const string IsExternalInit = "namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }";
        internal const string SchemaAttributes = """
            #nullable enable
            global using Rinku.Mapping;
            namespace Rinku.Mapping {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Method | System.AttributeTargets.Constructor)]
                public sealed class SchemaAttribute(string? lastUpdated = null) : System.Attribute { public System.DateTime? LastUpdated { get; } = lastUpdated is null ? null : System.DateTime.Parse(lastUpdated, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal); }
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, AllowMultiple = true)]
                public sealed class BasedOnAttribute : System.Attribute {
                    public BasedOnAttribute(System.Type declaringType, string? lastUpdated = null, string? methodName = null) { LastUpdated = lastUpdated is null ? null : System.DateTime.Parse(lastUpdated, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal); }
                    public System.DateTime? LastUpdated { get; }
                }
            }
            """;

        internal static ImmutableDictionary<string, ReportDiagnostic> NullableWarnings { get; } = GetNullableWarningsFromCompiler();

        private static ImmutableDictionary<string, ReportDiagnostic> GetNullableWarningsFromCompiler() {
            string[] args = ["/warnaserror:nullable"];
            var commandLineArguments = CSharpCommandLineParser.Default.Parse(args, baseDirectory: Environment.CurrentDirectory, sdkDirectory: Environment.CurrentDirectory);
            var nullableWarnings = commandLineArguments.CompilationOptions.SpecificDiagnosticOptions;

            nullableWarnings = nullableWarnings
                .SetItem("CS8632", ReportDiagnostic.Error)
                .SetItem("CS8669", ReportDiagnostic.Error);

            return nullableWarnings;
        }
    }
}
