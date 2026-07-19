using System.Data;
using System.Data.Common;
using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// Every execution verb is reachable from both builder forms, over both connection interfaces,
/// in both sync and async form.
/// </summary>
public class BuilderVerbTests(SqliteDb Db) : IClassFixture<SqliteDb> {
    private static readonly QueryCommand Count = new("SELECT COUNT(*) FROM Users WHERE IsActive = @Active");
    private static readonly QueryCommand Insert = new("INSERT INTO Scratch (Val) VALUES (@Val)");
    private static readonly QueryCommand TwoSets = new("SELECT 1; SELECT 2");
    private static readonly QueryCommand Names = new("SELECT Name FROM Users WHERE IsActive = @Active ORDER BY ID");

    private QueryBuilder ActiveBuilder(QueryCommand query, object value) {
        var builder = query.StartBuilder();
        builder.Use("@Active", value);
        return builder;
    }

    [Fact]
    public void Builder_Execute_on_DbConnection() {
        using var cnn = Db.GetConnection();
        var builder = Insert.StartBuilder();
        builder.Use("@Val", 1);
        Assert.Equal(1, builder.Execute(cnn));
    }

    [Fact]
    public void Builder_Execute_on_IDbConnection() {
        using var cnn = Db.GetConnection();
        var builder = Insert.StartBuilder();
        builder.Use("@Val", 2);
        Assert.Equal(1, builder.Execute((IDbConnection)cnn));
    }

