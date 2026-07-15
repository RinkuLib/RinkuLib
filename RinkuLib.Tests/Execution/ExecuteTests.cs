using System.Data;
using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// <c>Execute</c> returns the affected row count and <c>ExecuteScalar&lt;T&gt;</c> the first cell,
/// converted to the requested type.
/// </summary>
public class ExecuteTests(SqliteDb Db) : IClassFixture<SqliteDb> {
    private static readonly QueryCommand InsertVal = new("INSERT INTO Scratch (Val) VALUES (@Val)");
    private static readonly QueryCommand InsertAndCount = new("INSERT INTO Scratch (Val) VALUES (@Val); SELECT COUNT(*) FROM Scratch");
    private static readonly QueryCommand SelectMissing = new("SELECT Val FROM Scratch WHERE ID = -1");

    [Fact]
    public void Execute_returns_the_affected_row_count() {
        using var cnn = Db.GetConnection();
        Assert.Equal(1, InsertVal.Execute(cnn, new { Val = 10 }));
    }

    [Fact]
    public async Task ExecuteAsync_returns_the_affected_row_count() {
        using var cnn = Db.GetConnection();
        Assert.Equal(1, await InsertVal.ExecuteAsync(cnn, new { Val = 11 }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Execute_on_an_open_connection_leaves_it_open() {
        using var cnn = Db.Open();
        InsertVal.Execute(cnn, new { Val = 12 });
        Assert.Equal(ConnectionState.Open, cnn.State);
    }

    [Fact]
    public void Execute_on_a_closed_connection_closes_it_after() {
        using var cnn = Db.GetConnection();
        InsertVal.Execute(cnn, new { Val = 13 });
        Assert.Equal(ConnectionState.Closed, cnn.State);
    }

    [Fact]
    public void Execute_through_the_IDbConnection_path() {
        using var cnn = Db.GetConnection();
        Assert.Equal(1, InsertVal.Execute((IDbConnection)cnn, new { Val = 14 }));
    }

    [Fact]
    public async Task ExecuteAsync_through_the_IDbConnection_path() {
        using var cnn = Db.GetConnection();
        Assert.Equal(1, await InsertVal.ExecuteAsync((IDbConnection)cnn, new { Val = 15 }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Execute_within_a_rolled_back_transaction_leaves_no_row() {
        var before = Db.CountScratchRows();
        using (var cnn = Db.Open())
        using (var tx = cnn.BeginTransaction()) {
            InsertVal.Execute(cnn, new { Val = 16 }, transaction: tx);
            tx.Rollback();
        }
        Assert.Equal(before, Db.CountScratchRows());
    }

    [Fact]
    public void Execute_within_a_committed_transaction_keeps_the_row() {
        var before = Db.CountScratchRows();
        using (var cnn = Db.Open())
        using (var tx = cnn.BeginTransaction()) {
            InsertVal.Execute(cnn, new { Val = 17 }, transaction: tx);
            tx.Commit();
        }
        Assert.Equal(before + 1, Db.CountScratchRows());
    }

    [Fact]
    public void Timeout_is_applied_to_the_command() {
        using var cnn = Db.GetConnection();
        InsertVal.Execute(cnn, out var cmd, new { Val = 18 }, timeout: 123);
        Assert.Equal(123, cmd.CommandTimeout);
        cmd.Dispose();
    }

    [Fact]
    public void ExecuteScalar_converts_to_int() {
        using var cnn = Db.GetConnection();
        var count = InsertAndCount.ExecuteScalar<int>(cnn, new { Val = 20 });
        Assert.True(count >= 1);
    }

    [Fact]
    public async Task ExecuteScalarAsync_converts_to_long_and_uint() {
        using var cnn = Db.GetConnection();
        var asLong = await InsertAndCount.ExecuteScalarAsync<long>(cnn, new { Val = 21 }, ct: TestContext.Current.CancellationToken);
        Assert.True(asLong >= 1);
        var asUint = await InsertAndCount.ExecuteScalarAsync<uint>(cnn, new { Val = 22 }, ct: TestContext.Current.CancellationToken);
        Assert.True(asUint >= 1);
    }

    [Fact]
    public void ExecuteScalar_with_no_rows_returns_null_for_nullables() {
        using var cnn = Db.GetConnection();
        Assert.Null(SelectMissing.ExecuteScalar<int?>(cnn));
        Assert.Null(SelectMissing.ExecuteScalar<long?>(cnn));
    }

    [Fact]
    public async Task ExecuteScalarAsync_with_no_rows_returns_null_for_nullables() {
        using var cnn = Db.GetConnection();
        Assert.Null(await SelectMissing.ExecuteScalarAsync<int?>(cnn, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ExecuteScalar_through_the_IDbConnection_path() {
        using var cnn = Db.GetConnection();
        var count = InsertAndCount.ExecuteScalar<int>((IDbConnection)cnn, new { Val = 23 });
        Assert.True(count >= 1);
    }

    [Fact]
    public void Execute_with_generic_parameter_object() {
        using var cnn = Db.GetConnection();
        Assert.Equal(1, InsertVal.Execute(cnn, new ValHolder(30)));
    }

    [Fact]
    public void Execute_with_ref_parameter_object() {
        using var cnn = Db.GetConnection();
        var holder = new ValHolder(31);
        Assert.Equal(1, InsertVal.Execute(cnn, ref holder));
    }

    [Fact]
    public async Task ExecuteAsync_with_generic_parameter_object() {
        using var cnn = Db.GetConnection();
        Assert.Equal(1, await InsertVal.ExecuteAsync(cnn, new ValHolder(32), ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Command_bound_builder_executes_repeatedly() {
        using var cnn = Db.GetConnection();
        var before = Db.CountScratchRows();
        var builder = InsertVal.StartBuilder(cnn.CreateCommand());
        builder.Use("@Val", 40);
        builder.Execute();
        builder.Use("@Val", 41);
        builder.Execute();
        Assert.Equal(before + 2, Db.CountScratchRows());
    }

    [Fact]
    public async Task Command_bound_builder_executes_repeatedly_async() {
        using var cnn = Db.GetConnection();
        var before = Db.CountScratchRows();
        var builder = InsertVal.StartBuilder(cnn.CreateCommand());
        builder.UseWith(new { Val = 42 });
        await builder.ExecuteAsync(ct: TestContext.Current.CancellationToken);
        builder.UseWith(new { Val = 43 });
        await builder.ExecuteAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(before + 2, Db.CountScratchRows());
    }

    [Fact]
    public void In_memory_builder_executes_scalar() {
        using var cnn = Db.GetConnection();
        var builder = InsertAndCount.StartBuilder();
        builder.Use("@Val", 44);
        Assert.True(builder.ExecuteScalar<int>(cnn) >= 1);
    }
}

public record struct ValHolder(int Val);
