using System.Text.Json;

namespace RinkuPowerTools.Tests.Configuration;

public class ExtensionSettingsDatabaseSerializationTests
{
    [Fact]
    public void MissingDatabase_MeansAutoDetection()
    {
        ExtensionSettings? settings = JsonSerializer.Deserialize<ExtensionSettings>(
            """{"RawConnectionString":"Data Source=:memory:"}""");

        Assert.NotNull(settings);
        Assert.Null(settings.Database);
    }

    [Theory]
    [InlineData("Postgres")]
    [InlineData("PostgreSQL")]
    [InlineData("PostgreSql")]
    public void PostgreSqlAliases_AreAcceptedInConfiguration(string databaseName)
    {
        ExtensionSettings? settings = JsonSerializer.Deserialize<ExtensionSettings>(
            $$"""{"RawConnectionString":"Host=localhost;Database=rinku;Username=rinku","Database":"{{databaseName}}"}""");

        Assert.NotNull(settings);
        Assert.Equal(DatabaseType.PostgreSql, settings.Database);
    }

    [Fact]
    public void ExplicitDatabase_RoundTrips()
    {
        var settings = new ExtensionSettings
        {
            Database = DatabaseType.Sqlite,
            ConnectionSourceType = ConnectionSourceType.RawConnectionString,
            ConnectionTarget = "Data Source=:memory:"
        };

        string json = JsonSerializer.Serialize(settings);
        ExtensionSettings? roundTrip = JsonSerializer.Deserialize<ExtensionSettings>(json);

        Assert.Contains("\"Database\":\"Sqlite\"", json);
        Assert.NotNull(roundTrip);
        Assert.Equal(DatabaseType.Sqlite, roundTrip.Database);
    }
}
