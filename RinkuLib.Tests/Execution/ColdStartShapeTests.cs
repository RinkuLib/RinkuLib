using System.Data;
using System.Data.Common;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// The parser a run uses when it has none yet. It is an ordinary parser, so the run is the ordinary
/// <c>Query</c>, and it works out what the columns call for on the first row rather than being told.
/// </summary>
public class ColdStartShapeTests(SqliteDb Db) : IClassFixture<SqliteDb> {

    /// <summary>Counts how often it is asked, and answers with the parser the columns really call for.</summary>
    sealed class CountingCache : ICacheGivingParser<IEnumerable<UserRow>> {
        public int Asked;
        public CommandBehavior Behavior => CommandBehavior.Default;
        public ITypeParser<IEnumerable<UserRow>> UpdateCache(IDbCommand cmd, DbDataReader reader) {
            Asked++;
            var cols = reader.GetColumns();
            return TypeParser.GetTypeParser<IEnumerable<UserRow>>(ref cols);
        }
        public ValueTask<ITypeParser<IEnumerable<UserRow>>> UpdateCacheAsync(IDbCommand cmd, DbDataReader reader, CancellationToken ct = default)
            => new(UpdateCache(cmd, reader));
    }

    static readonly EnumerableTypeParserMaker Streamed = new();

    /// <summary>
    /// Taking the run does nothing yet, so the cache is not asked either. Walking the rows is what opens
    /// the reader and settles the parser the columns call for.
    /// </summary>
    [Fact]
    public void The_parser_is_settled_when_the_rows_are_walked() {
        using var cnn = Db.GetConnection();
        var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT ID, Name, Email FROM Users ORDER BY ID";
        var cache = new CountingCache();

        Assert.True(Streamed.TryColdStart<IEnumerable<UserRow>>((DbCommand)cmd, cache, false, out var rows));

        Assert.Equal(0, cache.Asked);
        Assert.Equal(ConnectionState.Closed, cnn.State);

        Assert.Equal(["John", "Victor", "Alice"], rows.Select(r => r.Name));
        Assert.Equal(1, cache.Asked);
        Assert.Equal(ConnectionState.Closed, cnn.State);
    }

    /// <summary>Each walk is its own run of the command, so the parser is settled again for each.</summary>
    [Fact]
    public void Walking_the_rows_again_runs_the_command_again() {
        using var cnn = Db.GetConnection();
        var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT ID, Name, Email FROM Users ORDER BY ID";
        var cache = new CountingCache();

        Assert.True(Streamed.TryColdStart<IEnumerable<UserRow>>((DbCommand)cmd, cache, false, out var rows));

        Assert.Equal(["John", "Victor", "Alice"], rows.Select(r => r.Name));
        Assert.Equal(["John", "Victor", "Alice"], rows.Select(r => r.Name));
        Assert.Equal(2, cache.Asked);
        Assert.Equal(ConnectionState.Closed, cnn.State);
    }

    /// <summary>The interface road too, for a command that is only an <see cref="IDbCommand"/>.</summary>
    [Fact]
    public void The_interface_road_takes_the_run_the_same_way() {
        using var cnn = Db.GetConnection();
        var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT ID, Name, Email FROM Users ORDER BY ID";
        var cache = new CountingCache();

        Assert.True(Streamed.TryColdStart<IEnumerable<UserRow>>((IDbCommand)cmd, cache, false, out var rows));
        Assert.Equal(0, cache.Asked);

        Assert.Equal(["John", "Victor", "Alice"], rows.Select(r => r.Name));
        Assert.Equal(ConnectionState.Closed, cnn.State);
    }

    /// <summary>
    /// The asynchronous road opens, runs and learns while it can await, so awaiting it is what settles the
    /// parser. The rows are then walked over the reader it left open, and walking them out closes it.
    /// </summary>
    [Fact]
    public async Task The_async_run_does_its_opening_while_it_can_await() {
        using var cnn = Db.GetConnection();
        var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT ID, Name, Email FROM Users ORDER BY ID";
        var cache = new CountingCache();

        Assert.True(Streamed.TryColdStartAsync<IEnumerable<UserRow>>((DbCommand)cmd, cache, false, default, out var task));

        var rows = await task;
        Assert.Equal(1, cache.Asked);
        Assert.Equal(ConnectionState.Open, cnn.State);

        Assert.Equal(["John", "Victor", "Alice"], rows.Select(r => r.Name));
        Assert.Equal(ConnectionState.Closed, cnn.State);
    }

