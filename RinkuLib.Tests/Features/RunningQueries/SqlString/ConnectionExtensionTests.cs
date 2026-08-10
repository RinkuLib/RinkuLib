using System.Data;
using System.Data.Common;
using Rinku;
using Rinku.Internal;
using Rinku.Mapping;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// The string-based connection extensions parse the SQL once into a shared cached
/// <see cref="Rinku.QueryCommand"/> and then behave like the command-based API.
/// </summary>
public class ConnectionExtensionTests(SqliteDb Db) : IClassFixture<SqliteDb> {
    private const string AllUsersSql = "SELECT ID, Name, Email FROM Users ORDER BY ID";
    private const string CountSql = "SELECT COUNT(*) FROM Users";
    private const string InsertSql = "INSERT INTO Scratch (Val) VALUES (@Val)";
    private const string ActiveNamesSql = "SELECT Name FROM Users WHERE IsActive = @Active ORDER BY ID";

    [Fact]
    public void Same_sql_reuses_one_cached_command() {
        var first = ConnectionQueryExtensions.GetOrCreateCommand("SELECT 'cache-probe'");
        var second = ConnectionQueryExtensions.GetOrCreateCommand("SELECT 'cache-probe'");
        Assert.Same(first, second);
    }

    [Fact]
    public void Dictionary_removal_does_not_dispose_or_mutate_the_removed_command() {
        string sql = $"SELECT 'remove-{Guid.NewGuid()}'";
        var first = ConnectionQueryExtensions.GetOrCreateCommand(sql);

        Assert.True(ConnectionQueryExtensions.CommandCache.TryRemove(sql, out var removed));
        Assert.Same(first, removed);
        Assert.NotSame(first, ConnectionQueryExtensions.GetOrCreateCommand(sql));
        Assert.Equal(sql, first.QueryText.QueryString);
        ConnectionQueryExtensions.CommandCache.TryRemove(sql, out _);
    }

    [Fact]
    public void Dictionary_can_bind_an_arbitrary_key_to_a_preconfigured_command() {
        string key = $"logical-query-{Guid.NewGuid():N}";
        using var configured = new QueryCommand("SELECT 'configured-command'");
        Assert.True(ConnectionQueryExtensions.CommandCache.TryAdd(key, configured));

        Assert.Same(configured, ConnectionQueryExtensions.GetOrCreateCommand(key));
        Assert.Equal("SELECT 'configured-command'", configured.QueryText.QueryString);

        Assert.True(ConnectionQueryExtensions.CommandCache.TryRemove(key, out var removed));
        Assert.Same(configured, removed);
    }

    [Fact]
    public void Query_maps_from_a_sql_string() {
        using var cnn = Db.GetConnection();
        var user = cnn.Query<UserRow>(AllUsersSql);
        Assert.Equal(new UserRow(1, "John"), user);
    }

    [Fact]
    public async Task QueryAsync_maps_from_a_sql_string() {
        using var cnn = Db.GetConnection();
        var user = await cnn.QueryAsync<UserRow>(AllUsersSql, ct: TestContext.Current.CancellationToken);
        Assert.Equal(new UserRow(1, "John"), user);
    }

    [Fact]
    public void Query_with_parameters_binds_them() {
        using var cnn = Db.GetConnection();
        var names = cnn.Query<List<string>>(ActiveNamesSql, new { Active = true });
        Assert.Equal(["John", "Alice"], names);
    }

    [Fact]
    public void Execute_runs_from_a_sql_string() {
        using var cnn = Db.GetConnection();
        Assert.Equal(1, cnn.Execute(InsertSql, new { Val = 1 }));
    }

    [Fact]
    public async Task ExecuteAsync_runs_from_a_sql_string() {
        using var cnn = Db.GetConnection();
        Assert.Equal(1, await cnn.ExecuteAsync(InsertSql, new { Val = 2 }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ExecuteScalar_reads_the_first_cell() {
        using var cnn = Db.GetConnection();
        Assert.Equal(3, cnn.ExecuteScalar<int>(CountSql));
    }

    [Fact]
    public async Task ExecuteScalarAsync_reads_the_first_cell() {
        using var cnn = Db.GetConnection();
        Assert.Equal(3L, await cnn.ExecuteScalarAsync<long>(CountSql, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StreamQueryAsync_yields_each_row() {
        using var cnn = Db.GetConnection();
        var names = new List<string>();
        await foreach (var name in cnn.StreamQueryAsync<string>(ActiveNamesSql, new { Active = false }, ct: TestContext.Current.CancellationToken))
            names.Add(name);
        Assert.Equal(["Victor"], names);
    }

    [Fact]
    public void ExecuteReader_hands_back_reader_and_command() {
        using var cnn = Db.GetConnection();
        using var reader = cnn.ExecuteReader(AllUsersSql, out var cmd);
        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal("John", reader.GetString(1));
        Assert.True(reader.IsDBNull(2));
        cmd.Dispose();
    }

    [Fact]
    public async Task ExecuteMultiReaderAsync_reads_each_set() {
        using var cnn = Db.Open();
        using var multi = await cnn.ExecuteMultiReaderAsync("SELECT 1; SELECT 2", out var cmd, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await multi.QueryAsync<int>(TestContext.Current.CancellationToken));
        Assert.Equal(2, await multi.QueryAsync<int>(TestContext.Current.CancellationToken));
        cmd.Dispose();
    }

    [Fact]
    public void IDbConnection_extensions_mirror_the_DbConnection_ones() {
        using var raw = Db.GetConnection();
        IDbConnection cnn = raw;
        Assert.Equal(3, cnn.ExecuteScalar<int>(CountSql));
        Assert.Equal(1, cnn.Execute(InsertSql, new { Val = 3 }));
        var user = cnn.Query<UserRow>(AllUsersSql);
        Assert.Equal(new UserRow(1, "John"), user);
    }

    [Fact]
    public async Task IDbConnection_async_extensions_mirror_the_DbConnection_ones() {
        using var raw = Db.GetConnection();
        IDbConnection cnn = raw;
        Assert.Equal(3, await cnn.ExecuteScalarAsync<int>(CountSql, ct: TestContext.Current.CancellationToken));
        Assert.Equal(1, await cnn.ExecuteAsync(InsertSql, new { Val = 4 }, ct: TestContext.Current.CancellationToken));
        var user = await cnn.QueryAsync<UserRow>(AllUsersSql, ct: TestContext.Current.CancellationToken);
        Assert.Equal(new UserRow(1, "John"), user);
    }

    [Fact]
    public void Generic_parameter_overloads_bind_without_boxing() {
        using var cnn = Db.GetConnection();
        Assert.Equal(1, cnn.Execute(InsertSql, new ValHolder(5)));
        var names = cnn.Query<List<string>, ActiveFilter>(ActiveNamesSql, new ActiveFilter(true));
        Assert.Equal(["John", "Alice"], names);
    }

    [Fact]
    public void Ref_parameter_overloads_bind_the_same_way() {
        using var cnn = Db.GetConnection();
        var holder = new ValHolder(6);
        Assert.Equal(1, cnn.Execute(InsertSql, ref holder));
        var filter = new ActiveFilter(false);
        var names = cnn.Query<List<string>, ActiveFilter>(ActiveNamesSql, ref filter);
        Assert.Equal(["Victor"], names);
    }

    [Fact]
    public void Out_command_overloads_expose_the_command() {
        using var cnn = Db.GetConnection();
        var count = cnn.ExecuteScalar<int>(CountSql, out var cmd);
        Assert.Equal(3, count);
        Assert.NotNull(cmd);
        cmd.Dispose();
    }
}
