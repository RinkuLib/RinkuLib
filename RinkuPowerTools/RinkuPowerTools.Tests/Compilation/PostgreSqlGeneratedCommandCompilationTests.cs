using System.Data;
using System.Data.Common;
using System.Reflection;
using Npgsql;
using RinkuPowerTools.Tests.Infrastructure;

namespace RinkuPowerTools.Tests.Compilation;

public class PostgreSqlGeneratedCommandCompilationTests
{
    [Fact]
    public async Task NativePostgreSqlParameter_CompilesAndSetsDataTypeName()
    {
        using var temp = new TempDirectory();
        var parameter = new ParameterMetadata(
            "@payload",
            DbType.String,
            false,
            0,
            ParameterDirection.Input,
            0,
            0,
            "string",
            new ProviderParameterType(DatabaseType.PostgreSql, "jsonb"));

        var generated = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            new QuerySetting
            {
                MethodName = "SavePayload",
                Target = "SELECT @payload::jsonb",
                SourceType = QuerySourceType.Text
            },
            new DiscoveredSchema("SELECT @payload::jsonb", [parameter], []));

        Assembly assembly = GeneratedAssemblyCompiler.Compile(generated.SupportCode, generated.CommandCode);
        Type commandType = assembly.GetType("TestApp.Generated.DbCommands")
            ?? throw new InvalidOperationException("Generated DbCommands type was not found.");
        MethodInfo method = commandType.GetMethod("SavePayload", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Generated SavePayload method was not found.");

        using var connection = new NpgsqlConnection();
        object? commandValue = method.Invoke(null, [connection, "{}"]);
        if (commandValue is not DbCommand command)
            throw new InvalidOperationException("Generated SavePayload method did not return a DbCommand.");

        using (command)
        {
            NpgsqlParameter npgsqlParameter = Assert.IsType<NpgsqlParameter>(Assert.Single(command.Parameters.Cast<DbParameter>()));
            Assert.Equal("jsonb", npgsqlParameter.DataTypeName);
            Assert.Equal(DbType.String, npgsqlParameter.DbType);
        }
    }
}
