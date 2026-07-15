using System.Data;
using System.Data.Common;
using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// One pass over every overload of the command and connection execution surface: each verb crossed
/// with the parameter styles (none, object, generic, ref generic, out command) and both connection
/// interfaces. Each call gets a sanity assertion; the behavioral depth lives in the other files.
/// </summary>
public class OverloadSurfaceTests(SqliteDb Db) : IClassFixture<SqliteDb> {
    private static readonly QueryCommand Count = new("SELECT COUNT(*) FROM Users WHERE IsActive = @Active");
    private static readonly QueryCommand Insert = new("INSERT INTO Scratch (Val) VALUES (@Val)");
    private static readonly QueryCommand TwoSets = new("SELECT 1; SELECT 2");
    private const string CountSql = "SELECT COUNT(*) FROM Users WHERE IsActive = @Active";
    private const string InsertSql = "INSERT INTO Scratch (Val) VALUES (@Val)";
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    // ---- QueryCommand over DbConnection ----

    [Fact]
    public void Command_execute_overloads_on_DbConnection() {
        using var cnn = Db.GetConnection();
        Assert.Equal(1, Insert.Execute(cnn, new { Val = 1 }));
        Assert.Equal(1, Insert.Execute(cnn, out var cmd, new { Val = 2 }));
        cmd.Dispose();
        Assert.Equal(1, Insert.Execute(cnn, new ValHolder(3)));
        var holder = new ValHolder(4);
        Assert.Equal(1, Insert.Execute(cnn, ref holder));
    }

    [Fact]
    public async Task Command_execute_async_overloads_on_DbConnection() {
        using var cnn = Db.GetConnection();
        Assert.Equal(1, await Insert.ExecuteAsync(cnn, new { Val = 1 }, ct: Ct));
        Assert.Equal(1, await Insert.ExecuteAsync(cnn, out var cmd, new { Val = 2 }, ct: Ct));
        cmd.Dispose();
        Assert.Equal(1, await Insert.ExecuteAsync(cnn, new ValHolder(3), ct: Ct));
        var holder = new ValHolder(4);
        Assert.Equal(1, await Insert.ExecuteAsync(cnn, ref holder, ct: Ct));
    }

    [Fact]
    public void Command_scalar_overloads_on_DbConnection() {
        using var cnn = Db.GetConnection();
        Assert.Equal(2, Count.ExecuteScalar<int>(cnn, new { Active = 1 }));
        Assert.Equal(2, Count.ExecuteScalar<int>(cnn, out var cmd, new { Active = 1 }));
        cmd.Dispose();
        Assert.Equal(2, Count.ExecuteScalar<int, ActiveFilter>(cnn, new ActiveFilter(true)));
        var filter = new ActiveFilter(true);
        Assert.Equal(2, Count.ExecuteScalar<int, ActiveFilter>(cnn, ref filter));
    }

    [Fact]
    public async Task Command_scalar_async_overloads_on_DbConnection() {
        using var cnn = Db.GetConnection();
        Assert.Equal(1, await Count.ExecuteScalarAsync<int>(cnn, new { Active = 0 }, ct: Ct));
        Assert.Equal(1, await Count.ExecuteScalarAsync<int>(cnn, out var cmd, new { Active = 0 }, ct: Ct));
        cmd.Dispose();
        Assert.Equal(1, await Count.ExecuteScalarAsync<int, ActiveFilter>(cnn, new ActiveFilter(false), ct: Ct));
        var filter = new ActiveFilter(false);
        Assert.Equal(1, await Count.ExecuteScalarAsync<int, ActiveFilter>(cnn, ref filter, ct: Ct));
    }

    [Fact]
    public void Command_reader_overloads_on_DbConnection() {
        using var cnn = Db.GetConnection();
        using (var reader = Count.ExecuteReader(cnn, out var cmd, new { Active = 1 })) {
            Assert.True(reader.Read());
            cmd.Dispose();
        }
        using (var reader = Count.ExecuteReader(cnn, out var cmd, new ActiveFilter(true))) {
            Assert.True(reader.Read());
            cmd.Dispose();
        }
        var filter = new ActiveFilter(true);
        using (var reader = Count.ExecuteReader(cnn, out var cmd, ref filter)) {
            Assert.True(reader.Read());
            cmd.Dispose();
        }
    }

