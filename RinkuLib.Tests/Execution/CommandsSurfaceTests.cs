using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// The command-layer overload matrix: the object-typed parameter overloads, the learned-parser dispatch
/// branches, the multi-reader roads, the bound builder surface, and the connection shortcut variants.
/// </summary>
public class CommandsSurfaceTests(SqliteDb Db) : IClassFixture<SqliteDb> {
    const string TwoSets = "SELECT ID FROM Users ORDER BY ID; SELECT Name FROM Users ORDER BY ID";

    // ---- the object-typed parameter overloads of the direct build road ----

    [Fact]
    public async Task Object_typed_parameter_overloads_run_on_both_connection_kinds() {
        var ct = TestContext.Current.CancellationToken;
        var insert = new QueryCommand("INSERT INTO Scratch (Val) VALUES (@Val)");
        object args = new { Val = 100 };

        using (var cnn = Db.GetConnection())
            Assert.Equal(1, insert.Execute(cnn, args));
        using (var cnn = Db.GetConnection())
            Assert.Equal(1, await insert.ExecuteAsync(cnn, args, ct: ct));
        using (var cnn = Db.GetConnection())
            Assert.Equal(1, insert.Execute((IDbConnection)cnn, args));
        using (var cnn = Db.GetConnection())
            Assert.Equal(1, await insert.ExecuteAsync((IDbConnection)cnn, args, ct: ct));

        var select = new QueryCommand("SELECT Name FROM Users WHERE ID = @ID");
        object one = new { ID = 1 };
        using (var cnn = Db.Open()) {
            using var reader = select.ExecuteReader((IDbConnection)cnn, out var cmd, one);
            Assert.True(reader.Read());
            Assert.Equal("John", reader.GetString(0));
            cmd.Dispose();
        }
        using (var cnn = Db.Open()) {
            using var reader = await select.ExecuteReaderAsync((IDbConnection)cnn, out var cmd, one, ct: ct);
            Assert.True(reader.Read());
            Assert.Equal("John", reader.GetString(0));
            cmd.Dispose();
        }
        using (var cnn = Db.Open()) {
            using var reader = await select.ExecuteReaderAsync(cnn, out var cmd, one, ct: ct);
            Assert.True(await reader.ReadAsync(ct));
            cmd.Dispose();
        }
    }

    // ---- the learned-parser dispatch branches ----

    public record UserPair(long ID, string Name) : IDbReadable;

    [Fact]
    public async Task The_dispatch_prefers_the_settled_parser_and_relearns_when_a_param_unsettles() {
        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand("SELECT ID, Name FROM Users WHERE ID = @ID");

        using var cnn = Db.GetConnection();
        Assert.Equal("John", query.Query<UserPair>(cnn, new { ID = 1 }).Name);              // cold: linker road
        Assert.Equal("Victor", query.Query<UserPair>(cnn, new { ID = 2 }).Name);            // warm: cached road
        Assert.Equal("Alice", (await query.QueryAsync<UserPair>(cnn, new { ID = 3 }, ct: ct)).Name);
        Assert.Equal("John", query.Query<UserPair>((IDbConnection)cnn, new { ID = 1 }).Name);
        Assert.Equal("Victor", (await query.QueryAsync<UserPair>((IDbConnection)cnn, new { ID = 2 }, ct: ct)).Name);

        // unsettle the parameter: the shape still matches, so the middle branch relearns while parsing
        int id = query.Mapper.GetIndex("@ID");
        Assert.True(query.Parameters.UpdateCache(id, InferedDbParamCache.Instance));
        query.Parameters.UpdateCachedIndexes();
        Assert.Equal("John", query.Query<UserPair>(cnn, new { ID = 1 }).Name);

        Assert.True(query.Parameters.UpdateCache(id, InferedDbParamCache.Instance));
        query.Parameters.UpdateCachedIndexes();
        Assert.Equal("Victor", (await query.QueryAsync<UserPair>(cnn, new { ID = 2 }, ct: ct)).Name);

        Assert.True(query.Parameters.UpdateCache(id, InferedDbParamCache.Instance));
        query.Parameters.UpdateCachedIndexes();
        Assert.Equal("Alice", query.Query<UserPair>((IDbConnection)cnn, new { ID = 3 }).Name);

        Assert.True(query.Parameters.UpdateCache(id, InferedDbParamCache.Instance));
        query.Parameters.UpdateCachedIndexes();
        Assert.Equal("John", (await query.QueryAsync<UserPair>((IDbConnection)cnn, new { ID = 1 }, ct: ct)).Name);

        Assert.True(query.Parameters.UpdateCache(id, InferedDbParamCache.Instance));
        query.Parameters.UpdateCachedIndexes();
        var streamed = new List<string>();
        await foreach (var row in query.StreamQueryAsync<UserPair>(cnn, new { ID = 1 }, ct: ct))
            streamed.Add(row.Name);
        Assert.Equal(["John"], streamed);
    }

