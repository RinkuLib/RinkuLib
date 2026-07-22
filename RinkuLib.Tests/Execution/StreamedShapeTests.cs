using System.Data;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// The rules a streamed <see cref="IEnumerable{T}"/> result follows. Nothing runs until the rows are
/// walked, the reader opens on the first step of enumerating and closes when the walk ends, and walking
/// twice runs the command twice.
/// <para>
/// A command that has not learned its parser yet reaches this through <c>DeferredRows</c> and a warm one
/// through the parser's own <c>Query</c>, so every rule here is checked cold and warm. Each test builds its
/// own database, and so its own connection pool, since a connection one test left open would come back out
/// of the pool and fail another.
/// </para>
/// </summary>
public class StreamedShapeTests {
    const string Sql = "SELECT ID, Name, Email FROM Users ORDER BY ID";

    static QueryCommand Cold() => new(Sql);

    static QueryCommand Warm(SqliteDb db) {
        var query = new QueryCommand(Sql);
        using var cnn = db.GetConnection();
        _ = query.Query<IEnumerable<UserRow>>(cnn).Count();
        return query;
    }

    public static TheoryData<bool> Roads => new() { false, true };

    static QueryCommand Road(SqliteDb db, bool warm) => warm ? Warm(db) : Cold();

    /// <summary>
    /// Asking for the result runs nothing. The command goes off when its rows are asked for, so a result
    /// nobody walks leaves the database alone and holds no reader to let go of.
    /// </summary>
    [Theory]
    [MemberData(nameof(Roads))]
    public void Asking_for_the_rows_runs_nothing(bool warm) {
        using var db = new SqliteDb();
        var query = new QueryCommand("INSERT INTO Scratch (Val) VALUES (7); SELECT 1");
        using var cnn = db.Open();
        if (warm)
            _ = query.Query<IEnumerable<long>>(cnn).Count();

        var before = db.CountScratchRows();
        var rows = query.Query<IEnumerable<long>>(cnn);
        Assert.Equal(before, db.CountScratchRows());

        _ = rows.Count();
        Assert.Equal(before + 1, db.CountScratchRows());
    }

    /// <summary>
    /// A <see cref="Task{TResult}"/> of <see cref="IEnumerable{T}"/> is a sequence delivered through a
    /// task, not an async stream. The task is the part that can be awaited, the opening and the running,
    /// and the rows it hands back are walked synchronously. <c>StreamQueryAsync</c> is where rows come
    /// asynchronously.
    /// </summary>
    [Theory]
    [MemberData(nameof(Roads))]
    public async Task The_async_road_runs_the_command_while_it_can_await(bool warm) {
        var ct = TestContext.Current.CancellationToken;
        using var db = new SqliteDb();
        var query = new QueryCommand("INSERT INTO Scratch (Val) VALUES (7); SELECT 1");
        using var cnn = db.Open();
        if (warm)
            _ = (await query.QueryAsync<IEnumerable<long>>(cnn, ct: ct)).ToList();

        var before = db.CountScratchRows();
        var rows = await query.QueryAsync<IEnumerable<long>>(cnn, ct: ct);
        Assert.Equal(before + 1, db.CountScratchRows());

        Assert.Equal([1L], rows.ToList());
    }

