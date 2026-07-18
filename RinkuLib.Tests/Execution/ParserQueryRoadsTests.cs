using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// The parsers' own Query plumbing: opening a closed connection, closing it after, disposing the command
/// when asked, wrapping legacy readers, learning through a cache, and the lazy shape's end-of-enumeration
/// cleanup.
/// </summary>
public class ParserQueryRoadsTests(SqliteDb Db) : IClassFixture<SqliteDb> {
    const string Names = "SELECT Name FROM Users ORDER BY ID";
    const string NoRows = "SELECT Name FROM Users WHERE ID = -1";

    static ITypeParser<T> Parser<T>() {
        ColumnInfo[] cols = [new("Name", typeof(string), false)];
        return TypeParser.GetTypeParser<T>(ref cols);
    }

    SqliteCommand Cmd(SqliteConnection cnn, string sql) {
        var cmd = cnn.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }

    sealed class CountingCache : ICache {
        public int Calls;
        public void UpdateCache(IDbCommand cmd) => Calls++;
        public Task UpdateCacheAsync(IDbCommand cmd, CancellationToken ct = default) {
            Calls++;
            return Task.CompletedTask;
        }
    }


    [Fact]
    public void Query_opens_a_closed_connection_and_closes_it_after() {
        using var cnn = Db.GetConnection();
        using var cmd = Cmd(cnn, Names);
        Assert.Equal("John", Parser<string>().Query(cmd));
        Assert.Equal(ConnectionState.Closed, cnn.State);

        using var open = Db.Open();
        using var cmd2 = Cmd(open, Names);
        Assert.Equal("John", Parser<string>().Query(cmd2));
        Assert.Equal(ConnectionState.Open, open.State);
    }

    [Fact]
    public void Query_with_no_row_defaults_per_shape() {
        using var cnn = Db.GetConnection();
        using var cmd = Cmd(cnn, NoRows);
        Assert.ThrowsAny<Exception>(() => Parser<string>().Query(cmd)); 
        using var cmd2 = Cmd(cnn, NoRows);
        Assert.Empty(Parser<List<string>>().Query(cmd2));
        using var cmd3 = Cmd(cnn, NoRows);
        Assert.False(Parser<Optional<string>>().Query(cmd3).HasValue);
    }

    [Fact]
    public void Query_can_dispose_the_command_and_requires_a_connection() {
        using var cnn = Db.GetConnection();
        var cmd = Cmd(cnn, Names);
        Assert.Equal("John", Parser<string>().Query(cmd, disposeCommand: true));

        var orphan = new SqliteCommand(Names);
        Assert.ThrowsAny<Exception>(() => Parser<string>().Query(orphan));
        Assert.ThrowsAny<Exception>(() => Parser<string>().Query((IDbCommand)orphan));
        Assert.ThrowsAny<Exception>(() => Parser<IEnumerable<string>>().Query(orphan).ToList());
        Assert.ThrowsAny<Exception>(() => Parser<IEnumerable<string>>().Query((IDbCommand)orphan).ToList());
    }

    [Fact]
    public void A_non_simple_shape_goes_through_Parse() {
        using var cnn = Db.GetConnection();
        using var cmd = Cmd(cnn, Names);
        Assert.Equal(["John", "Victor", "Alice"], Parser<List<string>>().Query(cmd));
    }

    [Fact]
    public void The_IDbCommand_road_wraps_a_legacy_reader() {
        using var cnn = Db.GetConnection();
        using var inner = Cmd(cnn, Names);
        IDbCommand plain = new PlainCommand(inner);
        Assert.Equal("John", Parser<string>().Query(plain));
        Assert.Equal(["John", "Victor", "Alice"], Parser<List<string>>().Query(plain));
        Assert.Equal(ConnectionState.Closed, cnn.State);

        IDbCommand asInterface = Cmd(cnn, Names);
        Assert.Equal("John", Parser<string>().Query(asInterface, disposeCommand: true));
    }

    [Fact]
    public async Task QueryAsync_covers_the_same_roads() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = Db.GetConnection();
        using var cmd = Cmd(cnn, Names);
        Assert.Equal("John", await Parser<string>().QueryAsync(cmd, ct: ct));
        Assert.Equal(ConnectionState.Closed, cnn.State);

        using var open = Db.Open();
        using var cmd2 = Cmd(open, Names);
        Assert.Equal(["John", "Victor", "Alice"], await Parser<List<string>>().QueryAsync(cmd2, ct: ct));