    [Fact]
    public async Task Streaming_covers_cold_warm_and_kept_command_roads() {
        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand("SELECT ID, Name FROM Users ORDER BY ID");
        using var cnn = Db.GetConnection();

        var cold = new List<long>();
        await foreach (var row in query.StreamQueryAsync<UserPair>(cnn, ct: ct))
            cold.Add(row.ID);
        Assert.Equal([1, 2, 3], cold);

        var warm = new List<long>();
        await foreach (var row in query.StreamQueryAsync<UserPair>(cnn, ct: ct))
            warm.Add(row.ID);
        Assert.Equal([1, 2, 3], warm);

        DbCommand kept;
        await foreach (var row in query.StreamQueryAsync<UserPair>(cnn, out kept, ct: ct)) {
            Assert.Equal(1, row.ID);
            break;                                  // abandon early: cleanup still runs
        }
        kept.Dispose();
    }

    [Fact]
    public async Task A_self_built_cached_parser_takes_a_cache_to_learn_through() {
        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand("SELECT ID, Name FROM Users WHERE ID = @ID");
        using var cnn = Db.Open();

        SqliteCommand Make(int idVal) {
            var cmd = cnn.CreateCommand();
            var map = new bool[query.Mapper.Count];
            query.SetCommand(cmd, new { ID = idVal }, map);
            return cmd;
        }

        var parser = new CachedTypeParser<UserPair>();
        using (var c = Make(1))
            Assert.Equal("John", parser.Query(c, query).Name);
        using (var c = Make(2))
            Assert.Equal("Victor", parser.Query((IDbCommand)Make(2), query, disposeCommand: true).Name);
        using (var c = Make(3))
            Assert.Equal("Alice", (await parser.QueryAsync(c, query, ct: ct)).Name);
        Assert.Equal("John", (await parser.QueryAsync((IDbCommand)Make(1), query, disposeCommand: true, ct: ct)).Name);
    }

    // ---- the multi reader ----

    [Fact]
    public async Task MultiReader_query_covers_every_shape_road() {
        var query = new QueryCommand(TwoSets);
        using var cnn = Db.GetConnection();
        using (var mr = query.ExecuteMultiReader(cnn, out var cmd)) {
            Assert.Equal([1L, 2L, 3L], mr.Query<List<long>>());          // buffered: the Parse road
            Assert.Equal(["John", "Victor", "Alice"], mr.Query<IEnumerable<string>>());  // lazy: ParseAndOwn road
            cmd.Dispose();
        }

        var ct = TestContext.Current.CancellationToken;
        using (var mr = await query.ExecuteMultiReaderAsync(cnn, out var cmd, ct: ct)) {
            Assert.Equal(1L, await mr.QueryAsync<long>(ct));             // simple row road
            Assert.Equal(["John", "Victor", "Alice"], await mr.QueryAsync<IEnumerable<string>>(ct));
            cmd.Dispose();
        }
    }

    [Fact]
    public async Task MultiReader_defaults_when_a_set_is_empty_and_streams_sets() {
        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand("SELECT ID FROM Users WHERE ID = -1; SELECT Name FROM Users ORDER BY ID");
        using var cnn = Db.GetConnection();

        using (var mr = query.ExecuteMultiReader(cnn, out var cmd)) {
            Assert.Empty(mr.Query<List<long>>());
            var streamed = new List<string>();
            await foreach (var name in mr.StreamQueryAsync<string>(ct: ct))
                streamed.Add(name);
            Assert.Equal(["John", "Victor", "Alice"], streamed);
            cmd.Dispose();
        }

        using (var mr = await query.ExecuteMultiReaderAsync(cnn, out var cmd, ct: ct)) {
            Assert.Empty(await mr.QueryAsync<List<long>>(ct));
            var pairsSet = new List<string>();
            await foreach (var name in mr.StreamQueryAsync<string>(goToNextResultSet: false, ct: ct))
                pairsSet.Add(name);
            Assert.Equal(3, pairsSet.Count);
            cmd.Dispose();
        }
    }