    [Fact]
    public async Task Command_reader_async_overloads_on_DbConnection() {
        using var cnn = Db.GetConnection();
        using (var reader = await Count.ExecuteReaderAsync(cnn, out var cmd, new { Active = 1 }, ct: Ct)) {
            Assert.True(await reader.ReadAsync(Ct));
            cmd.Dispose();
        }
        using (var reader = await Count.ExecuteReaderAsync(cnn, out var cmd, new ActiveFilter(true), ct: Ct)) {
            Assert.True(await reader.ReadAsync(Ct));
            cmd.Dispose();
        }
        var filter = new ActiveFilter(true);
        using (var reader = await Count.ExecuteReaderAsync(cnn, out var cmd, ref filter, ct: Ct)) {
            Assert.True(await reader.ReadAsync(Ct));
            cmd.Dispose();
        }
    }

    [Fact]
    public void Command_multi_reader_overloads_on_DbConnection() {
        using var cnn = Db.Open();
        using (var multi = TwoSets.ExecuteMultiReader(cnn, out var cmd)) {
            Assert.Equal(1, multi.Query<int>());
            cmd.Dispose();
        }
        using (var multi = TwoSets.ExecuteMultiReader(cnn, out var cmd, new ValHolder(0))) {
            Assert.Equal(1, multi.Query<int>());
            cmd.Dispose();
        }
        var holder = new ValHolder(0);
        using (var multi = TwoSets.ExecuteMultiReader(cnn, out var cmd, ref holder)) {
            Assert.Equal(1, multi.Query<int>());
            cmd.Dispose();
        }
    }

    [Fact]
    public async Task Command_multi_reader_async_overloads_on_DbConnection() {
        using var cnn = Db.Open();
        using (var multi = await TwoSets.ExecuteMultiReaderAsync(cnn, out var cmd, ct: Ct)) {
            Assert.Equal(1, await multi.QueryAsync<int>(Ct));
            cmd.Dispose();
        }
        using (var multi = await TwoSets.ExecuteMultiReaderAsync(cnn, out var cmd, new ValHolder(0), ct: Ct)) {
            Assert.Equal(1, await multi.QueryAsync<int>(Ct));
            cmd.Dispose();
        }
        var holder = new ValHolder(0);
        using (var multi = await TwoSets.ExecuteMultiReaderAsync(cnn, out var cmd, ref holder, ct: Ct)) {
            Assert.Equal(1, await multi.QueryAsync<int>(Ct));
            cmd.Dispose();
        }
    }

    [Fact]
    public void Command_query_overloads_on_DbConnection() {
        using var cnn = Db.GetConnection();
        Assert.Equal(2, Count.Query<int>(cnn, new { Active = 1 }));
        Assert.Equal(2, Count.Query<int>(cnn, out var cmd, new { Active = 1 }));
        cmd.Dispose();
        Assert.Equal(2, Count.Query<int, ActiveFilter>(cnn, new ActiveFilter(true)));
        var filter = new ActiveFilter(true);
        Assert.Equal(2, Count.Query<int, ActiveFilter>(cnn, ref filter));
    }

    [Fact]
    public async Task Command_query_async_overloads_on_DbConnection() {
        using var cnn = Db.GetConnection();
        Assert.Equal(1, await Count.QueryAsync<int>(cnn, new { Active = 0 }, ct: Ct));
        Assert.Equal(1, await Count.QueryAsync<int>(cnn, out var cmd, new { Active = 0 }, ct: Ct));
        cmd.Dispose();
        Assert.Equal(1, await Count.QueryAsync<int, ActiveFilter>(cnn, new ActiveFilter(false), ct: Ct));
        var filter = new ActiveFilter(false);
        Assert.Equal(1, await Count.QueryAsync<int, ActiveFilter>(cnn, ref filter, ct: Ct));
    }

    [Fact]
    public async Task Command_stream_overloads_on_DbConnection() {
        using var cnn = Db.GetConnection();
        await foreach (var v in Count.StreamQueryAsync<int>(cnn, new { Active = 1 }, ct: Ct))
            Assert.Equal(2, v);
        var stream = Count.StreamQueryAsync<int>(cnn, out var cmd, new { Active = 1 }, ct: Ct);
        await foreach (var v in stream)
            Assert.Equal(2, v);
        cmd.Dispose();
        await foreach (var v in Count.StreamQueryAsync<int, ActiveFilter>(cnn, new ActiveFilter(true), ct: Ct))
            Assert.Equal(2, v);
        var filter = new ActiveFilter(true);
        await foreach (var v in Count.StreamQueryAsync<int, ActiveFilter>(cnn, ref filter, ct: Ct))
            Assert.Equal(2, v);
    }

