using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace RinkuPowerTools.Tests.Infrastructure;

internal static class GeneratedAssemblyCompiler
{
    public static Assembly Compile(params string[] sources)
    {
        var syntaxTrees = new SyntaxTree[sources.Length];
        for (int i = 0; i < sources.Length; i++)
            syntaxTrees[i] = CSharpSyntaxTree.ParseText(sources[i]);

        string trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES was unavailable.");

        var paths = new HashSet<string>(
            trustedPlatformAssemblies.Split(System.IO.Path.PathSeparator),
            StringComparer.OrdinalIgnoreCase)
        {
            typeof(Npgsql.NpgsqlParameter).Assembly.Location,
            typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly.Location,
            typeof(Microsoft.Data.Sqlite.SqliteConnection).Assembly.Location
        };

        var references = new MetadataReference[paths.Count];
        int referenceIndex = 0;
        foreach (string path in paths)
            references[referenceIndex++] = MetadataReference.CreateFromFile(path);

        CSharpCompilation compilation = CSharpCompilation.Create(
            "RinkuPowerToolsGeneratedTests_" + Guid.NewGuid().ToString("N"),
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var pe = new MemoryStream();
        EmitResult result = compilation.Emit(pe);
        if (!result.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));

        return Assembly.Load(pe.ToArray());
    }
}