    [Fact]
    public async Task MultiReader_streams_object_rows_and_disposes_asynchronously() {
        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand("SELECT ID, Name FROM Users ORDER BY ID; SELECT ID FROM Users ORDER BY ID");
        using var cnn = Db.GetConnection();

        var mr = query.ExecuteMultiReader(cnn, out var cmd);
        var objects = new List<UserPair>();
        await foreach (var row in mr.StreamQueryAsync<UserPair>(ct: ct))   // non-simple stream road
            objects.Add(row);
        Assert.Equal(3, objects.Count);
        await mr.DisposeAsync();
        await mr.DisposeAsync();          // idempotent
        cmd.Dispose();

        var mr2 = await query.ExecuteMultiReaderAsync(cnn, out var cmd2, ct: ct);
        Assert.Equal(3, (await mr2.QueryAsync<List<UserPair>>(ct)).Count);
        await mr2.DisposeAsync();         // with the second set unread
        cmd2.Dispose();
    }

    // ---- the bound builder surface ----

    static readonly QueryCommand Conditional = new("SELECT Name FROM Users WHERE ID >= @Min ORDER BY ID");

    static object? Val(object? stored) => stored is IDbDataParameter p ? p.Value : stored;

    [Fact]
    public void Bound_builder_uses_and_unuses_by_every_key_form() {
        var query = new QueryCommand("SELECT * FROM t WHERE /*F*/a = 1 AND b = ?@V");
        var b = new QueryBuilderCommand<FakeCommand>(query, new FakeCommand());

        Assert.True(b.Use("F"));
        Assert.True(b.UnUse("F"));
        Assert.True(b.Use("F".AsSpan()));
        Assert.True(b.UnUse("F".AsSpan()));
        Assert.False(b.Use("Nope".AsSpan()));
        Assert.False(b.UnUse("Nope".AsSpan()));

        Assert.True(b.Use('@', "V", 5));
        Assert.Equal(5, Val(b["@V"]));
        Assert.True(b.Use("@V".AsSpan(), 6));
        Assert.Equal(6, Val(b["@V".AsSpan()]));
        Assert.Equal(6, Val(b[query.Mapper.GetIndex("@V")]));
        b.Remove("@V");
        Assert.Null(b["@V"]);
        Assert.True(b.Use("@V", 7));
        b.Remove("@V".AsSpan());
        Assert.Null(b["@V"]);
    }

    record struct MinArgs(int Min);
    class MinClass {
        public int Min { get; set; }
    }

    [Fact]
    public void Bound_builder_UseWith_takes_object_generic_and_ref_forms() {
        using var cnn = Db.Open();
        using var cmd = cnn.CreateCommand();
        var b = new QueryBuilderCommand<DbCommand>(Conditional, cmd);

        b.UseWith((object)new MinClass { Min = 2 });
        Assert.Equal(2, Val(b["@Min"]));

        b.UseWith(new MinArgs(3));
        Assert.Equal(3, Val(b["@Min"]));

        var byRefStruct = new MinArgs(1);
        b.UseWith(ref byRefStruct);
        Assert.Equal(1, Val(b["@Min"]));

        var byRefClass = new MinClass { Min = 2 };
        b.UseWith(ref byRefClass);
        Assert.Equal(2, Val(b["@Min"]));

        Assert.Equal(["Victor", "Alice"], b.Query<List<string>>());
    }

    [Fact]
    public async Task Bound_builder_executes_queries_on_both_command_kinds() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = Db.Open();

        using var dbCmd = cnn.CreateCommand();
        var db = new QueryBuilderCommand<DbCommand>(Conditional, dbCmd);
        db.Use("@Min", 3);
        Assert.Equal("Alice", db.Query<string>());
        Assert.Equal("Alice", await db.QueryAsync<string>(ct));
        var streamed = new List<string>();
        await foreach (var name in db.StreamQueryAsync<string>(ct))
            streamed.Add(name);
        Assert.Equal(["Alice"], streamed);