    // ---- QueryCommand over IDbConnection ----

    [Fact]
    public void Command_execute_overloads_on_IDbConnection() {
        using var raw = Db.GetConnection();
        IDbConnection cnn = raw;
        Assert.Equal(1, Insert.Execute(cnn, new { Val = 1 }));
        Assert.Equal(1, Insert.Execute(cnn, out var cmd, new { Val = 2 }));
        cmd.Dispose();
        Assert.Equal(1, Insert.Execute(cnn, new ValHolder(3)));
        var holder = new ValHolder(4);
        Assert.Equal(1, Insert.Execute(cnn, ref holder));
    }

    [Fact]
    public async Task Command_execute_async_overloads_on_IDbConnection() {
        using var raw = Db.GetConnection();
        IDbConnection cnn = raw;
        Assert.Equal(1, await Insert.ExecuteAsync(cnn, new { Val = 1 }, ct: Ct));
        Assert.Equal(1, await Insert.ExecuteAsync(cnn, out var cmd, new { Val = 2 }, ct: Ct));
        cmd.Dispose();
        Assert.Equal(1, await Insert.ExecuteAsync(cnn, new ValHolder(3), ct: Ct));
        var holder = new ValHolder(4);
        Assert.Equal(1, await Insert.ExecuteAsync(cnn, ref holder, ct: Ct));
    }

    [Fact]
    public void Command_scalar_overloads_on_IDbConnection() {
        using var raw = Db.GetConnection();
        IDbConnection cnn = raw;
        Assert.Equal(2, Count.ExecuteScalar<int>(cnn, new { Active = 1 }));
        Assert.Equal(2, Count.ExecuteScalar<int>(cnn, out var cmd, new { Active = 1 }));
        cmd.Dispose();
        Assert.Equal(2, Count.ExecuteScalar<int, ActiveFilter>(cnn, new ActiveFilter(true)));
        var filter = new ActiveFilter(true);
        Assert.Equal(2, Count.ExecuteScalar<int, ActiveFilter>(cnn, ref filter));
    }

    [Fact]
    public async Task Command_scalar_async_overloads_on_IDbConnection() {
        using var raw = Db.GetConnection();
        IDbConnection cnn = raw;
        Assert.Equal(1, await Count.ExecuteScalarAsync<int>(cnn, new { Active = 0 }, ct: Ct));
        Assert.Equal(1, await Count.ExecuteScalarAsync<int>(cnn, out var cmd, new { Active = 0 }, ct: Ct));
        cmd.Dispose();
        Assert.Equal(1, await Count.ExecuteScalarAsync<int, ActiveFilter>(cnn, new ActiveFilter(false), ct: Ct));
        var filter = new ActiveFilter(false);
        Assert.Equal(1, await Count.ExecuteScalarAsync<int, ActiveFilter>(cnn, ref filter, ct: Ct));
    }

    [Fact]
    public void Command_reader_overloads_on_IDbConnection() {
        using var raw = Db.GetConnection();
        IDbConnection cnn = raw;
        using (var reader = Count.ExecuteReader(cnn, out var cmd, new { Active = 1 })) {
            Assert.True(reader.Read());
            cmd.Dispose();
        }
        using (var reader = Count.ExecuteReader(cnn, out var cmd, new ActiveFilter(true))) {
            Assert.True(reader.Read());
            cmd.Dispose();
        }
        var filter = new ActiveFilter(true);
        using (var reader = Count.ExecuteReader(cnn, out var cmd, ref filter)) {
            Assert.True(reader.Read());
            cmd.Dispose();
        }
    }

    [Fact]
    public async Task Command_reader_async_overloads_on_IDbConnection() {
        using var raw = Db.GetConnection();
        IDbConnection cnn = raw;
        using (var reader = await Count.ExecuteReaderAsync(cnn, out var cmd, new { Active = 1 }, ct: Ct)) {
            Assert.True(await reader.ReadAsync(Ct));
            cmd.Dispose();
        }
        using (var reader = await Count.ExecuteReaderAsync(cnn, out var cmd, new ActiveFilter(true), ct: Ct)) {
            Assert.True(await reader.ReadAsync(Ct));
            cmd.Dispose();
        }
        var filter = new ActiveFilter(true);
        using (var reader = await Count.ExecuteReaderAsync(cnn, out var cmd, ref filter, ct: Ct)) {
            Assert.True(await reader.ReadAsync(Ct));
            cmd.Dispose();
        }
    }