        using var cmd3 = Cmd(cnn, NoRows);
        Assert.False((await Parser<Optional<string>>().QueryAsync(cmd3, ct: ct)).HasValue);

        var disposable = Cmd(cnn, Names);
        Assert.Equal("John", await Parser<string>().QueryAsync((IDbCommand)disposable, disposeCommand: true, ct: ct));

        using var inner = Cmd(cnn, Names);
        IDbCommand plain = new PlainCommand(inner);
        Assert.Equal("John", await Parser<string>().QueryAsync(plain, ct: ct));
        Assert.Equal(["John", "Victor", "Alice"], await Parser<IEnumerable<string>>().QueryAsync(plain, ct: ct));
    }

    [Fact]
    public async Task Query_with_a_cache_learns_once_per_run() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = Db.GetConnection();
        var cache = new CountingCache();

        using var cmd = Cmd(cnn, Names);
        Assert.Equal("John", Parser<string>().Query(cmd, cache));
        using var cmd2 = Cmd(cnn, Names);
        Assert.Equal("John", Parser<string>().Query((IDbCommand)new PlainCommand(cmd2), cache));
        using var cmd3 = Cmd(cnn, Names);
        Assert.Equal("John", await Parser<string>().QueryAsync(cmd3, cache, ct: ct));
        using var cmd4 = Cmd(cnn, Names);
        Assert.Equal("John", await Parser<string>().QueryAsync((IDbCommand)new PlainCommand(cmd4), cache, ct: ct));
        using var cmd5 = Cmd(cnn, Names);
        Assert.Equal("John", await Parser<string>().QueryAsync((IDbCommand)cmd5, cache, ct: ct));
        Assert.Equal(5, cache.Calls);

        using var cmd6 = Cmd(cnn, NoRows);
        Assert.False(Parser<Optional<string>>().Query(cmd6, cache).HasValue);
        using var cmd7 = Cmd(cnn, NoRows);
        Assert.False(Parser<Optional<string>>().Query((IDbCommand)new PlainCommand(cmd7), cache).HasValue);
        using var open = Db.Open();
        using var cmd8 = Cmd(open, Names);
        Assert.Equal("John", Parser<string>().Query(cmd8, cache, disposeCommand: false));
    }

    [Fact]
    public async Task Query_with_a_cache_disposes_and_closes_like_the_plain_road() {
        var ct = TestContext.Current.CancellationToken;
        var cache = new CountingCache();
        using var cnn = Db.GetConnection();

        var cmd = Cmd(cnn, Names);
        Assert.Equal("John", Parser<string>().Query(cmd, cache, disposeCommand: true));
        var cmd2 = Cmd(cnn, Names);
        Assert.Equal("John", Parser<string>().Query((IDbCommand)new PlainCommand(cmd2), cache, disposeCommand: true));
        var cmd3 = Cmd(cnn, Names);
        Assert.Equal("John", await Parser<string>().QueryAsync(cmd3, cache, disposeCommand: true, ct: ct));

        using var open = Db.Open();
        using var cmd4 = Cmd(open, Names);
        Assert.Equal("John", await Parser<string>().QueryAsync(cmd4, cache, ct: ct));
        Assert.Equal(ConnectionState.Open, open.State);
        using var cmd5 = Cmd(open, Names);
        Assert.Equal("John", Parser<string>().Query((IDbCommand)new PlainCommand(cmd5), cache));
        using var cmd6 = Cmd(cnn, NoRows);
        Assert.False((await Parser<Optional<string>>().QueryAsync(cmd6, cache, ct: ct)).HasValue);
        using var cmd7 = Cmd(cnn, NoRows);
        Assert.Empty(await Parser<List<string>>().QueryAsync(cmd7, cache, ct: ct));
    }

    [Fact]
    public async Task An_optional_row_goes_through_the_row_delegate() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = Db.GetConnection();
        using var cmd = Cmd(cnn, Names);
        Assert.Equal("John", Parser<Optional<string>>().Query(cmd).Value);
        using var cmd2 = Cmd(cnn, Names);
        Assert.Equal("John", (await Parser<Optional<string>>().QueryAsync(cmd2, ct: ct)).Value);
        using var cmd3 = Cmd(cnn, Names);
        Assert.Equal(1L, Parser<Single<long>>().Query(Cmd(cnn, "SELECT ID FROM Users WHERE ID = 1"), disposeCommand: true).Value);
    }

    [Fact]
    public void Abandoned_lazy_iterators_still_dispose_and_close_per_variant() {
        var cache = new CountingCache();

        using (var cnn = Db.GetConnection()) {
            var cmd = Cmd(cnn, Names);
            Assert.Equal("John", Parser<IEnumerable<string>>().Query(cmd, cache, disposeCommand: true).First());
            Assert.Equal(ConnectionState.Closed, cnn.State);
        }
        using (var cnn = Db.GetConnection()) {
            var inner = Cmd(cnn, Names);
            Assert.Equal("John", Parser<IEnumerable<string>>().Query((IDbCommand)new PlainCommand(inner), disposeCommand: true).First());
            Assert.Equal(ConnectionState.Closed, cnn.State);
        }
        using (var cnn = Db.GetConnection()) {
            var inner = Cmd(cnn, Names);
            Assert.Equal("John", Parser<IEnumerable<string>>().Query((IDbCommand)new PlainCommand(inner), cache, disposeCommand: true).First());
            Assert.Equal(ConnectionState.Closed, cnn.State);
        }
        using (var cnn = Db.GetConnection()) {
            var cmd = Cmd(cnn, Names);
            Assert.Equal("John", Parser<IEnumerable<string>>().Query(cmd, disposeCommand: true).First());
            Assert.Equal(ConnectionState.Closed, cnn.State);
        }
    }


    [Fact]
    public void The_lazy_shape_streams_and_cleans_up_at_the_end() {
        using var cnn = Db.GetConnection();
        using var cmd = Cmd(cnn, Names);
        var streamed = Parser<IEnumerable<string>>().Query(cmd).ToList();
        Assert.Equal(["John", "Victor", "Alice"], streamed);
        Assert.Equal(ConnectionState.Closed, cnn.State);

        using var open = Db.Open();
        using var cmd2 = Cmd(open, Names);
        Assert.Equal(3, Parser<IEnumerable<string>>().Query(cmd2).Count());
        Assert.Equal(ConnectionState.Open, open.State);

        using var cmd3 = Cmd(cnn, NoRows);
        Assert.Empty(Parser<IEnumerable<string>>().Query(cmd3));

        var disposing = Cmd(cnn, Names);
        Assert.Equal(3, Parser<IEnumerable<string>>().Query(disposing, disposeCommand: true).Count());
    }

    [Fact]
    public void Abandoning_the_lazy_shape_midway_still_cleans_up() {
        using var cnn = Db.GetConnection();
        using var cmd = Cmd(cnn, Names);
        Assert.Equal("John", Parser<IEnumerable<string>>().Query(cmd).First());
        Assert.Equal(ConnectionState.Closed, cnn.State);

        var cache = new CountingCache();
        using var cmd2 = Cmd(cnn, Names);
        Assert.Equal("John", Parser<IEnumerable<string>>().Query(cmd2, cache).First());
        Assert.Equal(1, cache.Calls);

        using var inner = Cmd(cnn, Names);
        IDbCommand plain = new PlainCommand(inner);
        Assert.Equal("John", Parser<IEnumerable<string>>().Query(plain).First());
        Assert.Equal("John", Parser<IEnumerable<string>>().Query(plain, cache).First());
        Assert.Equal(2, cache.Calls);
    }

    [Fact]
    public async Task The_lazy_shape_async_wrappers_deliver_the_same_rows() {
        var ct = TestContext.Current.CancellationToken;
        var cache = new CountingCache();
        using var cnn = Db.GetConnection();
        using var cmd = Cmd(cnn, Names);
        Assert.Equal(3, (await Parser<IEnumerable<string>>().QueryAsync(cmd, ct: ct)).Count());
        using var cmd2 = Cmd(cnn, Names);
        Assert.Equal(3, (await Parser<IEnumerable<string>>().QueryAsync(cmd2, cache, ct: ct)).Count());
        using var cmd3 = Cmd(cnn, Names);
        Assert.Equal(3, (await Parser<IEnumerable<string>>().QueryAsync((IDbCommand)cmd3, ct: ct)).Count());
        using var cmd4 = Cmd(cnn, Names);
        Assert.Equal(3, (await Parser<IEnumerable<string>>().QueryAsync((IDbCommand)cmd4, cache, ct: ct)).Count());
        using var cmd5 = Cmd(cnn, Names);
        IDbCommand plain = new PlainCommand(cmd5);
        Assert.Equal(3, (await Parser<IEnumerable<string>>().QueryAsync(plain, ct: ct)).Count());
        Assert.Equal(3, (await Parser<IEnumerable<string>>().QueryAsync(plain, cache, ct: ct)).Count());
    }


    static readonly ColumnInfo[] ValueCol = [new("V", typeof(int), false)];

    static (BaseEnumerableTypeParser<int> Parser, DbDataReader Reader) Lazy(params int[] values) {
        var cols = ValueCol;
        var parser = (BaseEnumerableTypeParser<int>)TypeParser.GetTypeParser<IEnumerable<int>>(ref cols);
        var reader = Rows.Reader(ValueCol, values.Select(v => new object[] { v }).ToArray());
        reader.Read();
        return (parser, reader);
    }

    [Fact]
    public void Each_ownership_strategy_cleans_its_own_set() {
        var (parser, reader) = Lazy(1, 2);
        Assert.Equal([1, 2], parser.ParseAndOwn(reader, new DisposeReader()));
        Assert.True(reader.IsClosed);

        using var cnn = Db.Open();
        var (p2, r2) = Lazy(3);
        var cmd = Cmd(cnn, Names);
        Assert.Equal([3], p2.ParseAndOwn(r2, cmd, wasClosed: true, disposeCommand: true));
        Assert.Equal(ConnectionState.Closed, cnn.State);

        using var cnn2 = Db.Open();
        var (p3, r3) = Lazy(4);
        using var cmd2 = Cmd(cnn2, Names);
        Assert.Equal([4], p3.ParseAndOwn(r3, cmd2, wasClosed: true, disposeCommand: false));
        Assert.Equal(ConnectionState.Closed, cnn2.State);

        var (p4, r4) = Lazy(5);
        var cmd3 = Cmd(cnn2, Names);
        Assert.Equal([5], p4.ParseAndOwn(r4, cmd3, wasClosed: false, disposeCommand: true));
        Assert.True(r4.IsClosed);

        var (p5, r5) = Lazy(6);
        using var cmd4 = Cmd(cnn2, Names);
        Assert.Equal([6], p5.ParseAndOwn(r5, cmd4, wasClosed: false, disposeCommand: false));
        Assert.True(r5.IsClosed);

        var (p6, r6) = Lazy(7);
        new DoNothing().Invoke(r6);
        Assert.False(r6.IsClosed);
        new GoToNextResultSet().Invoke(r6);  
        r6.Dispose();
    }

    public record UserRow(long ID, string Name) : IDbReadable;

    [Fact]
    public async Task The_remaining_shape_and_row_crossings() {
        var ct = TestContext.Current.CancellationToken;
        var cache = new CountingCache();
        using var cnn = Db.GetConnection();

        using var empty = Cmd(cnn, NoRows);
        Assert.False(Parser<Optional<string>>().Query((IDbCommand)new PlainCommand(empty)).HasValue);

        using var cmd = Cmd(cnn, Names);
        Assert.Equal(3, Parser<List<string>>().Query(cmd, cache).Count);
        using var cmd2 = Cmd(cnn, Names);
        Assert.Equal(3, Parser<List<string>>().Query((IDbCommand)new PlainCommand(cmd2), cache).Count);
        using var cmd3 = Cmd(cnn, Names);
        Assert.Equal(3, (await Parser<List<string>>().QueryAsync(cmd3, cache, ct: ct)).Count);

        ColumnInfo[] cols = [new("ID", typeof(long), false), new("Name", typeof(string), false)];
        var lazyUsers = TypeParser.GetTypeParser<IEnumerable<UserRow>>(ref cols);
        using var cmd4 = Cmd(cnn, "SELECT ID, Name FROM Users ORDER BY ID");
        Assert.Equal("John", lazyUsers.Query(cmd4).First().Name);
        using var cmd5 = Cmd(cnn, "SELECT ID, Name FROM Users ORDER BY ID");
        Assert.Equal(3, lazyUsers.Query((IDbCommand)new PlainCommand(cmd5), cache).Count());
        using var cmd6 = Cmd(cnn, "SELECT ID, Name FROM Users ORDER BY ID");
        Assert.Equal(3, lazyUsers.Query(cmd6, cache).Count());
        using var cmd7 = Cmd(cnn, "SELECT ID, Name FROM Users ORDER BY ID");
        Assert.Equal(3, lazyUsers.Query((IDbCommand)new PlainCommand(cmd7)).Count());
    }


    static SqliteCommand BadCmd() {
        var cnn = new SqliteConnection("Data Source=Z:\\rinku-no-such-dir\\missing.db;Mode=ReadOnly");
        var cmd = cnn.CreateCommand();
        cmd.CommandText = Names;
        return cmd;
    }

    [Fact]
    public async Task A_failing_open_still_runs_every_cleanup_half() {
        var ct = TestContext.Current.CancellationToken;
        var cache = new CountingCache();
        var p = Parser<string>();
        var lazy = Parser<IEnumerable<string>>();

        Assert.ThrowsAny<Exception>(() => p.Query(BadCmd(), disposeCommand: true));
        Assert.ThrowsAny<Exception>(() => p.Query((IDbCommand)new PlainCommand(BadCmd()), disposeCommand: true));
        Assert.ThrowsAny<Exception>(() => p.Query(BadCmd(), cache, disposeCommand: true));
        Assert.ThrowsAny<Exception>(() => p.Query((IDbCommand)new PlainCommand(BadCmd()), cache, disposeCommand: true));
        await Assert.ThrowsAnyAsync<Exception>(() => p.QueryAsync(BadCmd(), disposeCommand: true, ct: ct));
        await Assert.ThrowsAnyAsync<Exception>(() => p.QueryAsync(BadCmd(), cache, disposeCommand: true, ct: ct));

        Assert.ThrowsAny<Exception>(() => lazy.Query(BadCmd(), disposeCommand: true).ToList());
        Assert.ThrowsAny<Exception>(() => lazy.Query((IDbCommand)new PlainCommand(BadCmd()), disposeCommand: true).ToList());
        Assert.ThrowsAny<Exception>(() => lazy.Query(BadCmd(), cache, disposeCommand: true).ToList());
        Assert.ThrowsAny<Exception>(() => lazy.Query((IDbCommand)new PlainCommand(BadCmd()), cache, disposeCommand: true).ToList());
        Assert.Equal(0, cache.Calls);
    }

    [Fact]
    public void Empty_results_flow_through_each_lazy_variant() {
        var cache = new CountingCache();
        using var cnn = Db.GetConnection();
        using var cmd = Cmd(cnn, NoRows);
        Assert.Empty(Parser<IEnumerable<string>>().Query(cmd, cache));
        using var cmd2 = Cmd(cnn, NoRows);
        Assert.Empty(Parser<IEnumerable<string>>().Query((IDbCommand)new PlainCommand(cmd2)));
        using var cmd3 = Cmd(cnn, NoRows);
        Assert.Empty(Parser<IEnumerable<string>>().Query((IDbCommand)new PlainCommand(cmd3), cache));
    }


    [Fact]
    public async Task Single_shapes_refuse_a_second_row() {
        var ct = TestContext.Current.CancellationToken;
        var cols = ValueCol;
        var inner = TypeParser.GetTypeParser<int>(ref cols);

        var slow = new SingleTypeParser<Single<int>, int>(inner);
        Assert.NotEqual(default, slow.Behavior);
        Assert.Equal(default, slow.Default());
        using (var r = Rows.Reader(ValueCol, [1], [2])) {
            r.Read();
            Assert.ThrowsAny<Exception>(() => slow.Parse(r));
        }
        using (var r = Rows.Reader(ValueCol, [1], [2])) {
            r.Read();
            await Assert.ThrowsAnyAsync<Exception>(async () => await slow.ParseAsync(r, ct));
        }

        var fast = (BaseTypeParser<Single<int>>)TypeParser.GetTypeParser<Single<int>>(ref cols);
        Assert.Equal(default, fast.Default());
        using (var r = Rows.Reader(ValueCol, [1], [2])) {
            r.Read();
            Assert.ThrowsAny<Exception>(() => fast.Parse(r));
        }
        using (var r = Rows.Reader(ValueCol, [1], [2])) {
            r.Read();
            await Assert.ThrowsAnyAsync<Exception>(async () => await fast.ParseAsync(r, ct));
        }
    }

    [Fact]
    public void The_slow_wrappers_expose_their_shape_members() {
        var cols = ValueCol;
        var inner = TypeParser.GetTypeParser<int>(ref cols);

        var list = new ListTypeParser<int>(inner);
        Assert.NotEqual((System.Data.CommandBehavior)(-1), list.Behavior);
        Assert.Empty(list.Default());

        var optional = new OptionalTypeParser<OptionalStruct<int>, int>(inner);
        Assert.NotEqual((System.Data.CommandBehavior)(-1), optional.Behavior);
        Assert.False(optional.Default().HasValue);

        var enumerable = new EnumerableTypeParser<int>(inner);
        Assert.NotEqual((System.Data.CommandBehavior)(-1), enumerable.Behavior);
        using var r = Rows.Reader(ValueCol, [4], [5]);
        r.Read();
        Assert.Equal([4, 5], enumerable.Parse(r).Result);

        Assert.True(((ITypeParser<int>)inner).InternalProtect);
        Assert.True(((ITypeParser<IEnumerable<int>>)enumerable).InternalProtect);
    }

    [Fact]
    public void Cleanup_strategies_survive_a_command_without_a_connection() {
        var legacy = new LegacyCommand();
        using var r1 = Rows.Reader(ValueCol, [1]);
        new DisposeReaderAndCommandAndCloseConnection(legacy).Invoke(r1);
        Assert.True(r1.IsClosed);
        var legacy2 = new LegacyCommand();
        using var r2 = Rows.Reader(ValueCol, [1]);
        new DisposeReaderAndCloseConnection(legacy2).Invoke(r2);
        Assert.True(r2.IsClosed);
    }


    [Fact]
    public async Task Every_shape_parses_asynchronously() {
        var ct = TestContext.Current.CancellationToken;
        var cols = ValueCol;

        using var r1 = Rows.Reader(ValueCol, [1], [2]);
        await r1.ReadAsync(ct);
        var fastList = TypeParser.GetTypeParser<List<int>>(ref cols);
        Assert.Equal([1, 2], (await fastList.ParseAsync(r1, ct)).Result);

        var inner = TypeParser.GetTypeParser<int>(ref cols);
        using var r2 = Rows.Reader(ValueCol, [3], [4]);
        await r2.ReadAsync(ct);
        Assert.Equal([3, 4], (await new ListTypeParser<int>(inner).ParseAsync(r2, ct)).Result);

        using var r3 = Rows.Reader(ValueCol, [5]);
        await r3.ReadAsync(ct);
        Assert.Equal(5, (await new SingleTypeParser<Single<int>, int>(inner).ParseAsync(r3, ct)).Result.Value);

        var strCols = new ColumnInfo[] { new("V", typeof(string), false) };
        var innerStr = TypeParser.GetTypeParser<string>(ref strCols);
        using var r4 = Rows.Reader(strCols, ["a"]);
        await r4.ReadAsync(ct);
        Assert.Equal("a", (await new OptionalTypeParser<Optional<string>, string>(innerStr).ParseAsync(r4, ct)).Result.Value);

        using var r5 = Rows.Reader(strCols, ["b"]);
        await r5.ReadAsync(ct);
        var fastOptional = TypeParser.GetTypeParser<Optional<string>>(ref strCols);
        Assert.Equal("b", (await fastOptional.ParseAsync(r5, ct)).Result.Value);

        using var r6 = Rows.Reader(ValueCol, [9]);
        await r6.ReadAsync(ct);
        var fastSingle = TypeParser.GetTypeParser<Single<int>>(ref cols);
        Assert.Equal(9, (await fastSingle.ParseAsync(r6, ct)).Result.Value);

        using var r7 = Rows.Reader(ValueCol, [7], [8]);
        await r7.ReadAsync(ct);
        var enumerable = TypeParser.GetTypeParser<IEnumerable<int>>(ref cols);
        Assert.Equal([7, 8], (await enumerable.ParseAsync(r7, ct)).Result);
    }

    [Fact]
    public async Task A_simple_parser_steps_row_by_row() {
        var ct = TestContext.Current.CancellationToken;
        var cols = ValueCol;
        var stepper = (IStepParser<int>)TypeParser.GetTypeParser<int>(ref cols);

        using var reader = Rows.Reader(ValueCol, [1], [2]);
        reader.Read();
        Assert.Equal(1, stepper.ParseStep(reader));  
        reader.Read();
        Assert.Equal(2, await stepper.ParseStepAsync(reader, ct));
    }
}
