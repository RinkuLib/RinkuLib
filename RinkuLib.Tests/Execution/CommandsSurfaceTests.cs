using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// The command-layer overload matrix: the object-typed parameter overloads, the learned-parser dispatch
/// branches, the multi-reader roads, the bound builder surface, and the connection shortcut variants.
/// </summary>
public class CommandsSurfaceTests(SqliteDb Db) : IClassFixture<SqliteDb> {
    const string TwoSets = "SELECT ID FROM Users ORDER BY ID; SELECT Name FROM Users ORDER BY ID";


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

    public record UserPair(long ID, string Name) : IDbReadable;

    [Fact]
    public async Task The_dispatch_prefers_the_settled_parser_and_relearns_when_a_param_unsettles() {
        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand("SELECT ID, Name FROM Users WHERE ID = @ID");

        using var cnn = Db.GetConnection();
        Assert.Equal("John", query.Query<UserPair>(cnn, new { ID = 1 }).Name); 
        Assert.Equal("Victor", query.Query<UserPair>(cnn, new { ID = 2 }).Name);      
        Assert.Equal("Alice", (await query.QueryAsync<UserPair>(cnn, new { ID = 3 }, ct: ct)).Name);
        Assert.Equal("John", query.Query<UserPair>((IDbConnection)cnn, new { ID = 1 }).Name);
        Assert.Equal("Victor", (await query.QueryAsync<UserPair>((IDbConnection)cnn, new { ID = 2 }, ct: ct)).Name);

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
            break;                              
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


    [Fact]
    public async Task MultiReader_query_covers_every_shape_road() {
        var query = new QueryCommand(TwoSets);
        using var cnn = Db.GetConnection();
        using (var mr = query.ExecuteMultiReader(cnn, out var cmd)) {
            Assert.Equal([1L, 2L, 3L], mr.Query<List<long>>());  
            Assert.Equal(["John", "Victor", "Alice"], mr.Query<IEnumerable<string>>()); 
            cmd.Dispose();
        }

        var ct = TestContext.Current.CancellationToken;
        using (var mr = await query.ExecuteMultiReaderAsync(cnn, out var cmd, ct: ct)) {
            Assert.Equal(1L, await mr.QueryAsync<long>(ct));      
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
        await foreach (var row in mr.StreamQueryAsync<UserPair>(ct: ct))
            objects.Add(row);
        Assert.Equal(3, objects.Count);
        await mr.DisposeAsync();
        await mr.DisposeAsync();      
        cmd.Dispose();

        var mr2 = await query.ExecuteMultiReaderAsync(cnn, out var cmd2, ct: ct);
        Assert.Equal(3, (await mr2.QueryAsync<List<UserPair>>(ct)).Count);
        await mr2.DisposeAsync();     
        cmd2.Dispose();
    }


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

    [Fact]
    public async Task A_plain_connection_travels_every_wrapped_command_road() {
        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand("SELECT ID, Name FROM Users WHERE ID >= ?@Min ORDER BY ID");
        using var real = Db.GetConnection();
        IDbConnection plain = new PlainConnection(real);

        Assert.Equal("John", query.Query<UserPair>(plain, new { Min = 1 }).Name);   
        Assert.Equal("Victor", query.Query<UserPair>(plain, new { Min = 2 }).Name);  
        Assert.Equal("Alice", (await query.QueryAsync<UserPair>(plain, new { Min = 3 }, ct: ct)).Name);

        int min = query.Mapper.GetIndex("@Min");
        Assert.True(query.Parameters.UpdateCache(min, InferedDbParamCache.Instance));
        query.Parameters.UpdateCachedIndexes();
        Assert.Equal("John", query.Query<UserPair>(plain, new { Min = 1 }).Name); 

        Assert.True(query.Parameters.UpdateCache(min, InferedDbParamCache.Instance));
        query.Parameters.UpdateCachedIndexes();
        Assert.Equal("Victor", (await query.QueryAsync<UserPair>(plain, new { Min = 2 }, ct: ct)).Name);

        var insert = new QueryCommand("INSERT INTO Scratch (Val) VALUES (@Val)");
        Assert.Equal(1, insert.Execute(plain, new { Val = 400 }));
        Assert.Equal(1, await insert.ExecuteAsync(plain, new { Val = 401 }, ct: ct));

        var count = new QueryCommand("SELECT COUNT(*) FROM Users WHERE ID >= @Min");
        Assert.Equal(3L, count.ExecuteScalar<long>(plain, new { Min = 1 }));
        Assert.Equal(2L, await count.ExecuteScalarAsync<long>(plain, new { Min = 2 }, ct: ct));
        var nulls = new QueryCommand("SELECT Email FROM Users WHERE ID = @ID");
        Assert.Null(nulls.ExecuteScalar<string>(plain, new { ID = 1 }));

        using (var reader = query.ExecuteReader(plain, out var cmd, new { Min = 1 })) {
            Assert.True(reader.Read());
            cmd.Dispose();
        }
        using (var reader = await query.ExecuteReaderAsync(plain, out var cmd, new { Min = 1 }, ct: ct)) {
            Assert.True(await reader.ReadAsync(ct));
            cmd.Dispose();
        }
    }

    /// <summary>
    /// The multi reader hands the connection to the reader it returns, so a failure before that reader
    /// exists leaves nobody holding the duty to close. A connection the call opened itself has to be closed
    /// on the way out, on each of the three roads that build one.
    /// </summary>
    [Fact]
    public async Task A_multi_reader_that_fails_to_execute_closes_the_connection_it_opened() {
        var ct = TestContext.Current.CancellationToken;
        var bad = new QueryCommand("SELECT ID FROM NoSuchTableAnywhere; SELECT 1");

        using (var cnn = Db.GetConnection()) {
            Assert.Equal(ConnectionState.Closed, cnn.State);
            Assert.ThrowsAny<Exception>(() => bad.ExecuteMultiReader(cnn, out _));
            Assert.Equal(ConnectionState.Closed, cnn.State);
        }

        using (var cnn = Db.GetConnection()) {
            await Assert.ThrowsAnyAsync<Exception>(async () => {
                using var mr = await bad.ExecuteMultiReaderAsync(cnn, out _, ct: ct);
            });
            Assert.Equal(ConnectionState.Closed, cnn.State);
        }

        using (var real = Db.GetConnection()) {
            IDbConnection plain = new PlainConnection(real);
            Assert.ThrowsAny<Exception>(() => bad.ExecuteMultiReader(plain, out _));
            Assert.Equal(ConnectionState.Closed, real.State);
        }
    }

    /// <summary>A connection the caller had already opened stays open, whatever the outcome.</summary>
    [Fact]
    public void A_multi_reader_failure_leaves_a_caller_opened_connection_alone() {
        var bad = new QueryCommand("SELECT ID FROM NoSuchTableAnywhere; SELECT 1");
        using var open = Db.Open();
        Assert.Equal(ConnectionState.Open, open.State);
        Assert.ThrowsAny<Exception>(() => bad.ExecuteMultiReader(open, out _));
        Assert.Equal(ConnectionState.Open, open.State);
    }

    [Fact]
    public async Task A_plain_connection_runs_the_multi_reader_and_its_plain_close() {
        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand(TwoSets);
        using var real = Db.GetConnection();
        IDbConnection plain = new PlainConnection(real);

        using (var mr = query.ExecuteMultiReader(plain, out var cmd)) {
            Assert.Equal([1L, 2L, 3L], mr.Query<List<long>>());
            Assert.Equal(3, mr.Query<List<string>>().Count);
            cmd.Dispose();
        }
        Assert.Equal(ConnectionState.Closed, real.State);

        var mr2 = await query.ExecuteMultiReaderAsync(plain, out var cmd2, ct: ct);
        Assert.Equal(1L, mr2.Query<long>());
        await mr2.DisposeAsync();         
        cmd2.Dispose();
        Assert.Equal(ConnectionState.Closed, real.State);
    }

    [Fact]
    public async Task MultiReader_streams_a_buffered_shape_and_skips_leading_empty_sets() {
        var ct = TestContext.Current.CancellationToken;
        var withInsert = new QueryCommand("INSERT INTO Scratch (Val) VALUES (500); SELECT ID FROM Users ORDER BY ID");
        using var cnn = Db.GetConnection();
        using (var mr = withInsert.ExecuteMultiReader(cnn, out var cmd)) {
            var sets = new List<List<long>>();
            await foreach (var list in mr.StreamQueryAsync<List<long>>(ct: ct)) 
                sets.Add(list);
            Assert.Single(sets);
            Assert.Equal([1L, 2L, 3L], sets[0]);
            cmd.Dispose();
        }
    }

    [Fact]
    public void MultiReader_lifecycle_members_forward_and_the_base_dispose_routes() {
        var query = new QueryCommand(TwoSets);
        using var cnn = Db.GetConnection();
        var mr = query.ExecuteMultiReader(cnn, out var cmd);
        Assert.NotNull(mr.GetSchemaTable());
        ((DbDataReader)mr).Dispose();    
        cmd.Dispose();

        var mr2 = query.ExecuteMultiReader(cnn, out var cmd2);
        ((IDisposable)mr2).Dispose();   
        cmd2.Dispose();
    }

    [Fact]
    public async Task MultiReader_close_async_forwards() {
        var query = new QueryCommand(TwoSets);
        using var cnn = Db.GetConnection();
        var mr = query.ExecuteMultiReader(cnn, out var cmd);
        await mr.CloseAsync();
        mr.Dispose();
        cmd.Dispose();
    }

    [Fact]
    public async Task The_learning_road_hands_out_lazy_and_default_shapes() {
        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand("SELECT ID, Name FROM Users WHERE ID >= @Min ORDER BY ID");
        using var cnn = Db.GetConnection();

        var lazy = query.Query<IEnumerable<UserPair>>(cnn, new { Min = 1 });
        Assert.Equal(3, lazy.Count());
        var lazyAsync = await query.QueryAsync<IEnumerable<UserPair>>(cnn, new { Min = 2 }, ct: ct);
        Assert.Equal(2, lazyAsync.Count());
        Assert.False(query.Query<Optional<string>>(cnn, new { Min = 99 }).HasValue);
        Assert.False((await query.QueryAsync<Optional<string>>(cnn, new { Min = 99 }, ct: ct)).HasValue);

        var wrapped = new QueryCommand("SELECT ID, Name FROM Users WHERE ID >= @Min ORDER BY ID");
        IDbConnection plain = new PlainConnection(Db.GetConnection());
        var wrappedLazy = wrapped.Query<IEnumerable<UserPair>>(plain, new { Min = 1 });
        Assert.Equal(3, wrappedLazy.Count());
        Assert.False(wrapped.Query<Optional<string>>(plain, new { Min = 99 }).HasValue);
        Assert.Equal(2, wrapped.Query<List<UserPair>>(plain, new { Min = 2 }).Count);
        ((PlainConnection)plain).Inner.Dispose();
    }

    [Fact]
    public async Task Streams_carry_buffered_shapes_and_survive_failing_opens() {
        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand("SELECT ID FROM Users WHERE ID >= @Min ORDER BY ID");
        using var cnn = Db.GetConnection();

        var cold = new List<List<long>>();
        await foreach (var list in query.StreamQueryAsync<List<long>>(cnn, new { Min = 1 }, ct: ct))
            cold.Add(list);                          
        Assert.Equal([1L, 2L, 3L], Assert.Single(cold));

        var warm = new List<List<long>>();
        await foreach (var list in query.StreamQueryAsync<List<long>>(cnn, new { Min = 2 }, ct: ct))
            warm.Add(list);                                 
        Assert.Equal([2L, 3L], Assert.Single(warm));

        await foreach (var _ in query.StreamQueryAsync<List<long>>(cnn, new { Min = 99 }, ct: ct))
            Assert.Fail("no list should surface for an empty result");

        using var bad = new SqliteConnection("Data Source=Z:\\rinku-no-such-dir\\missing.db;Mode=ReadOnly");
        await Assert.ThrowsAnyAsync<Exception>(async () => {
            await foreach (var _ in query.StreamQueryAsync<List<long>>(bad, new { Min = 1 }, ct: ct)) { }
        });                                          
        var fresh = new QueryCommand("SELECT ID FROM Users ORDER BY ID");
        await Assert.ThrowsAnyAsync<Exception>(async () => {
            await foreach (var _ in fresh.StreamQueryAsync<long>(bad, ct: ct)) { }
        });                                         
        await Assert.ThrowsAnyAsync<Exception>(() => fresh.QueryAsync<UserPair>(bad, ct: ct));
        Assert.ThrowsAny<Exception>(() => fresh.Query<UserPair>(bad));
    }


    [Fact]
    public async Task MultiReader_disposes_its_command_when_it_owns_it() {
        var query = new QueryCommand(TwoSets);
        using var cnn = Db.Open();

        var cmd = cnn.CreateCommand();
        var usage = new bool[query.Mapper.Count];
        query.SetCommand(cmd, null, usage);
        var mr = cmd.ExecuteMultiReader(query, [.. usage], disposeCommand: true);
        Assert.Equal(3, mr.Query<List<long>>().Count);
        mr.Dispose();

        var cmd2 = cnn.CreateCommand();
        query.SetCommand(cmd2, null, usage);
        var mr2 = await cmd2.ExecuteMultiReaderAsync(query, [.. usage], disposeCommand: true,
            ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, mr2.Query<List<long>>().Count);
        await mr2.DisposeAsync();
    }

    [Fact]
    public async Task A_directly_built_multi_reader_honors_the_was_closed_flag() {
        var query = new QueryCommand("SELECT ID FROM Users ORDER BY ID");
        var cnn = Db.Open();
        var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT ID FROM Users ORDER BY ID";
        var usage = new bool[query.Mapper.Count];
        var mr = new MultiReader(usage, query, cmd.ExecuteReader(), cmd, disposeCmd: true, wasClosed: true);
        Assert.Equal(3, mr.Query<List<long>>().Count);
        mr.Dispose();
        Assert.Equal(ConnectionState.Closed, cnn.State);
        cnn.Dispose();

        var cnn2 = Db.Open();
        var cmd2 = cnn2.CreateCommand();
        cmd2.CommandText = "SELECT ID FROM Users ORDER BY ID";
        var mr2 = new MultiReader(usage, query, cmd2.ExecuteReader(), cmd2, disposeCmd: true, wasClosed: true);
        await mr2.DisposeAsync();    
        Assert.Equal(ConnectionState.Closed, cnn2.State);
        cnn2.Dispose();

        var cnn3 = Db.Open();
        var cmd3 = cnn3.CreateCommand();
        cmd3.CommandText = "SELECT ID FROM Users ORDER BY ID";
        IDbCommand plain3 = new PlainCommand(cmd3);
        var mr3 = new MultiReader(usage, query, cmd3.ExecuteReader(), plain3, disposeCmd: false, wasClosed: true);
        await mr3.DisposeAsync(); 
        Assert.Equal(ConnectionState.Closed, cnn3.State);
        cnn3.Dispose();
    }

    [Fact]
    public async Task MultiReader_forwards_every_reader_member() {
        using (var setup = Db.Open()) {
            using var ddl = setup.CreateCommand();
            ddl.CommandText = """
                CREATE TABLE IF NOT EXISTS TypedM (I INTEGER, S TEXT, D REAL, C TEXT, DT TEXT, G TEXT);
                DELETE FROM TypedM;
                INSERT INTO TypedM VALUES (1, 'text', 2.5, 'a', '2024-05-01 13:30:15',
                    '33221100-5544-7766-8899-aabbccddeeff');
                """;
            ddl.ExecuteNonQuery();
        }

        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand("SELECT I, S, D, C, DT, G FROM TypedM; SELECT ID FROM Users");
        using var cnn = Db.GetConnection();
        var mr = query.ExecuteMultiReader(cnn, out var cmd);
        Assert.True(mr.Read());
        Assert.Equal(1, mr.GetInt32(0));
        Assert.Equal((short)1, mr.GetInt16(0));
        Assert.Equal(2.5m, mr.GetDecimal(2));
        Assert.Equal(2.5f, mr.GetFloat(2));
        Assert.Equal('a', mr.GetChar(3));
        var chars = new char[1];
        Assert.Equal(1, mr.GetChars(3, 0, chars, 0, 1));
        Assert.Equal(new DateTime(2024, 5, 1, 13, 30, 15), mr.GetDateTime(4));
        Assert.Equal(Guid.Parse("33221100-5544-7766-8899-aabbccddeeff"), mr.GetGuid(5));
        Assert.Equal("INTEGER", mr.GetDataTypeName(0));
        Assert.Equal(1L, mr.GetValue(0));
        Assert.Equal(1L, mr.GetFieldValue<long>(0));
        Assert.Equal(1L, await mr.GetFieldValueAsync<long>(0, ct));
        Assert.Equal(typeof(long), mr.GetProviderSpecificFieldType(0));
        Assert.Equal(1L, mr.GetProviderSpecificValue(0));
        var provider = new object[6];
        Assert.Equal(6, mr.GetProviderSpecificValues(provider));
        Assert.Equal((byte)1, mr.GetByte(0));
        Assert.False(await mr.IsDBNullAsync(0, ct));
        Assert.NotEmpty(await mr.GetColumnSchemaAsync(ct));
        Assert.NotNull(await mr.GetSchemaTableAsync(ct));
        Assert.Equal(6, mr.VisibleFieldCount);
        using (var stream = mr.GetStream(1))
            Assert.True(stream.ReadByte() >= 0);
        using (var text = mr.GetTextReader(1))
            Assert.Equal("text", text.ReadToEnd());

        int forwarded = mr.RecordsAffected;
        using (var raw = Db.Open()) {
            using var direct = raw.CreateCommand();
            direct.CommandText = "SELECT I, S, D, C, DT, G FROM TypedM";
            using var reader = direct.ExecuteReader();
            Assert.Equal(reader.RecordsAffected, forwarded);
        }

        Assert.False(mr.Read());
        mr.Close();
        mr.Dispose();
        cmd.Dispose();
    }

    [Fact]
    public void MultiReader_enumerates_the_rows_of_the_set_it_is_on() {
        var query = new QueryCommand(TwoSets);
        using var cnn = Db.GetConnection();
        using var mr = query.ExecuteMultiReader(cnn, out var cmd);

        var ids = new List<long>();
        foreach (System.Data.Common.DbDataRecord record in mr)
            ids.Add(record.GetInt64(0));
        Assert.Equal([1L, 2L, 3L], ids);
        cmd.Dispose();
    }

    [Fact]
    public async Task MultiReader_reads_blobs_and_skips_leading_writes_on_each_road() {
        using (var setup = Db.Open()) {
            using var ddl = setup.CreateCommand();
            ddl.CommandText = """
                CREATE TABLE IF NOT EXISTS BlobM (B BLOB);
                DELETE FROM BlobM;
                INSERT INTO BlobM VALUES (X'0102');
                """;
            ddl.ExecuteNonQuery();
        }
        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand("SELECT B FROM BlobM");
        using var cnn = Db.GetConnection();
        using (var mr = query.ExecuteMultiReader(cnn, out var cmd)) {
            Assert.True(mr.Read());
            var buffer = new byte[2];
            Assert.Equal(2, mr.GetBytes(0, 0, buffer, 0, 2));
            Assert.Equal([1, 2], buffer);
            cmd.Dispose();
        }

        var withInsert = new QueryCommand("INSERT INTO Scratch (Val) VALUES (600); SELECT ID FROM Users ORDER BY ID");
        using (var mr = withInsert.ExecuteMultiReader(cnn, out var cmd)) {
            Assert.Equal([1L, 2L, 3L], mr.Query<List<long>>()); 
            cmd.Dispose();
        }
        using (var mr = await withInsert.ExecuteMultiReaderAsync(cnn, out var cmd, ct: ct)) {
            Assert.Equal(3, (await mr.QueryAsync<List<long>>(ct)).Count);
            cmd.Dispose();
        }
    }

    [Fact]
    public void Builder_index_based_use_updates_spreads_and_removes_on_null() {
        var query = new QueryCommand("SELECT * FROM t WHERE b IN (?@Ids_X) AND c > ?@Min");
        var b = new QueryBuilderCommand<FakeCommand>(query, new FakeCommand());
        int ids = query.Mapper.GetIndex("@Ids"), min = query.Mapper.GetIndex("@Min");

        Assert.True(b.Use(ids, new[] { 1, 2 }));
        Assert.True(b.Use(min, 3));
        Assert.Equal("SELECT * FROM t WHERE b IN (@Ids_1, @Ids_2) AND c > @Min", b.GetQueryText());
        Assert.True(b.Use(ids, new[] { 4, 5, 6 }));   
        Assert.Equal("SELECT * FROM t WHERE b IN (@Ids_1, @Ids_2, @Ids_3) AND c > @Min", b.GetQueryText());
        Assert.True(b.Use(min, null));       
        Assert.True(b.Use(ids, null));
        Assert.Equal("SELECT * FROM t", b.GetQueryText());

        ((IQueryBuilder)b).Use(min, 9);           
        Assert.Equal(9, Val(b[min]));

        var unbound = query.StartBuilder();
        ((IQueryBuilder)unbound).Use(min, 4);
        Assert.Equal(4, unbound[min]);
    }

    [Fact]
    public async Task An_empty_schema_set_is_skipped_on_every_query_road() {
        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand("SELECT V FROM t");

        static DbDataReader TwoSetReader() {
            var ds = new DataSet();
            var empty = new DataTable("none");    
            var data = new DataTable("data");
            data.Columns.Add("V", typeof(int));
            data.Rows.Add(7);
            data.Rows.Add(8);
            ds.Tables.Add(empty);
            ds.Tables.Add(data);
            return ds.CreateDataReader();
        }

        var usage = new bool[query.Mapper.Count];
        using (var mr = new MultiReader(usage, query, TwoSetReader(), new LegacyCommand(), false, false))
            Assert.Equal([7, 8], mr.Query<List<int>>());
        using (var mr = new MultiReader(usage, query, TwoSetReader(), new LegacyCommand(), false, false))
            Assert.Equal(2, (await mr.QueryAsync<List<int>>(ct)).Count);
        using (var mr = new MultiReader(usage, query, TwoSetReader(), new LegacyCommand(), false, false)) {
            var streamed = new List<int>();
            await foreach (var v in mr.StreamQueryAsync<int>(ct: ct))
                streamed.Add(v);
            Assert.Equal([7, 8], streamed);
        }
    }

    [Fact]
    public async Task A_multi_reader_learns_unsettled_parameters_on_both_command_kinds() {
        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand("SELECT ID FROM Users WHERE ID >= ?@Min; SELECT Name FROM Users");
        using var cnn = Db.GetConnection();
        using (var mr = query.ExecuteMultiReader(cnn, out var cmd, new { Min = 2 })) {
            Assert.Equal([2L, 3L], mr.Query<List<long>>());
            cmd.Dispose();
        }
        var query2 = new QueryCommand("SELECT ID FROM Users WHERE ID >= ?@Min; SELECT Name FROM Users");
        IDbConnection plain = new PlainConnection(Db.GetConnection());
        using (var mr = query2.ExecuteMultiReader(plain, out var cmd, new { Min = 3 })) {
            Assert.Equal([3L], mr.Query<List<long>>());
            cmd.Dispose();
        }
        var query3 = new QueryCommand("SELECT ID FROM Users WHERE ID >= ?@Min; SELECT Name FROM Users");
        using (var mr = await query3.ExecuteMultiReaderAsync(cnn, out var cmd, new { Min = 1 }, ct: ct)) {
            Assert.Equal(3, mr.Query<List<long>>().Count);
            cmd.Dispose();
        }
        ((PlainConnection)plain).Inner.Dispose();
    }

    [Fact]
    public async Task Warm_halves_of_the_remaining_roads() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = Db.GetConnection();

        var query = new QueryCommand("SELECT ID, Name FROM Users WHERE ID = @ID");
        IDbConnection plain = new PlainConnection(Db.GetConnection());
        Assert.Equal("John", query.Query<UserPair>(plain, new { ID = 1 }).Name);
        Assert.Equal("Victor", (await query.QueryAsync<UserPair>(plain, new { ID = 2 }, ct: ct)).Name);

        var parser = new CachedTypeParser<UserPair>();
        Assert.Equal(CommandBehavior.SingleResult, parser.Behavior);
        using (var open = Db.Open()) {
            using var cmd = open.CreateCommand();
            var map = new bool[query.Mapper.Count];
            query.SetCommand(cmd, new { ID = 1 }, map);
            Assert.Equal("John", parser.Query(cmd, query).Name);
        }
        _ = parser.Behavior;               

        var q2 = new QueryCommand("SELECT Name FROM Users WHERE ID >= @Min ORDER BY ID");
        int min = q2.Mapper.GetIndex("@Min");
        var b = q2.StartBuilder();
        b.Use("@Min", 2);
        Assert.Equal("Victor", b.Query<string>(cnn));
        Assert.Equal("Victor", b.Query<string>(cnn));
        Assert.Equal("Victor", await b.QueryAsync<string>(cnn, ct: ct));
        var s1 = new List<string>();
        await foreach (var n in b.StreamQueryAsync<string>(cnn, ct: ct))
            s1.Add(n);
        Assert.Equal(2, s1.Count);
        q2.Parameters.UpdateCache(min, InferedDbParamCache.Instance);
        q2.Parameters.UpdateCachedIndexes();
        Assert.Equal("Victor", b.Query<string>(cnn));
        q2.Parameters.UpdateCache(min, InferedDbParamCache.Instance);
        q2.Parameters.UpdateCachedIndexes();
        Assert.Equal("Victor", await b.QueryAsync<string>(cnn, ct: ct));

        var realTx = Db.Open();
        IDbConnection plainTx = new PlainConnection(realTx);
        using (var tx = plainTx.BeginTransaction()) {
            var insert = new QueryCommand("INSERT INTO Scratch (Val) VALUES (@Val)");
            Assert.Equal(1, insert.Execute(plainTx, new { Val = 700 }, tx, 5));
            tx.Rollback();
        }
        realTx.Dispose();
        ((PlainConnection)plain).Inner.Dispose();
    }

    const string MinSql = "SELECT Name FROM Users WHERE ID >= @Min ORDER BY ID";

    /// <summary>A fresh command whose parser is learned but whose parameters are deliberately unsettled,
    /// the state that sends a run down the relearn road: a known shape that must still teach the cache.</summary>
    static QueryCommand Relearning(Action<QueryCommand> warmUp) {
        var query = new QueryCommand(MinSql);
        warmUp(query);
        int min = query.Mapper.GetIndex("@Min");
        Assert.True(query.Parameters.UpdateCache(min, InferedDbParamCache.Instance));
        query.Parameters.UpdateCachedIndexes();
        var probe = new object?[query.Mapper.Count];
        probe[min] = 1;
        Assert.True(query.Parameters.NeedToCache(probe));
        Assert.False(query.TryGetCachedParser<string>(probe, out var parser));
        Assert.NotNull(parser);
        return query;
    }

    [Fact]
    public async Task The_relearn_road_runs_on_every_builder_query_overload() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = Db.Open();

        DbCommand Fresh() {
            var c = cnn.CreateCommand();
            return c;
        }

        var q1 = Relearning(q => {
            using var warm = Fresh();
            var b = new QueryBuilderCommand<DbCommand>(q, warm);
            b.Use("@Min", 1);
            Assert.Equal("John", b.Query<string>());
        });
        using (var c = Fresh()) {
            var b = new QueryBuilderCommand<DbCommand>(q1, c);
            b.Use("@Min", 1);
            Assert.False(q1.TryGetCachedParser<string>(b.Variables, out var staged));
            Assert.NotNull(staged);
            Assert.Equal("John", await b.QueryAsync<string>(ct));
        }

        var q2 = Relearning(q => {
            using var warm = Fresh();
            var b = new QueryBuilderCommand<IDbCommand>(q, new PlainCommand(warm));
            b.Use("@Min", 1);
            Assert.Equal("John", b.Query<string>());
        });
        using (var c = Fresh()) {
            var b = new QueryBuilderCommand<IDbCommand>(q2, new PlainCommand(c));
            b.Use("@Min", 1);
            Assert.Equal("John", b.Query<string>());
        }
        var q3 = Relearning(q => {
            using var warm = Fresh();
            var b = new QueryBuilderCommand<IDbCommand>(q, new PlainCommand(warm));
            b.Use("@Min", 1);
            Assert.Equal("John", b.Query<string>());
        });
        using (var c = Fresh()) {
            var b = new QueryBuilderCommand<IDbCommand>(q3, new PlainCommand(c));
            b.Use("@Min", 1);
            Assert.Equal("John", await b.QueryAsync<string>(ct));
        }

        var q4 = Relearning(q => {
            var b = q.StartBuilder();
            b.Use("@Min", 1);
            Assert.Equal("John", b.Query<string>(cnn));
        });
        var streamed = new List<string>();
        var b4 = q4.StartBuilder();
        b4.Use("@Min", 1);
        await foreach (var name in b4.StreamQueryAsync<string>(cnn, ct: ct))
            streamed.Add(name);
        Assert.Equal(3, streamed.Count);
    }