    [Fact]
    public async Task Command_multi_reader_overloads_on_IDbConnection() {
        using var raw = Db.Open();
        IDbConnection cnn = raw;
        using (var multi = TwoSets.ExecuteMultiReader(cnn, out var cmd)) {
            Assert.Equal(1, multi.Query<int>());
            cmd.Dispose();
        }
        using (var multi = TwoSets.ExecuteMultiReader(cnn, out var cmd, new ValHolder(0))) {
            Assert.Equal(1, multi.Query<int>());
            cmd.Dispose();
        }
        var holder = new ValHolder(0);
        using (var multi = TwoSets.ExecuteMultiReader(cnn, out var cmd, ref holder)) {
            Assert.Equal(1, multi.Query<int>());
            cmd.Dispose();
        }
        using (var multi = await TwoSets.ExecuteMultiReaderAsync(cnn, out var acmd, ct: Ct)) {
            Assert.Equal(1, await multi.QueryAsync<int>(Ct));
            acmd.Dispose();
        }
        using (var multi = await TwoSets.ExecuteMultiReaderAsync(cnn, out var acmd, new ValHolder(0), ct: Ct)) {
            Assert.Equal(1, await multi.QueryAsync<int>(Ct));
            acmd.Dispose();
        }
        using (var multi = await TwoSets.ExecuteMultiReaderAsync(cnn, out var acmd, ref holder, ct: Ct)) {
            Assert.Equal(1, await multi.QueryAsync<int>(Ct));
            acmd.Dispose();
        }
    }

    [Fact]
    public async Task Command_query_overloads_on_IDbConnection() {
        using var raw = Db.GetConnection();
        IDbConnection cnn = raw;
        Assert.Equal(2, Count.Query<int>(cnn, new { Active = 1 }));
        Assert.Equal(2, Count.Query<int>(cnn, out var cmd, new { Active = 1 }));
        cmd.Dispose();
        Assert.Equal(2, Count.Query<int, ActiveFilter>(cnn, new ActiveFilter(true)));
        var filter = new ActiveFilter(true);
        Assert.Equal(2, Count.Query<int, ActiveFilter>(cnn, ref filter));
        Assert.Equal(2, await Count.QueryAsync<int>(cnn, new { Active = 1 }, ct: Ct));
        Assert.Equal(2, await Count.QueryAsync<int>(cnn, out var acmd, new { Active = 1 }, ct: Ct));
        acmd.Dispose();
        Assert.Equal(2, await Count.QueryAsync<int, ActiveFilter>(cnn, new ActiveFilter(true), ct: Ct));
        Assert.Equal(2, await Count.QueryAsync<int, ActiveFilter>(cnn, ref filter, ct: Ct));
    }

    // ---- string SQL over DbConnection ----

    [Fact]
    public async Task Sql_string_overloads_on_DbConnection() {
        using var cnn = Db.GetConnection();
        Assert.Equal(1, cnn.Execute(InsertSql, new { Val = 1 }));
        Assert.Equal(1, cnn.Execute(InsertSql, out var c1, new { Val = 2 }));
        c1.Dispose();
        Assert.Equal(1, await cnn.ExecuteAsync(InsertSql, out var c2, new { Val = 3 }, ct: Ct));
        c2.Dispose();
        Assert.Equal(2, cnn.ExecuteScalar<int>(CountSql, new { Active = 1 }));
        Assert.Equal(2, await cnn.ExecuteScalarAsync<int>(CountSql, out var c3, new { Active = 1 }, ct: Ct));
        c3.Dispose();
        Assert.Equal(2, cnn.Query<int>(CountSql, out var c4, new { Active = 1 }));
        c4.Dispose();
        Assert.Equal(2, await cnn.QueryAsync<int>(CountSql, out var c5, new { Active = 1 }, ct: Ct));
        c5.Dispose();
        var stream = cnn.StreamQueryAsync<int>(CountSql, out var c6, new { Active = 1 }, ct: Ct);
        await foreach (var v in stream)
            Assert.Equal(2, v);
        c6.Dispose();
        using (var reader = await cnn.ExecuteReaderAsync(CountSql, out var c7, new { Active = 1 }, ct: Ct)) {
            Assert.True(reader.Read());
            c7.Dispose();
        }
        using (var multi = cnn.ExecuteMultiReader("SELECT 1; SELECT 2", out var c8)) {
            Assert.Equal(1, multi.Query<int>());
            c8.Dispose();
        }
    }

