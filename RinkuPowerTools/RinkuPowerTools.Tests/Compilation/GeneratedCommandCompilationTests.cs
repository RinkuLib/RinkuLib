using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;
using Microsoft.Data.SqlClient;
using RinkuPowerTools.Tests.Infrastructure;

namespace RinkuPowerTools.Tests.Compilation;

public class GeneratedCommandCompilationTests
{
    [Fact]
    public async Task SqlFileCommand_CompilesWithSupportFileAndUsesRuntimeOverride()
    {
        using var temp = new TempDirectory();
        const string key = "Sql/GetAlbums.sql";
        var generated = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            new QuerySetting
            {
                MethodName = "GetAlbums",
                Target = key,
                SourceType = QuerySourceType.FromFile
            },
            new DiscoveredSchema("SELECT AlbumId, Title FROM albums", [], []));

        Assembly assembly = GeneratedAssemblyCompiler.Compile(generated.SupportCode, generated.CommandCode);
        Type supportType = assembly.GetType("TestApp.RinkuPowerTools")
            ?? throw new InvalidOperationException("Generated RinkuPowerTools type was not found.");
        object? fieldValue = supportType.GetField("SqlFiles", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (fieldValue is not ConcurrentDictionary<string, string> sqlFiles)
            throw new InvalidOperationException("Generated SqlFiles dictionary was not found.");
        sqlFiles[key] = "SELECT runtime";

        Type commandType = assembly.GetType("TestApp.Generated.DbCommands")
            ?? throw new InvalidOperationException("Generated DbCommands type was not found.");
        MethodInfo method = commandType.GetMethod("GetAlbums", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Generated GetAlbums method was not found.");
        using var connection = new SqlConnection();
        object? commandValue = method.Invoke(null, [connection]);
        if (commandValue is not DbCommand command)
            throw new InvalidOperationException("Generated GetAlbums method did not return a DbCommand.");
        using (command)
            Assert.Equal("SELECT runtime", command.CommandText);
    }
}
