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

        var generated = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            new QuerySetting
            {
                MethodName = "GetValue",
                Target = "SELECT $value",
                SourceType = QuerySourceType.Text
            },
            new DiscoveredSchema("SELECT $value", [parameter], []));

        Assert.Contains("GetValue(this DbConnection connection, object? value)", generated.CommandCode);
        Assert.Contains("command.Add(\"$value\", (object?)value ?? DBNull.Value);", generated.CommandCode);
        Assert.DoesNotContain("DbType.Object", generated.CommandCode);
        Assert.Contains("Add(this DbCommand command, string name, object value)", generated.SupportCode);
    }

    [Fact]
    public async Task PostgreSqlPositionalParameter_GeneratesValidCSharpName()
    {
        using var temp = new TempDirectory();
        var parameter = new ParameterMetadata("$1", DbType.Int32, false, 0, ParameterDirection.Input, 0, 0, "int", binding: ParameterBinding.Positional);

        var generated = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            new QuerySetting
            {
                MethodName = "GetValue",
                Target = "SELECT $1",
                SourceType = QuerySourceType.Text
            },
            new DiscoveredSchema("SELECT $1", [parameter], []));

        Assert.Contains("GetValue(this DbConnection connection, int p1)", generated.CommandCode);
        Assert.Contains("command.Add(\"\", DbType.Int32, p1);", generated.CommandCode);
    }

    [Fact]
    public async Task SqlContainingDoubleQuote_RemainsValidVerbatimString()
    {
        using var temp = new TempDirectory();

        var generated = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            new QuerySetting
            {
                MethodName = "GetValue",
                Target = "SELECT \"Value\" FROM \"Example\"",
                SourceType = QuerySourceType.Text
            },
            new DiscoveredSchema("SELECT \"Value\" FROM \"Example\"", [], []));

        Assert.Contains("command.CommandText = @\"SELECT \"\"Value\"\" FROM \"\"Example\"\"\";", generated.CommandCode);
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

        var generated = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            new QuerySetting
            {
                MethodName = "SavePayload",
                Target = "SELECT @payload::jsonb",
                SourceType = QuerySourceType.Text
            },
            new DiscoveredSchema("SELECT @payload::jsonb", [parameter], []));

        Assert.Contains("var p_payload = command.Add(\"@payload\", DbType.String, payload);", generated.CommandCode);
        Assert.Contains("p_payload is not Npgsql.NpgsqlParameter npgsql_p_payload", generated.CommandCode);
        Assert.Contains("npgsql_p_payload.DataTypeName = \"jsonb\";", generated.CommandCode);
    }
}