    [Fact]
    public async Task Sql_string_generic_overloads_on_DbConnection() {
        using var cnn = Db.GetConnection();
        Assert.Equal(1, cnn.Execute(InsertSql, new ValHolder(4)));
        var holder = new ValHolder(5);
        Assert.Equal(1, cnn.Execute(InsertSql, ref holder));
        Assert.Equal(1, await cnn.ExecuteAsync(InsertSql, new ValHolder(6), ct: Ct));
        Assert.Equal(1, await cnn.ExecuteAsync(InsertSql, ref holder, ct: Ct));
        Assert.Equal(2, cnn.ExecuteScalar<int, ActiveFilter>(CountSql, new ActiveFilter(true)));
        var filter = new ActiveFilter(true);
        Assert.Equal(2, cnn.ExecuteScalar<int, ActiveFilter>(CountSql, ref filter));
        Assert.Equal(2, await cnn.ExecuteScalarAsync<int, ActiveFilter>(CountSql, new ActiveFilter(true), ct: Ct));
        Assert.Equal(2, await cnn.ExecuteScalarAsync<int, ActiveFilter>(CountSql, ref filter, ct: Ct));
        Assert.Equal(2, cnn.Query<int, ActiveFilter>(CountSql, new ActiveFilter(true)));
        Assert.Equal(2, cnn.Query<int, ActiveFilter>(CountSql, ref filter));
        Assert.Equal(2, await cnn.QueryAsync<int, ActiveFilter>(CountSql, new ActiveFilter(true), ct: Ct));
        Assert.Equal(2, await cnn.QueryAsync<int, ActiveFilter>(CountSql, ref filter, ct: Ct));
        await foreach (var v in cnn.StreamQueryAsync<int, ActiveFilter>(CountSql, new ActiveFilter(true), ct: Ct))
            Assert.Equal(2, v);
        await foreach (var v in cnn.StreamQueryAsync<int, ActiveFilter>(CountSql, ref filter, ct: Ct))
            Assert.Equal(2, v);
        using (var reader = cnn.ExecuteReader(CountSql, out var c1, new ActiveFilter(true))) {
            Assert.True(reader.Read());
            c1.Dispose();
        }
        using (var reader = await cnn.ExecuteReaderAsync(CountSql, out var c2, new ActiveFilter(true), ct: Ct)) {
            Assert.True(reader.Read());
            c2.Dispose();
        }
        using (var reader = cnn.ExecuteReader(CountSql, out var c3, ref filter)) {
            Assert.True(reader.Read());
            c3.Dispose();
        }
        using (var reader = await cnn.ExecuteReaderAsync(CountSql, out var c4, ref filter, ct: Ct)) {
            Assert.True(reader.Read());
            c4.Dispose();
        }
        using (var multi = cnn.ExecuteMultiReader("SELECT 1; SELECT 2", out var c5, new ValHolder(0))) {
            Assert.Equal(1, multi.Query<int>());
            c5.Dispose();
        }
        using (var multi = await cnn.ExecuteMultiReaderAsync("SELECT 1; SELECT 2", out var c6, new ValHolder(0), ct: Ct)) {
            Assert.Equal(1, await multi.QueryAsync<int>(Ct));
            c6.Dispose();
        }
        using (var multi = cnn.ExecuteMultiReader("SELECT 1; SELECT 2", out var c7, ref holder)) {
            Assert.Equal(1, multi.Query<int>());
            c7.Dispose();
        }
        using (var multi = await cnn.ExecuteMultiReaderAsync("SELECT 1; SELECT 2", out var c8, ref holder, ct: Ct)) {
            Assert.Equal(1, await multi.QueryAsync<int>(Ct));
            c8.Dispose();
        }
    }

    // ---- string SQL over IDbConnection ----

