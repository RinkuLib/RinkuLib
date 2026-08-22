using System.Data;
using System.Data.Common;

namespace RinkuPowerTools.Tests.Configuration;

public class ParameterOverrideTests
{
    [Fact]
    public void ApplyUserOverrides_ChangesTypeAndNullability()
    {
        var parameters = new List<ParameterMetadata>
        {
            new("@amount", DbType.Int32, false, 0, ParameterDirection.Input, 0, 0)
        };
        var overrides = new List<ParameterOverride>
        {
            new()
            {
                Name = "@AMOUNT",
                Type = "decimal(18,2)",
                IsNullable = true
            }
        };

        List<ParameterMetadata> result = OverrideProbe.Apply(parameters, overrides, SqlServerTypeParser.Parse);

        Assert.Same(parameters, result);
        Assert.Equal(DbType.Decimal, result[0].DbType);
        Assert.Equal("decimal?", result[0].CSharpType);
        Assert.True(result[0].IsNullable);
        Assert.Equal((byte)18, result[0].Precision);
        Assert.Equal((byte)2, result[0].Scale);
    }

    [Fact]
    public void ApplyUserOverrides_MatchesParameterWithoutPrefix()
    {
        var parameters = new List<ParameterMetadata>
        {
            new("$artistId", null, true, 0, ParameterDirection.Input, 0, 0, "object")
        };
        var overrides = new List<ParameterOverride>
        {
            new()
            {
                Name = "artistId",
                Type = "integer",
                IsNullable = false
            }
        };

        List<ParameterMetadata> result = OverrideProbe.Apply(parameters, overrides, PostgreSqlTypeParser.Parse);

        Assert.Equal(DbType.Int32, result[0].DbType);
        Assert.Equal("int", result[0].CSharpType);
    }

    [Fact]
    public void ApplyUserOverrides_RejectsUnknownParameter()
    {
        var parameters = new List<ParameterMetadata>
        {
            new("@amount", DbType.Int32, false, 0, ParameterDirection.Input, 0, 0)
        };
        var overrides = new List<ParameterOverride>
        {
            new()
            {
                Name = "@missing",
                Type = "int"
            }
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => OverrideProbe.Apply(parameters, overrides, SqlServerTypeParser.Parse));

        Assert.Contains("@missing", error.Message);
    }

    private sealed class OverrideProbe : SchemaDiscoverer
    {
        public static List<ParameterMetadata> Apply(
            List<ParameterMetadata> parameters,
            List<ParameterOverride> overrides,
            Func<string, ProviderTypeInfo> parser) =>
            ApplyUserOverrides(parameters, overrides, parser);

        public override Task<DiscoveredSchema> DiscoverSchemaAsync(
            ExtensionSettings settings,
            DbConnection cnn,
            QuerySetting query,
            CancellationToken ct) => throw new NotSupportedException();
    }
}
