using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace RinkuPowerTools.Tests.Database;

public class DatabaseProviderResolutionTests
{
    [Fact]
    public void SqlServer_IsInferredFromStrongSqlServerKeys()
    {
        DatabaseProvider provider = DatabaseProviders.Resolve(
            "Server=localhost;Initial Catalog=Rinku;Integrated Security=true;TrustServerCertificate=true");

        Assert.Equal(DatabaseType.SqlServer, provider.Type);
        Assert.IsType<SqlConnection>(provider.CreateConnection("Server=localhost;Initial Catalog=Rinku;Integrated Security=true;TrustServerCertificate=true"));
    }

    [Fact]
    public void PostgreSql_IsInferredFromHostAndUsername()
    {
        const string connectionString = "Host=localhost;Database=rinku;Username=rinku;Password=test";

        DatabaseProvider provider = DatabaseProviders.Resolve(connectionString);

        Assert.Equal(DatabaseType.PostgreSql, provider.Type);
        Assert.IsType<NpgsqlConnection>(provider.CreateConnection(connectionString));
    }

    [Theory]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=Data/rinku.db")]
    [InlineData("Data Source=Data/rinku.sqlite")]
    public void Sqlite_IsInferredFromSqliteSpecificDataSource(string connectionString)
    {
        DatabaseProvider provider = DatabaseProviders.Resolve(connectionString);

        Assert.Equal(DatabaseType.Sqlite, provider.Type);
        Assert.IsType<SqliteConnection>(provider.CreateConnection(connectionString));
    }

    [Fact]
    public void AmbiguousDataSource_RequiresExplicitDatabase()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => DatabaseProviders.Resolve("Data Source=mydatabase"));

        Assert.Contains("ambiguous", error.Message.ToLowerInvariant());
    }

    [Fact]
    public void ExplicitDatabase_WinsOverInference()
    {
        DatabaseProvider provider = DatabaseProviders.Resolve(
            "Data Source=mydatabase",
            DatabaseType.Sqlite);

        Assert.Equal(DatabaseType.Sqlite, provider.Type);
    }

    [Theory]
    [InlineData("Postgres")]
    [InlineData("PostgreSQL")]
    [InlineData("PostgreSql")]
    public void PostgreSqlAliases_AreAccepted(string value)
    {
        Assert.True(DatabaseProviders.TryParseType(value, out DatabaseType type));
        Assert.Equal(DatabaseType.PostgreSql, type);
    }
}