    [Fact]
    public async Task Sql_string_overloads_on_IDbConnection() {
        using var raw = Db.GetConnection();
        IDbConnection cnn = raw;
        Assert.Equal(1, cnn.Execute(InsertSql, out var c1, new { Val = 1 }));
        c1.Dispose();
        Assert.Equal(1, await cnn.ExecuteAsync(InsertSql, out var c2, new { Val = 2 }, ct: Ct));
        c2.Dispose();
        Assert.Equal(2, cnn.ExecuteScalar<int>(CountSql, out var c3, new { Active = 1 }));
        c3.Dispose();
        Assert.Equal(2, await cnn.ExecuteScalarAsync<int>(CountSql, out var c4, new { Active = 1 }, ct: Ct));
        c4.Dispose();
        Assert.Equal(2, cnn.Query<int>(CountSql, out var c5, new { Active = 1 }));
        c5.Dispose();
        Assert.Equal(2, await cnn.QueryAsync<int>(CountSql, out var c6, new { Active = 1 }, ct: Ct));
        c6.Dispose();
        using (var reader = cnn.ExecuteReader(CountSql, out var c7, new { Active = 1 })) {
            Assert.True(reader.Read());
            c7.Dispose();
        }
        using (var reader = await cnn.ExecuteReaderAsync(CountSql, out var c8, new { Active = 1 }, ct: Ct)) {
            Assert.True(reader.Read());
            c8.Dispose();
        }
        using (var multi = cnn.ExecuteMultiReader("SELECT 1; SELECT 2", out var c9)) {
            Assert.Equal(1, multi.Query<int>());
            c9.Dispose();
        }
        using (var multi = await cnn.ExecuteMultiReaderAsync("SELECT 1; SELECT 2", out var c10, ct: Ct)) {
            Assert.Equal(1, await multi.QueryAsync<int>(Ct));
            c10.Dispose();
        }
    }

    [Fact]
    public async Task Sql_string_generic_overloads_on_IDbConnection() {
        using var raw = Db.GetConnection();
        IDbConnection cnn = raw;
        Assert.Equal(1, cnn.Execute(InsertSql, new ValHolder(7)));
        var holder = new ValHolder(8);
        Assert.Equal(1, cnn.Execute(InsertSql, ref holder));
        Assert.Equal(1, await cnn.ExecuteAsync(InsertSql, new ValHolder(9), ct: Ct));
        Assert.Equal(1, await cnn.ExecuteAsync(InsertSql, ref holder, ct: Ct));
        Assert.Equal(2, cnn.ExecuteScalar<int, ActiveFilter>(CountSql, new ActiveFilter(true)));
        var filter = new ActiveFilter(true);
        Assert.Equal(2, cnn.ExecuteScalar<int, ActiveFilter>(CountSql, ref filter));
        Assert.Equal(2, await cnn.ExecuteScalarAsync<int, ActiveFilter>(CountSql, new ActiveFilter(true), ct: Ct));
        Assert.Equal(2, await cnn.ExecuteScalarAsync<int, ActiveFilter>(CountSql, ref filter, ct: Ct));
        Assert.Equal(2, cnn.Query<int, ActiveFilter>(CountSql, new ActiveFilter(true)));
        Assert.Equal(2, cnn.Query<int, ActiveFilter>(CountSql, ref filter));
        Assert.Equal(2, await cnn.QueryAsync<int, ActiveFilter>(CountSql, new ActiveFilter(true), ct: Ct));
        Assert.Equal(2, await cnn.QueryAsync<int, ActiveFilter>(CountSql, ref filter, ct: Ct));
        using (var reader = cnn.ExecuteReader(CountSql, out var c1, new ActiveFilter(true))) {
            Assert.True(reader.Read());
            c1.Dispose();
        }
        using (var reader = await cnn.ExecuteReaderAsync(CountSql, out var c2, ref filter, ct: Ct)) {
            Assert.True(reader.Read());
            c2.Dispose();
        }
        using (var multi = cnn.ExecuteMultiReader("SELECT 1; SELECT 2", out var c3, new ValHolder(0))) {
            Assert.Equal(1, multi.Query<int>());
            c3.Dispose();
        }
        using (var multi = await cnn.ExecuteMultiReaderAsync("SELECT 1; SELECT 2", out var c4, ref holder, ct: Ct)) {
            Assert.Equal(1, await multi.QueryAsync<int>(Ct));
            c4.Dispose();
        }
    }

    [Fact]
    public void GetCommand_helpers_build_bound_commands() {
        using var cnn = Db.GetConnection();
        var variables = new object?[Count.Mapper.Count];
        variables[Count.Mapper.GetIndex("@Active")] = 1;
        using (var cmd = QueryBuilderExtensions.GetCommand(Count, variables, cnn))
            Assert.Equal("SELECT COUNT(*) FROM Users WHERE IsActive = @Active", cmd.CommandText);
        using (var cmd = QueryBuilderExtensions.GetCommand(Count, variables, (IDbConnection)cnn))
            Assert.Equal("SELECT COUNT(*) FROM Users WHERE IsActive = @Active", cmd.CommandText);
    }
}