    /// <summary>Leaving the walk early closes the reader the awaiting left open.</summary>
    [Fact]
    public async Task Leaving_the_async_walk_early_closes_the_reader() {
        using var cnn = Db.GetConnection();
        var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT ID, Name, Email FROM Users ORDER BY ID";

        Assert.True(Streamed.TryColdStartAsync<IEnumerable<UserRow>>((DbCommand)cmd, new CountingCache(), false, default, out var task));

        foreach (var row in await task) {
            Assert.Equal(1, row.ID);
            break;
        }
        Assert.Equal(ConnectionState.Closed, cnn.State);
    }

    /// <summary>
    /// The token is honoured over the part the road awaits, so a cancelled one stops it at the await rather
    /// than going to the database.
    /// </summary>
    [Fact]
    public async Task The_async_run_honours_the_token_over_what_it_awaits() {
        using var cnn = Db.GetConnection();
        var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT ID, Name, Email FROM Users ORDER BY ID";
        var cache = new CountingCache();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.True(Streamed.TryColdStartAsync<IEnumerable<UserRow>>((DbCommand)cmd, cache, false, cancelled.Token, out var task));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        Assert.Equal(0, cache.Asked);
        Assert.Equal(ConnectionState.Closed, cnn.State);
    }

    /// <summary>The synchronous road holds everything back to the walk, a walk being all it has.</summary>
    [Fact]
    public void The_sync_run_holds_everything_back_to_the_walk() {
        using var cnn = Db.GetConnection();
        var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT ID, Name, Email FROM Users ORDER BY ID";
        var cache = new CountingCache();

        Assert.True(Streamed.TryColdStart<IEnumerable<UserRow>>((DbCommand)cmd, cache, false, out var rows));
        Assert.Equal(0, cache.Asked);
        Assert.Equal(ConnectionState.Closed, cnn.State);

        Assert.Equal(["John", "Victor", "Alice"], rows.Select(r => r.Name));
        Assert.Equal(1, cache.Asked);
        Assert.Equal(ConnectionState.Closed, cnn.State);
    }

    /// <summary>
    /// The streamed shape takes a cold run and hands the rows straight back. Every other maker declines it,
    /// whatever order they are asked in, so the run falls through to the engine.
    /// </summary>
    [Fact]
    public void Only_the_streamed_shape_takes_a_cold_run() {
        using var cnn = Db.GetConnection();
        var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT ID, Name, Email FROM Users ORDER BY ID";
        var taken = new List<ITypeParserMaker>();

        foreach (var maker in TypeParser.TypeParserMakers)
            if (maker.TryColdStart<IEnumerable<UserRow>>(cmd, new CountingCache(), false, out var rows)) {
                taken.Add(maker);
                Assert.Equal(["John", "Victor", "Alice"], rows.Select(r => r.Name));
            }

        Assert.Single(taken);
        Assert.Equal(ConnectionState.Closed, cnn.State);
    }

    /// <summary>A buffered shape is nobody's cold run, so every maker declines and the engine takes it.</summary>
    [Fact]
    public void A_buffered_shape_is_declined_by_every_maker() {
        using var cnn = Db.GetConnection();
        var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT ID, Name, Email FROM Users ORDER BY ID";

        foreach (var maker in TypeParser.TypeParserMakers) {
            Assert.False(maker.TryColdStart<List<UserRow>>(cmd, new ListCache(), false, out _));
            Assert.False(maker.TryColdStart<UserRow>(cmd, new RowCache(), false, out _));
        }
        Assert.False(((ITypeParserMaker)TypeParser.DefaultTypeParserMaker)
            .TryColdStart<IEnumerable<UserRow>>(cmd, new CountingCache(), false, out _));

        Assert.Equal(ConnectionState.Closed, cnn.State);
    }

    sealed class ListCache : ICacheGivingParser<List<UserRow>> {
        public CommandBehavior Behavior => CommandBehavior.Default;
        public ITypeParser<List<UserRow>> UpdateCache(IDbCommand cmd, DbDataReader reader) {
            var cols = reader.GetColumns();
            return TypeParser.GetTypeParser<List<UserRow>>(ref cols);
        }
        public ValueTask<ITypeParser<List<UserRow>>> UpdateCacheAsync(IDbCommand cmd, DbDataReader reader, CancellationToken ct = default)
            => new(UpdateCache(cmd, reader));
    }

    sealed class RowCache : ICacheGivingParser<UserRow> {
        public CommandBehavior Behavior => CommandBehavior.Default;
        public ITypeParser<UserRow> UpdateCache(IDbCommand cmd, DbDataReader reader) {
            var cols = reader.GetColumns();
            return TypeParser.GetTypeParser<UserRow>(ref cols);
        }
        public ValueTask<ITypeParser<UserRow>> UpdateCacheAsync(IDbCommand cmd, DbDataReader reader, CancellationToken ct = default)
            => new(UpdateCache(cmd, reader));
    }
}