        using var plainCmd = cnn.CreateCommand();
        var idb = new QueryBuilderCommand<IDbCommand>(Conditional, new PlainCommand(plainCmd));
        idb.Use("@Min", 2);
        Assert.Equal(["Victor", "Alice"], idb.Query<List<string>>());
        Assert.Equal("Victor", await idb.QueryAsync<string>(ct));
    }

    // ---- the unbound builder over a connection ----

    [Fact]
    public async Task Unbound_builder_queries_through_the_interface_connection() {
        var ct = TestContext.Current.CancellationToken;
        var b = Conditional.StartBuilder();
        b.Use("@Min", 2);

        using var cnn = Db.GetConnection();
        Assert.Equal(["Victor", "Alice"], b.Query<List<string>>((IDbConnection)cnn));
        Assert.Equal("Victor", await b.QueryAsync<string>((IDbConnection)cnn, ct: ct));
        var streamed = new List<string>();
        await foreach (var name in b.StreamQueryAsync<string>(cnn, ct: ct))
            streamed.Add(name);
        Assert.Equal(["Victor", "Alice"], streamed);
    }

    [Fact]
    public void GetCommand_applies_the_transaction_and_timeout_on_both_kinds() {
        var b = Conditional.StartBuilder();
        b.Use("@Min", 1);

        using var cnn = Db.Open();
        using var tx = cnn.BeginTransaction();
        var cmd = QueryBuilderExtensions.GetCommand(Conditional, b.Variables, (DbConnection)cnn, tx, timeout: 7);
        Assert.Same(tx, cmd.Transaction);
        Assert.Equal(7, cmd.CommandTimeout);
        cmd.Dispose();

        var icmd = QueryBuilderExtensions.GetCommand(Conditional, b.Variables, (IDbConnection)cnn, tx, timeout: 9);
        Assert.Equal(9, icmd.CommandTimeout);
        icmd.Dispose();

        var count = new QueryCommand("SELECT COUNT(*) FROM Users WHERE ID >= @Min");
        Assert.Equal(3, count.ExecuteScalar<long, MinClass>(cnn, new MinClass { Min = 1 }, tx, 5));
        tx.Rollback();
    }

    // ---- the sql-string connection shortcuts ----

    [Fact]
    public async Task Connection_shortcuts_cover_the_remaining_overload_cells() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = Db.GetConnection();
        IDbConnection icnn = cnn;

        Assert.Equal(1, icnn.Execute("INSERT INTO Scratch (Val) VALUES (@Val)", new { Val = 300 }));

        using (var reader = icnn.ExecuteReader("SELECT Name FROM Users WHERE ID = @ID", out var cmd, new { ID = 1 })) {
            Assert.True(reader.Read());
            cmd.Dispose();
        }
        using (var reader = await icnn.ExecuteReaderAsync("SELECT Name FROM Users WHERE ID = @ID", out var cmd, new { ID = 2 }, ct: ct)) {
            Assert.True(await reader.ReadAsync(ct));
            cmd.Dispose();
        }
        using (var reader = await cnn.ExecuteReaderAsync("SELECT Name FROM Users WHERE ID = @ID", out var cmd, new { ID = 3 }, ct: ct)) {
            Assert.True(await reader.ReadAsync(ct));
            cmd.Dispose();
        }

        var refArgs = new MinArgs(1);
        using (var reader = icnn.ExecuteReader("SELECT Name FROM Users WHERE ID >= @Min", out var cmd, ref refArgs)) {
            Assert.True(reader.Read());
            cmd.Dispose();
        }
        using (var mr = icnn.ExecuteMultiReader(TwoSets, out var cmd, ref refArgs)) {
            Assert.Equal(3, mr.Query<List<long>>().Count);
            cmd.Dispose();
        }
        using (var mr = await icnn.ExecuteMultiReaderAsync(TwoSets, out var cmd, new MinArgs(1), ct: ct)) {
            Assert.Equal(3, (await mr.QueryAsync<List<long>>(ct)).Count);
            cmd.Dispose();
        }
    }
}
