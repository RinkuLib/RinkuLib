using System.Data;
using Microsoft.Data.Sqlite;

namespace RinkuPowerTools.Tests.Integration;

public class SqliteSchemaDiscovererTests
{
    [Fact]
    public async Task QueryDiscovery_FindsResultColumnsAndLeavesUnknownParameterUntyped()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);
        await CreateSchemaAsync(connection, cancellationToken);

        var query = new QuerySetting
        {
            MethodName = "GetAlbum",
            Target = "SELECT Id, Title FROM Album WHERE Id = $id",
            SourceType = QuerySourceType.Text
        };

        DiscoveredSchema schema = await SqliteDatabaseProvider.Instance.SchemaDiscoverer.DiscoverSchemaAsync(
            CreateSettings(),
            connection,
            query,
            cancellationToken);

        ParameterMetadata parameter = Assert.Single(schema.Parameters);
        Assert.Equal("$id", parameter.DbName);
        Assert.Null(parameter.DbType);
        Assert.Equal("object?", parameter.CSharpType);

        Assert.Collection(
            schema.ResultColumns,
            id =>
            {
                Assert.Equal("Id", id.DbName);
                Assert.Equal("long", id.CSharpType.TrimEnd('?'));
            },
            title =>
            {
                Assert.Equal("Title", title.DbName);
                Assert.Equal("string", title.CSharpType.TrimEnd('?'));
            });
    }

    [Fact]
    public async Task ParameterOverride_MakesSqliteParameterStronglyTyped()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);
        await CreateSchemaAsync(connection, cancellationToken);

        var query = new QuerySetting
        {
            MethodName = "GetAlbum",
            Target = "SELECT Id, Title FROM Album WHERE Id = $id",
            SourceType = QuerySourceType.Text,
            Parameters =
            [
                new ParameterOverride
                {
                    Name = "id",
                    Type = "integer",
                    IsNullable = false
                }
            ]
        };

        DiscoveredSchema schema = await SqliteDatabaseProvider.Instance.SchemaDiscoverer.DiscoverSchemaAsync(
            CreateSettings(),
            connection,
            query,
            cancellationToken);

        ParameterMetadata parameter = Assert.Single(schema.Parameters);
        Assert.Equal(DbType.Int64, parameter.DbType);
        Assert.Equal("long", parameter.CSharpType);
    }

    [Fact]
    public async Task SchemaDiscovery_DoesNotExecuteReturningMutation()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);
        await CreateSchemaAsync(connection, cancellationToken);

        var query = new QuerySetting
        {
            MethodName = "InsertAlbum",
            Target = "INSERT INTO Album (Title) VALUES ($title) RETURNING Id",
            SourceType = QuerySourceType.Text,
            Parameters =
            [
                new ParameterOverride
                {
                    Name = "title",
                    Type = "text",
                    IsNullable = true
                }
            ]
        };

        DiscoveredSchema schema = await SqliteDatabaseProvider.Instance.SchemaDiscoverer.DiscoverSchemaAsync(
            CreateSettings(),
            connection,
            query,
            cancellationToken);

        Assert.Single(schema.ResultColumns);

        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM Album";
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync(cancellationToken) ?? -1L));
    }

    [Fact]
    public async Task StoredProcedure_IsRejectedBeforeExecution()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var query = new QuerySetting
        {
            MethodName = "NotSupported",
            Target = "Anything",
            SourceType = QuerySourceType.StoredProcedure
        };

        NotSupportedException error = await Assert.ThrowsAsync<NotSupportedException>(
            () => SqliteDatabaseProvider.Instance.SchemaDiscoverer.DiscoverSchemaAsync(
                CreateSettings(),
                connection,
                query,
                cancellationToken));

        Assert.Contains("stored procedures", error.Message.ToLowerInvariant());
    }

    private static ExtensionSettings CreateSettings() => new()
    {
        Database = DatabaseType.Sqlite,
        ConnectionSourceType = ConnectionSourceType.RawConnectionString,
        ConnectionTarget = "Data Source=:memory:",
        ConnectionString = "Data Source=:memory:"
    };

    private static async Task CreateSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Album (Id INTEGER NOT NULL, Title TEXT NULL);";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
