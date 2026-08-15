using System.Data.Common;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace RinkuLib.Testing;

public sealed class SqlServerFixture : IAsyncDisposable {
    private readonly MsSqlContainer Container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    public string ConnectionString { get; private set; } = string.Empty;

    public SqlConnection GetConnection() => new(ConnectionString);

    public async Task InitializeAsync() {
        await Container.StartAsync();
        ConnectionString = Container.GetConnectionString();
        await WaitForSystemDatabasesAsync();
    }

    private async Task WaitForSystemDatabasesAsync() {
        for (int attempt = 0; attempt < 60; attempt++) {
            try {
                await using var cnn = GetConnection();
                await cnn.OpenAsync();
                await using var cmd = cnn.CreateCommand();
                cmd.CommandText = "SELECT state FROM sys.databases WHERE name = N'tempdb'";
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 0)
                    return;
            }
            catch (DbException) when (attempt < 59) { }
            catch (InvalidOperationException) when (attempt < 59) { }
            await Task.Delay(500);
        }
        throw new InvalidOperationException("SQL Server did not make tempdb available during fixture startup.");
    }

    public async ValueTask DisposeAsync() => await Container.DisposeAsync();
}
