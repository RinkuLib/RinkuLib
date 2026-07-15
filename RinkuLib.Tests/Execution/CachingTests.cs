using System.Data.Common;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// A command learns its row parser per condition shape and reuses it, so runs that render different
/// SQL never poison each other's cache.
/// </summary>
public class CachingTests(SqliteDb Db) : IClassFixture<SqliteDb> {
    [Fact]
    public void Each_condition_shape_gets_its_own_parser() {
        var query = new QueryCommand("SELECT ID, /*Name*/Name FROM Users WHERE Name = ?@Name");
        using var cnn = Db.GetConnection();

        var oneColumn = query.StartBuilder();
        var row1 = oneColumn.Query<DynaObject>(cnn);
        Assert.NotNull(row1);
        Assert.Single(row1);

        var twoColumns = query.StartBuilder();
        twoColumns.Use("Name");
        twoColumns.Use("@Name", "Victor");
        var row2 = twoColumns.Query<DynaObject>(cnn);
        Assert.NotNull(row2);
        Assert.Equal(2, row2.Count);

        var filteredOneColumn = query.StartBuilder();
        filteredOneColumn.Use("@Name", "Victor");
        var row3 = filteredOneColumn.Query<DynaObject>(cnn);
        Assert.NotNull(row3);
        Assert.Single(row3);

        var twoColumnsNoFilter = query.StartBuilder();
        twoColumnsNoFilter.Use("Name");
        var row4 = twoColumnsNoFilter.Query<DynaObject>(cnn);
        Assert.NotNull(row4);
        Assert.Equal(2, row4.Count);
    }

    [Fact]
    public void Parser_cache_fills_after_the_first_run() {
        var query = new QueryCommand("SELECT ID, Name FROM Users WHERE ID = @ID");
        using var cnn = Db.GetConnection();

        Span<bool> usage = stackalloc bool[query.Mapper.Count];
        usage[0] = true;
        Assert.False(query.TryGetCachedParser<UserRow>(usage, out _));

        query.Query<UserRow>(cnn, new { ID = 1 });

        Assert.True(query.TryGetCachedParser<UserRow>(usage, out var parser));
        Assert.NotNull(parser);
    }

    [Fact]
    public void Warm_runs_reuse_the_same_parser_instance() {
        var query = new QueryCommand("SELECT ID, Name FROM Users WHERE ID = @ID");
        using var cnn = Db.GetConnection();
        query.Query<UserRow>(cnn, new { ID = 1 });

        Span<bool> usage = stackalloc bool[query.Mapper.Count];
        usage[0] = true;
        Assert.True(query.TryGetCachedParser<UserRow>(usage, out var first));
        Assert.True(query.TryGetCachedParser<UserRow>(usage, out var second));
        Assert.Same(first, second);
    }

    [Fact]
    public void Cached_parser_still_returns_correct_results_on_reruns() {
        var query = new QueryCommand("SELECT ID, Name, Email FROM Users WHERE ID = @ID");
        using var cnn = Db.GetConnection();
        for (int i = 1; i <= 3; i++) {
            var again = query.Query<UserRow>(cnn, new { ID = 2 });
            Assert.NotNull(again);
            Assert.Equal("Victor", again.Name);
        }
    }

    [Fact]
    public void CachedTypeParser_maps_a_self_built_command() {
        var parser = new CachedTypeParser<UserRow>();
        using var cnn = Db.Open();
        using var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT ID, Name, Email FROM Users WHERE ID = 1";
        var user = parser.Query((DbCommand)cmd);
        Assert.NotNull(user);
        Assert.Equal("John", user.Name);
    }

    [Fact]
    public void CachedTypeParser_reuses_its_parser_on_the_second_run() {
        var parser = new CachedTypeParser<UserRow>();
        using var cnn = Db.Open();
        using var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT ID, Name, Email FROM Users WHERE ID = 1";

        Assert.Equal(System.Data.CommandBehavior.SingleResult, parser.Behavior);
        parser.Query((DbCommand)cmd);
        var warm = parser.Query((DbCommand)cmd);
        Assert.NotNull(warm);
        Assert.Equal("John", warm.Name);
    }

    [Fact]
    public async Task CachedTypeParser_queries_async() {
        var parser = new CachedTypeParser<UserRow>();
        using var cnn = Db.Open();
        using var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT ID, Name, Email FROM Users WHERE ID = 2";
        var cold = await parser.QueryAsync((DbCommand)cmd, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(cold);
        Assert.Equal("Victor", cold.Name);
        var warm = await parser.QueryAsync((DbCommand)cmd, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(warm);
        Assert.Equal("Victor", warm.Name);
    }

    [Fact]
    public async Task CachedTypeParser_streams_rows() {
        var parser = new CachedTypeParser<string>();
        using var cnn = Db.Open();

        for (int pass = 0; pass < 2; pass++) {
            using var cmd = cnn.CreateCommand();
            cmd.CommandText = "SELECT Name FROM Users ORDER BY ID";
            var names = new List<string>();
            await foreach (var name in parser.StreamQueryAsync(cmd, disposeCommand: false, ct: TestContext.Current.CancellationToken))
                names.Add(name);
            Assert.Equal(["John", "Victor", "Alice"], names);
        }
    }

    [Fact]
    public void CachedTypeParser_works_through_the_IDbCommand_path() {
        var parser = new CachedTypeParser<long>();
        using var cnn = Db.Open();
        using var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Users";
        Assert.Equal(3L, parser.Query((System.Data.IDbCommand)cmd));
        Assert.Equal(3L, parser.Query((System.Data.IDbCommand)cmd));
    }
}