    /// <summary>
    /// The token given to the async road covers the part it awaits, so a cancelled one stops it there
    /// rather than quietly going to the database.
    /// </summary>
    [Theory]
    [MemberData(nameof(Roads))]
    public async Task The_async_road_carries_the_token_into_what_it_awaits(bool warm) {
        using var db = new SqliteDb();
        var query = Road(db, warm);
        using var cnn = db.GetConnection();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await query.QueryAsync<IEnumerable<UserRow>>(cnn, ct: cancelled.Token));
        Assert.Equal(ConnectionState.Closed, cnn.State);
    }

    [Theory]
    [MemberData(nameof(Roads))]
    public void A_result_nobody_walks_leaves_the_connection_closed(bool warm) {
        using var db = new SqliteDb();
        var query = Road(db, warm);
        using var cnn = db.GetConnection();

        _ = query.Query<IEnumerable<UserRow>>(cnn);
        Assert.Equal(ConnectionState.Closed, cnn.State);
    }

    [Theory]
    [MemberData(nameof(Roads))]
    public void Reading_every_row_closes_the_connection(bool warm) {
        using var db = new SqliteDb();
        var query = Road(db, warm);
        using var cnn = db.GetConnection();

        Assert.Equal(3, query.Query<IEnumerable<UserRow>>(cnn).Count());
        Assert.Equal(ConnectionState.Closed, cnn.State);
    }

    [Theory]
    [MemberData(nameof(Roads))]
    public void Stopping_part_way_closes_the_connection(bool warm) {
        using var db = new SqliteDb();
        var query = Road(db, warm);
        using var cnn = db.GetConnection();

        foreach (var row in query.Query<IEnumerable<UserRow>>(cnn)) {
            Assert.Equal(1, row.ID);
            break;
        }
        Assert.Equal(ConnectionState.Closed, cnn.State);
    }

    /// <summary>Each walk is its own run, so the rows come back as many times as they are asked for.</summary>
    [Theory]
    [MemberData(nameof(Roads))]
    public void The_rows_can_be_walked_again(bool warm) {
        using var db = new SqliteDb();
        var query = Road(db, warm);
        using var cnn = db.GetConnection();

        var rows = query.Query<IEnumerable<UserRow>>(cnn);
        Assert.Equal(3, rows.Count());
        Assert.Equal(3, rows.Count());
        Assert.Equal(["John", "Victor", "Alice"], rows.Select(r => r.Name));
        Assert.Equal(ConnectionState.Closed, cnn.State);
    }

    /// <summary>
    /// The command runs when the rows are walked, so what it refuses surfaces there rather than where the
    /// result was asked for.
    /// </summary>
    [Theory]
    [MemberData(nameof(Roads))]
    public void A_refused_statement_throws_where_the_rows_are_walked(bool warm) {
        using var db = new SqliteDb();
        var query = new QueryCommand("SELECT * FROM NoSuchTable");
        if (warm) {
            using var warmUp = db.GetConnection();
            Assert.ThrowsAny<Exception>(() => query.Query<IEnumerable<UserRow>>(warmUp).Count());
        }
        using var cnn = db.GetConnection();

        var rows = query.Query<IEnumerable<UserRow>>(cnn);
        Assert.ThrowsAny<Exception>(() => rows.Count());
        Assert.Equal(ConnectionState.Closed, cnn.State);
    }

    public record Paid(long ID, double Salary);

    /// <summary>
    /// A row the parser refuses ends the walk there, and what the run opened goes back the same way it
    /// would have at the end of the rows.
    /// </summary>
    [Theory]
    [MemberData(nameof(Roads))]
    public void A_row_that_fails_mid_stream_still_releases_the_reader(bool warm) {
        using var db = new SqliteDb();
        var query = new QueryCommand("SELECT ID, Salary FROM Users ORDER BY ID");
        if (warm) {
            using var warmUp = db.GetConnection();
            Assert.ThrowsAny<Exception>(() => query.Query<IEnumerable<Paid>>(warmUp).Count());
        }
        using var cnn = db.GetConnection();

        var seen = 0;
        Assert.ThrowsAny<Exception>(() => {
            foreach (var row in query.Query<IEnumerable<Paid>>(cnn))
                seen++;
        });
        Assert.Equal(2, seen);
        Assert.Equal(ConnectionState.Closed, cnn.State);
    }

    [Theory]
    [MemberData(nameof(Roads))]
    public void A_result_with_no_rows_is_empty(bool warm) {
        using var db = new SqliteDb();
        _ = Road(db, warm);
        using var cnn = db.GetConnection();

        var none = new QueryCommand("SELECT ID, Name, Email FROM Users WHERE ID = 999");
        Assert.Empty(none.Query<IEnumerable<UserRow>>(cnn));
        Assert.Equal(ConnectionState.Closed, cnn.State);
    }

    /// <summary>An open connection is left open, only the one the run opened is closed by it.</summary>
    [Theory]
    [MemberData(nameof(Roads))]
    public void A_connection_the_caller_opened_stays_open(bool warm) {
        using var db = new SqliteDb();
        var query = Road(db, warm);
        using var cnn = db.Open();

        Assert.Equal(3, query.Query<IEnumerable<UserRow>>(cnn).Count());
        Assert.Equal(ConnectionState.Open, cnn.State);
        Assert.Equal(3, query.Query<List<UserRow>>(cnn).Count);
    }

    /// <summary>A buffered shape does not wait, so asking for it runs the command there and then.</summary>
    [Fact]
    public void A_buffered_shape_runs_when_it_is_asked_for() {
        using var db = new SqliteDb();
        var query = new QueryCommand("INSERT INTO Scratch (Val) VALUES (7); SELECT 1");
        using var cnn = db.Open();

        var before = db.CountScratchRows();
        _ = query.Query<List<long>>(cnn);
        Assert.Equal(before + 1, db.CountScratchRows());
    }
}
