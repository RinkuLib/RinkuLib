using System.Data;
using RinkuPowerTools.Tests.Infrastructure;

namespace RinkuPowerTools.Tests.CodeGeneration;

public class ProviderNeutralParameterGenerationTests
{
    [Fact]
    public async Task UnknownProviderParameter_DoesNotForceDbTypeObject()
    {
        using var temp = new TempDirectory();
        var parameter = new ParameterMetadata("$value", null, true, 0, ParameterDirection.Input, 0, 0, "object");

        var (_, commandCode, supportCode) = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            new QuerySetting
            {
                MethodName = "GetValue",
                Target = "SELECT $value",
                SourceType = QuerySourceType.Text
            },
            new DiscoveredSchema("SELECT $value", [parameter], []));

        Assert.Contains("GetValue(this DbConnection connection, object? value)", commandCode);
        Assert.Contains("command.Add(\"$value\", (object?)value ?? DBNull.Value);", commandCode);
        Assert.DoesNotContain("DbType.Object", commandCode);
        Assert.Contains("Add(this DbCommand command, string name, object value)", supportCode);
    }

    [Fact]
    public async Task PostgreSqlPositionalParameter_GeneratesValidCSharpName()
    {
        using var temp = new TempDirectory();
        var parameter = new ParameterMetadata("$1", DbType.Int32, false, 0, ParameterDirection.Input, 0, 0, "int", binding: ParameterBinding.Positional);

        var (_, commandCode, supportCode) = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            new QuerySetting
            {
                MethodName = "GetValue",
                Target = "SELECT $1",
                SourceType = QuerySourceType.Text
            },
            new DiscoveredSchema("SELECT $1", [parameter], []));

        Assert.Contains("GetValue(this DbConnection connection, int p1)", commandCode);
        Assert.Contains("command.Add(\"\", DbType.Int32, (object)p1);", commandCode);
        GeneratedAssemblyCompiler.Compile(commandCode, supportCode);
    }

    [Fact]
    public async Task SqlContainingDoubleQuote_RemainsValidVerbatimString()
    {
        using var temp = new TempDirectory();

        var (_, commandCode, _) = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            new QuerySetting
            {
                MethodName = "GetValue",
                Target = "SELECT \"Value\" FROM \"Example\"",
                SourceType = QuerySourceType.Text
            },
            new DiscoveredSchema("SELECT \"Value\" FROM \"Example\"", [], []));

        Assert.Contains("command.CommandText = @\"SELECT \"\"Value\"\" FROM \"\"Example\"\"\";", commandCode);
    }

    [Fact]
    public async Task PostgreSqlNativeType_IsAppliedWithoutReplacingSharedParameterGeneration()
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

        var (_, commandCode, _) = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            new QuerySetting
            {
                MethodName = "SavePayload",
                Target = "SELECT @payload::jsonb",
                SourceType = QuerySourceType.Text
            },
            new DiscoveredSchema("SELECT @payload::jsonb", [parameter], []));

        Assert.Contains("var p_payload = command.Add(\"@payload\", DbType.String, payload);", commandCode);
        Assert.Contains("p_payload is not Npgsql.NpgsqlParameter npgsql_p_payload", commandCode);
        Assert.Contains("npgsql_p_payload.DataTypeName = \"jsonb\";", commandCode);
    }
}