    [Fact]
    public async Task A_bound_builder_whose_shape_is_new_learns_on_an_async_query() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = Db.Open();

        using (var c = cnn.CreateCommand()) {
            var q = new QueryCommand(MinSql);
            var b = new QueryBuilderCommand<DbCommand>(q, c);
            b.Use("@Min", 1);
            Assert.False(q.TryGetCachedParser<string>(b.Variables, out var none));
            Assert.Null(none);
            Assert.Equal("John", await b.QueryAsync<string>(ct));
        }

        using (var c = cnn.CreateCommand()) {
            var q = new QueryCommand(MinSql);
            var b = new QueryBuilderCommand<IDbCommand>(q, new PlainCommand(c));
            b.Use("@Min", 1);
            Assert.False(q.TryGetCachedParser<string>(b.Variables, out var none));
            Assert.Null(none);
            Assert.Equal("John", await b.QueryAsync<string>(ct));
        }
    }

    [Fact]
    public async Task A_cold_async_query_on_a_plain_command_takes_the_linker_road() {
        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand(MinSql);
        IDbConnection plain = new PlainConnection(Db.GetConnection());
        Assert.Equal("John", await query.QueryAsync<string>(plain, new { Min = 1 }, ct: ct));
        ((PlainConnection)plain).Inner.Dispose();
    }

    [Fact]
    public async Task A_self_built_parser_async_on_a_plain_command_falls_back_to_the_sync_road() {
        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand("SELECT ID, Name FROM Users WHERE ID = @ID");
        using var cnn = Db.Open();
        using var inner = cnn.CreateCommand();
        var map = new bool[query.Mapper.Count];
        query.SetCommand(inner, new { ID = 1 }, map);

        var parser = new CachedTypeParser<UserPair>();
        IDbCommand plain = new PlainCommand(inner);
        Assert.Equal("John", (await parser.QueryAsync(plain, disposeCommand: false, ct: ct)).Name);
    }

    [Fact]
    public void Bool_condition_keys_are_used_and_unused_by_index_and_name() {
        var query = new QueryCommand("SELECT * FROM t WHERE /*F*/a = 1 AND b = ?@V");
        var b = new QueryBuilderCommand<FakeCommand>(query, new FakeCommand());
        int f = query.Mapper.GetIndex("F");

        b.Use(f);
        Assert.Equal("SELECT * FROM t WHERE a = 1", b.GetQueryText());
        b.UnUse(f);
        Assert.Equal("SELECT * FROM t", b.GetQueryText());

        Assert.False(b.Use("@V"));
        Assert.False(b.UnUse("@V"));
        Assert.False(b.Use("@V".AsSpan()));
        Assert.False(b.UnUse("@V".AsSpan()));

        var unbound = query.StartBuilder();
        Assert.False(unbound.UnUse("@V"));
        Assert.True(unbound.Use("F"));
        Assert.True(unbound.UnUse("F"));
    }

    [Fact]
    public void A_base_handler_slot_is_bound_without_a_parameter() {
        var query = new QueryCommand("SELECT * FROM t ORDER BY @Sort_R");
        var b = new QueryBuilderCommand<FakeCommand>(query, new FakeCommand());
        Assert.True(b.Use("@Sort", "Price"));
        Assert.Equal("SELECT * FROM t ORDER BY Price", b.GetQueryText());
        Assert.True(b.Use("@Sort", "Name"));
        Assert.Equal("SELECT * FROM t ORDER BY Name", b.GetQueryText());
        Assert.Equal(0, b.Command.Parameters.Count);
    }

    [Fact]
    public void Removing_a_spread_slot_by_index_drops_its_parameters() {
        var query = new QueryCommand("SELECT * FROM t WHERE b IN (?@Ids_X)");
        var b = new QueryBuilderCommand<FakeCommand>(query, new FakeCommand());
        int ids = query.Mapper.GetIndex("@Ids");
        Assert.True(b.Use(ids, new[] { 1, 2 }));
        Assert.Equal(2, b.Command.Parameters.Count);
        b.Remove(ids);
        Assert.Equal(0, b.Command.Parameters.Count);
        Assert.Null(b[ids]);
        b.Remove(ids);
        Assert.Equal(0, b.Command.Parameters.Count);
    }

    [Fact]
    public void The_presence_map_marks_only_supplied_slots() {
        var query = new QueryCommand("SELECT * FROM t WHERE a = ?@A AND b = ?@B");
        var vars = new object?[query.Mapper.Count];
        vars[query.Mapper.GetIndex("@A")] = 1;
        var map = vars.ToBoolArr();
        Assert.True(map[query.Mapper.GetIndex("@A")]);
        Assert.False(map[query.Mapper.GetIndex("@B")]);
        Assert.Equal(vars.Length, map.Length);
    }

    [Fact]
    public async Task A_connection_left_broken_by_a_failed_open_is_closed_on_the_way_out() {
        var ct = TestContext.Current.CancellationToken;
        var parser = new CachedTypeParser<UserPair>();

        var brokenForDb = new BrokenConnection();
        using (var cmd = (FakeCommand)brokenForDb.CreateCommand()) {
            Assert.ThrowsAny<Exception>(() => parser.Query(cmd, disposeCommand: false));
            Assert.True(brokenForDb.Closed);
        }

        var brokenForAsync = new BrokenConnection();
        using (var cmd = (FakeCommand)brokenForAsync.CreateCommand()) {
            await Assert.ThrowsAnyAsync<Exception>(() => parser.QueryAsync(cmd, disposeCommand: false, ct: ct));
            Assert.True(brokenForAsync.Closed);
        }

        var brokenForPlain = new BrokenConnection();
        IDbCommand plain = new BrokenPlainCommand(brokenForPlain);
        Assert.ThrowsAny<Exception>(() => parser.Query(plain, disposeCommand: false));
        Assert.True(brokenForPlain.Closed);
    }

    [Fact]
    public void GetCommand_over_a_plain_connection_binds_through_the_interface() {
        var query = new QueryCommand(MinSql);
        var b = query.StartBuilder();
        b.Use("@Min", 2);
        var real = Db.Open();
        IDbConnection plain = new PlainConnection(real);
        var cmd = QueryBuilderExtensions.GetCommand(query, b.Variables, plain, null, 11);
        Assert.Equal(11, cmd.CommandTimeout);
        Assert.Equal("SELECT Name FROM Users WHERE ID >= @Min ORDER BY ID", cmd.CommandText);
        Assert.Equal(1, cmd.Parameters.Count);
        cmd.Dispose();
        real.Dispose();
    }

    [Fact]
    public void Removing_by_a_negative_or_condition_index_is_a_no_op() {
        var query = new QueryCommand("SELECT * FROM t WHERE /*F*/a = 1 AND c > ?@Min");
        var b = new QueryBuilderCommand<FakeCommand>(query, new FakeCommand());
        b.Remove(-1);
        Assert.True(b.Use("F"));
        b.Remove(query.Mapper.GetIndex("F"));
        Assert.Equal("SELECT * FROM t", b.GetQueryText());

        var unbound = query.StartBuilder();
        Assert.False(unbound.UnUse("@Min".AsSpan()));
        Assert.True(unbound.Use("F".AsSpan()));
        Assert.True(unbound.UnUse("F".AsSpan()));
    }

    [Fact]
    public void A_type_schema_is_exposed_for_its_shape() {
        var cols = TypeSchema<UserPair>.Schema;
        Assert.Equal(["ID", "Name"], cols.Select(c => c.Name));
    }

    [Fact]
    public void Index_based_variable_updates_replace_in_place() {
        var query = new QueryCommand("SELECT * FROM t WHERE c > ?@Min");
        var b = new QueryBuilderCommand<FakeCommand>(query, new FakeCommand());
        int min = query.Mapper.GetIndex("@Min");
        Assert.True(b.Use(min, 1));
        Assert.True(b.Use(min, 2));      
        Assert.Equal(2, Val(b[min]));
        Assert.True(b.Use(min, null));
        Assert.True(b.Use(min, null));   
        Assert.Null(b[min]);
    }

    [Fact]
    public async Task A_cold_async_lazy_query_learns_while_streaming() {
        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand("SELECT ID, Name FROM Users ORDER BY ID");
        using var cnn = Db.GetConnection();
        var rows = await query.QueryAsync<IEnumerable<UserPair>>(cnn, ct: ct);
        Assert.Equal(3, rows.Count());
        Assert.Equal(ConnectionState.Closed, cnn.State);
    }

    sealed class CancelThrowingCommand : IDbCommand {
        public string? CommandText { get; set; }
        public int CommandTimeout { get; set; }
        public CommandType CommandType { get; set; }
        public IDbConnection? Connection { get; set; }
        public IDataParameterCollection Parameters { get; } = new LegacyParameterCollection();
        public IDbTransaction? Transaction { get; set; }
        public UpdateRowSource UpdatedRowSource { get; set; }
        public void Cancel() => throw new InvalidOperationException("cancel refused");
        public IDbDataParameter CreateParameter() => throw new NotSupportedException();
        public void Dispose() { }
        public int ExecuteNonQuery() => 0;
        public IDataReader ExecuteReader() => throw new NotSupportedException();
        public IDataReader ExecuteReader(CommandBehavior behavior) => throw new NotSupportedException();
        public object? ExecuteScalar() => null;
        public void Prepare() { }
    }

    [Fact]
    public async Task A_refusing_cancel_does_not_break_disposal() {
        var query = new QueryCommand("SELECT ID FROM Users");
        var usage = new bool[query.Mapper.Count];
        var mr = new MultiReader(usage, query, Rows.Reader([new("ID", typeof(long), false)], [1L]), new CancelThrowingCommand(), disposeCmd: false, wasClosed: true);
        mr.Dispose();            

        var mr2 = new MultiReader(usage, query, Rows.Reader([new("ID", typeof(long), false)], [1L]), new CancelThrowingCommand(), disposeCmd: false, wasClosed: true);
        await mr2.DisposeAsync();
    }


    [Fact]
    public async Task Builder_command_queries_reuse_and_relearn_their_parser() {
        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand("SELECT Name FROM Users WHERE ID >= @Min ORDER BY ID");
        using var cnn = Db.Open();
        int min = query.Mapper.GetIndex("@Min");

        using var dbCmd = cnn.CreateCommand();
        var db = new QueryBuilderCommand<DbCommand>(query, dbCmd);
        db.Use("@Min", 1);
        Assert.Equal("John", db.Query<string>());                       
        query.UpdateCache(dbCmd);                                       
        Assert.Equal("John", db.Query<string>());                       
        Assert.Equal("John", await db.QueryAsync<string>(ct));          
        query.Parameters.UpdateCache(min, InferedDbParamCache.Instance);
        query.Parameters.UpdateCachedIndexes();
        Assert.Equal("John", db.Query<string>());                       
        query.Parameters.UpdateCache(min, InferedDbParamCache.Instance);
        query.Parameters.UpdateCachedIndexes();
        Assert.Equal("John", await db.QueryAsync<string>(ct));          
        var streamed = new List<string>();
        await foreach (var name in db.StreamQueryAsync<string>(ct))     
            streamed.Add(name);
        Assert.Equal(3, streamed.Count);

        var coldQuery = new QueryCommand("SELECT Name FROM Users WHERE ID >= @Min ORDER BY Name");
        using var coldCmd = cnn.CreateCommand();
        var coldBuilder = new QueryBuilderCommand<DbCommand>(coldQuery, coldCmd);
        coldBuilder.Use("@Min", 1);
        var coldStream = new List<string>();
        await foreach (var name in coldBuilder.StreamQueryAsync<string>(ct))  
            coldStream.Add(name);
        Assert.Equal(3, coldStream.Count);
        coldQuery.Parameters.UpdateCache(coldQuery.Mapper.GetIndex("@Min"), InferedDbParamCache.Instance);
        coldQuery.Parameters.UpdateCachedIndexes();
        var middleStream = new List<string>();
        await foreach (var name in coldBuilder.StreamQueryAsync<string>(ct))
            middleStream.Add(name);
        Assert.Equal(3, middleStream.Count);

        using var plainCmd = cnn.CreateCommand();
        var idb = new QueryBuilderCommand<IDbCommand>(query, new PlainCommand(plainCmd));
        idb.Use("@Min", 2);
        Assert.Equal("Victor", idb.Query<string>());
        query.UpdateCache(idb.Command);                               
        Assert.Equal("Victor", idb.Query<string>());                 
        Assert.Equal("Victor", await idb.QueryAsync<string>(ct));   
        query.Parameters.UpdateCache(min, InferedDbParamCache.Instance);
        query.Parameters.UpdateCachedIndexes();
        Assert.Equal("Victor", idb.Query<string>());                 
        query.Parameters.UpdateCache(min, InferedDbParamCache.Instance);
        query.Parameters.UpdateCachedIndexes();
        Assert.Equal("Victor", await idb.QueryAsync<string>(ct));  
    }

    [Fact]
    public async Task Unbound_builder_over_the_interface_connection_reuses_and_relearns() {
        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand("SELECT Name FROM Users WHERE ID >= @Min ORDER BY ID");
        int min = query.Mapper.GetIndex("@Min");
        var b = query.StartBuilder();
        b.Use("@Min", 3);
        using var cnn = Db.GetConnection();
        IDbConnection icnn = cnn;

        Assert.Equal("Alice", b.Query<string>(icnn));                
        Assert.Equal("Alice", b.Query<string>(icnn));            
        Assert.Equal("Alice", await b.QueryAsync<string>(icnn, ct: ct));
        query.Parameters.UpdateCache(min, InferedDbParamCache.Instance);
        query.Parameters.UpdateCachedIndexes();
        Assert.Equal("Alice", b.Query<string>(icnn));              
        query.Parameters.UpdateCache(min, InferedDbParamCache.Instance);
        query.Parameters.UpdateCachedIndexes();
        Assert.Equal("Alice", await b.QueryAsync<string>(icnn, ct: ct));
    }

    class SpreadCondArgs {
        [ForBoolCond] public bool F;
        public int[]? Ids { get; set; }
        public int? Min { get; set; }
    }

    [Fact]
    public void Bound_builder_UseWith_updates_spreads_and_conditions() {
        var query = new QueryCommand("SELECT * FROM t WHERE /*F*/a = 1 AND b IN (?@Ids_X) AND c > ?@Min");
        var b = new QueryBuilderCommand<FakeCommand>(query, new FakeCommand());
        b.UseWith(new SpreadCondArgs { F = true, Ids = [1, 2], Min = 3 });
        Assert.Equal("SELECT * FROM t WHERE a = 1 AND b IN (@Ids_1, @Ids_2) AND c > @Min", b.GetQueryText());
        b.UseWith(new SpreadCondArgs());
        Assert.Equal("SELECT * FROM t", b.GetQueryText());
    }

    [Fact]
    public async Task A_self_built_cached_parser_reuses_its_parser_through_every_overload() {
        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand("SELECT ID, Name FROM Users WHERE ID = @ID");
        using var cnn = Db.Open();

        SqliteCommand Make(int idVal) {
            var cmd = cnn.CreateCommand();
            var map = new bool[query.Mapper.Count];
            query.SetCommand(cmd, new { ID = idVal }, map);
            return cmd;
        }

        var idbAsync = new CachedTypeParser<UserPair>();
        Assert.Equal("John", (await idbAsync.QueryAsync((IDbCommand)Make(1), disposeCommand: true, ct: ct)).Name);
        Assert.Equal("Victor", (await idbAsync.QueryAsync((IDbCommand)Make(2), disposeCommand: true, ct: ct)).Name);

        var withCache = new CachedTypeParser<UserPair>();
        Assert.Equal("John", withCache.Query(Make(1), query, disposeCommand: true).Name);
        Assert.Equal("Victor", withCache.Query(Make(2), query, disposeCommand: true).Name);  
        Assert.Equal("Alice", withCache.Query((IDbCommand)Make(3), query, disposeCommand: true).Name);
        Assert.Equal("John", (await withCache.QueryAsync(Make(1), query, disposeCommand: true, ct: ct)).Name);
        Assert.Equal("Victor", (await withCache.QueryAsync((IDbCommand)Make(2), query, disposeCommand: true, ct: ct)).Name);

        var coldAsync = new CachedTypeParser<UserPair>();
        Assert.Equal("Alice", (await coldAsync.QueryAsync(Make(3), query, disposeCommand: true, ct: ct)).Name);
        var coldAsyncIdb = new CachedTypeParser<UserPair>();
        Assert.Equal("John", (await coldAsyncIdb.QueryAsync((IDbCommand)Make(1), query, disposeCommand: true, ct: ct)).Name);
        var coldIdb = new CachedTypeParser<UserPair>();
        Assert.Equal("Victor", coldIdb.Query((IDbCommand)Make(2), query, disposeCommand: true).Name);
    }
    

    [Fact]
    public async Task Streams_run_on_open_connections_and_empty_results() {
        var ct = TestContext.Current.CancellationToken;
        var query = new QueryCommand("SELECT ID, Name FROM Users WHERE ID >= @Min ORDER BY ID");
        using var open = Db.Open();

        var cold = new List<long>();
        await foreach (var row in query.StreamQueryAsync<UserPair>(open, new { Min = 1 }, ct: ct))
            cold.Add(row.ID);
        Assert.Equal([1, 2, 3], cold);
        Assert.Equal(ConnectionState.Open, open.State);

        var empty = new List<long>();
        await foreach (var row in query.StreamQueryAsync<UserPair>(open, new { Min = 99 }, ct: ct))
            empty.Add(row.ID);
        Assert.Empty(empty);

        var warmEmpty = new List<long>();
        await foreach (var row in query.StreamQueryAsync<UserPair>(open, new { Min = 99 }, ct: ct))
            warmEmpty.Add(row.ID);
        Assert.Empty(warmEmpty);

        using var closed = Db.GetConnection();
        var scalarStream = new List<string>();
        await foreach (var name in closed.StreamQueryAsync<string>("SELECT Name FROM Users ORDER BY ID", ct: ct))
            scalarStream.Add(name);
        Assert.Equal(3, scalarStream.Count);
    }

    public struct FieldsOnly {
        public int A;
        public string? B;
    }

    [Fact]
    public void A_fields_only_shape_derives_its_schema_from_the_fields() {
        var cols = SchemaExtractor.FromType(typeof(FieldsOnly));
        Assert.Contains(cols, c => c.Name == "A");
        Assert.Contains(cols, c => c.Name == "B");
    }

    [Fact]
    public async Task Connection_shortcuts_cover_the_remaining_overload_cells() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = Db.GetConnection();
        IDbConnection icnn = cnn;
        object insertArgs = new { Val = 300 };

        Assert.Equal(1, icnn.Execute("INSERT INTO Scratch (Val) VALUES (@Val)", insertArgs));
        Assert.Equal(1, await icnn.ExecuteAsync("INSERT INTO Scratch (Val) VALUES (@Val)", insertArgs, ct: ct));
        Assert.Equal(1, cnn.Execute("INSERT INTO Scratch (Val) VALUES (@Val)", insertArgs));
        Assert.Equal(1, await cnn.ExecuteAsync("INSERT INTO Scratch (Val) VALUES (@Val)", insertArgs, ct: ct));

        using (var tx = (SqliteTransaction)((IDbConnection)Db.Open()).BeginTransaction()) {
            var openCnn = tx.Connection!;
            Assert.Equal(1, ((IDbConnection)openCnn).Execute("INSERT INTO Scratch (Val) VALUES (@Val)", insertArgs, tx, 5));
            tx.Rollback();
            openCnn.Dispose();
        }

        object byId = new { ID = 1 };
        using (var reader = icnn.ExecuteReader("SELECT Name FROM Users WHERE ID = @ID", out var cmd, byId)) {
            Assert.True(reader.Read());
            cmd.Dispose();
        }
        using (var reader = await icnn.ExecuteReaderAsync("SELECT Name FROM Users WHERE ID = @ID", out var cmd, byId, ct: ct)) {
            Assert.True(await reader.ReadAsync(ct));
            cmd.Dispose();
        }
        using (var reader = await cnn.ExecuteReaderAsync("SELECT Name FROM Users WHERE ID = @ID", out var cmd, byId, ct: ct)) {
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