    [Fact]
    public async Task Builder_ExecuteAsync_on_both_interfaces() {
        using var cnn = Db.GetConnection();
        var builder = Insert.StartBuilder();
        builder.Use("@Val", 3);
        Assert.Equal(1, await builder.ExecuteAsync(cnn, ct: TestContext.Current.CancellationToken));
        Assert.Equal(1, await builder.ExecuteAsync((IDbConnection)cnn, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Builder_ExecuteScalar_on_both_interfaces() {
        using var cnn = Db.GetConnection();
        var builder = ActiveBuilder(Count, 1);
        Assert.Equal(2, builder.ExecuteScalar<int>(cnn));
        Assert.Equal(2, builder.ExecuteScalar<int>((IDbConnection)cnn));
    }

    [Fact]
    public async Task Builder_ExecuteScalarAsync_on_both_interfaces() {
        using var cnn = Db.GetConnection();
        var builder = ActiveBuilder(Count, 0);
        Assert.Equal(1, await builder.ExecuteScalarAsync<int>(cnn, ct: TestContext.Current.CancellationToken));
        Assert.Equal(1, await builder.ExecuteScalarAsync<int>((IDbConnection)cnn, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Builder_ExecuteReader_on_both_interfaces() {
        using var cnn = Db.GetConnection();
        var builder = ActiveBuilder(Names, 1);
        using (var reader = builder.ExecuteReader(cnn, out var cmd)) {
            Assert.True(reader.Read());
            cmd.Dispose();
        }
        using (var reader = builder.ExecuteReader((IDbConnection)cnn, out var icmd)) {
            Assert.True(reader.Read());
            icmd.Dispose();
        }
    }

    [Fact]
    public async Task Builder_ExecuteReaderAsync_on_both_interfaces() {
        using var cnn = Db.GetConnection();
        var builder = ActiveBuilder(Names, 1);
        using (var reader = await builder.ExecuteReaderAsync(cnn, out var cmd, ct: TestContext.Current.CancellationToken)) {
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            cmd.Dispose();
        }
        using (var reader = await builder.ExecuteReaderAsync((IDbConnection)cnn, out var icmd, ct: TestContext.Current.CancellationToken)) {
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            icmd.Dispose();
        }
    }

    [Fact]
    public void Builder_ExecuteMultiReader_reads_each_set() {
        using var cnn = Db.Open();
        var builder = TwoSets.StartBuilder();
        using var multi = builder.ExecuteMultiReader(cnn, out var cmd);
        Assert.Equal(1, multi.Query<int>());
        Assert.Equal(2, multi.Query<int>());
        cmd.Dispose();
    }

    [Fact]
    public async Task Builder_ExecuteMultiReaderAsync_reads_each_set() {
        using var cnn = Db.Open();
        var builder = TwoSets.StartBuilder();
        using var multi = await builder.ExecuteMultiReaderAsync(cnn, out var cmd, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await multi.QueryAsync<int>(TestContext.Current.CancellationToken));
        Assert.Equal(2, await multi.QueryAsync<int>(TestContext.Current.CancellationToken));
        cmd.Dispose();
    }

    [Fact]
    public async Task Builder_ExecuteMultiReader_on_IDbConnection() {
        using var cnn = Db.Open();
        var builder = TwoSets.StartBuilder();
        using (var multi = builder.ExecuteMultiReader((IDbConnection)cnn, out var icmd)) {
            Assert.Equal(1, multi.Query<int>());
            icmd.Dispose();
        }
        using (var multi = await builder.ExecuteMultiReaderAsync((IDbConnection)cnn, out var icmd2, ct: TestContext.Current.CancellationToken)) {
            Assert.Equal(1, await multi.QueryAsync<int>(TestContext.Current.CancellationToken));
            icmd2.Dispose();
        }
    }

    [Fact]
    public void Builder_Query_on_both_interfaces() {
        using var cnn = Db.GetConnection();
        var builder = ActiveBuilder(Names, 1);
        Assert.Equal("John", builder.Query<string>(cnn));
        Assert.Equal("John", builder.Query<string>((IDbConnection)cnn));
    }

    [Fact]
    public async Task Builder_QueryAsync_on_both_interfaces() {
        using var cnn = Db.GetConnection();
        var builder = ActiveBuilder(Names, 0);
        Assert.Equal("Victor", await builder.QueryAsync<string>(cnn, ct: TestContext.Current.CancellationToken));
        Assert.Equal("Victor", await builder.QueryAsync<string>((IDbConnection)cnn, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Builder_StreamQueryAsync_yields_rows() {
        using var cnn = Db.GetConnection();
        var builder = ActiveBuilder(Names, 1);
        var names = new List<string>();
        await foreach (var name in builder.StreamQueryAsync<string>(cnn, ct: TestContext.Current.CancellationToken))
            names.Add(name);
        Assert.Equal(["John", "Alice"], names);
    }

    private (QueryBuilderCommand<DbCommand> Builder, SqliteDbCleanup Cleanup) BoundBuilder(QueryCommand query) {
        var cnn = Db.GetConnection();
        var cmd = cnn.CreateCommand();
        return (query.StartBuilder((DbCommand)cmd), new SqliteDbCleanup(cnn, cmd));
    }

    private sealed class SqliteDbCleanup(IDisposable a, IDisposable b) : IDisposable {
        public void Dispose() {
            b.Dispose();
            a.Dispose();
        }
    }

    [Fact]
    public void Bound_builder_runs_every_sync_verb() {
        var (builder, cleanup) = BoundBuilder(Count);
        using (cleanup) {
            builder.Use("@Active", 1);
            Assert.Equal(2, builder.ExecuteScalar<int>());
            Assert.Equal(2, builder.Query<int>());
            using var reader = builder.ExecuteReader();
            Assert.True(reader.Read());
        }
    }

    [Fact]
    public async Task Bound_builder_runs_every_async_verb() {
        var (builder, cleanup) = BoundBuilder(Count);
        using (cleanup) {
            builder.Use("@Active", 0);
            Assert.Equal(1, await builder.ExecuteScalarAsync<int>(TestContext.Current.CancellationToken));
            Assert.Equal(1, await builder.QueryAsync<int>(TestContext.Current.CancellationToken));
            using var reader = await builder.ExecuteReaderAsync(ct: TestContext.Current.CancellationToken);
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Bound_builder_streams_rows() {
        var (builder, cleanup) = BoundBuilder(Names);
        using (cleanup) {
            builder.Use("@Active", 1);
            var names = new List<string>();
            await foreach (var name in builder.StreamQueryAsync<string>(TestContext.Current.CancellationToken))
                names.Add(name);
            Assert.Equal(["John", "Alice"], names);
        }
    }

    [Fact]
    public async Task Bound_builder_multi_reader_reads_each_set() {
        var (builder, cleanup) = BoundBuilder(TwoSets);
        using (cleanup) {
            using (var multi = builder.ExecuteMultiReader()) {
                Assert.Equal(1, multi.Query<int>());
                Assert.Equal(2, multi.Query<int>());
            }
            using (var multi = await builder.ExecuteMultiReaderAsync(ct: TestContext.Current.CancellationToken)) {
                Assert.Equal(1, await multi.QueryAsync<int>(TestContext.Current.CancellationToken));
            }
        }
    }

    [Fact]
    public async Task Bound_interface_builder_runs_the_core_verbs() {
        using var cnn = Db.GetConnection();
        using var cmd = cnn.CreateCommand();
        var builder = Insert.StartBuilder((IDbCommand)cmd);
        builder.Use("@Val", 4);
        Assert.Equal(1, builder.Execute());
        builder.Use("@Val", 5);
        Assert.Equal(1, await builder.ExecuteAsync(TestContext.Current.CancellationToken));

        using var cmd2 = cnn.CreateCommand();
        var countBuilder = Count.StartBuilder((IDbCommand)cmd2);
        countBuilder.Use("@Active", 1);
        Assert.Equal(2, countBuilder.ExecuteScalar<int>());
        Assert.Equal(2, await countBuilder.ExecuteScalarAsync<int>(TestContext.Current.CancellationToken));
        Assert.Equal(2, countBuilder.Query<int>());
        Assert.Equal(2, await countBuilder.QueryAsync<int>(TestContext.Current.CancellationToken));
        using (var reader = countBuilder.ExecuteReader())
            Assert.True(reader.Read());
        using (var reader = await countBuilder.ExecuteReaderAsync(ct: TestContext.Current.CancellationToken))
            Assert.True(reader.Read());
    }

    [Fact]
    public async Task Bound_interface_builder_multi_reads() {
        using var cnn = Db.GetConnection();
        using var cmd2 = cnn.CreateCommand();
        var multiBuilder = TwoSets.StartBuilder((IDbCommand)cmd2);
        using (var multi = multiBuilder.ExecuteMultiReader()) {
            Assert.Equal(1, multi.Query<int>());
        }
        using (var multi = await multiBuilder.ExecuteMultiReaderAsync(ct: TestContext.Current.CancellationToken)) {
            Assert.Equal(1, await multi.QueryAsync<int>(TestContext.Current.CancellationToken));
        }
    }

    /// <summary>
    /// A run reuses the parser it learned last time while the command is still settling its parameter
    /// metadata, so the second pass takes the road that has the parser in hand but still lets the command
    /// finish learning. A spread keeps the command asking, which holds that road open past the first run.
    /// </summary>
    [Fact]
    public async Task Builder_QueryAsync_reuses_the_parser_while_the_command_still_learns() {
        var query = new QueryCommand("SELECT Name FROM Users WHERE ID IN (?@ids_X) ORDER BY ID");
        using var cnn = Db.GetConnection();

        var first = query.StartBuilder();
        first.Use("@ids", new[] { 1, 2 });
        Assert.Equal("John", await first.QueryAsync<string>(cnn, ct: TestContext.Current.CancellationToken));

        var second = query.StartBuilder();
        second.Use("@ids", new[] { 1, 2 });
        Assert.Equal("John", await second.QueryAsync<string>(cnn, ct: TestContext.Current.CancellationToken));
    }
}
