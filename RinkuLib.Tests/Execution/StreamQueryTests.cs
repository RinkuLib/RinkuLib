using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// <c>StreamQueryAsync</c> yields rows one at a time without buffering the result.
/// </summary>
public class StreamQueryTests(SqliteDb Db) : IClassFixture<SqliteDb> {
    private static readonly QueryCommand AllNames = new("SELECT Name FROM Users ORDER BY ID");
    private static readonly QueryCommand NoRows = new("SELECT Name FROM Users WHERE 1 = 0");
    private static readonly QueryCommand WithNull = new("SELECT 'value' AS Value UNION ALL SELECT CAST(NULL AS TEXT) UNION ALL SELECT @txt");

    [Fact]
    public async Task Streams_rows_in_order() {
        using var cnn = Db.GetConnection();
        var names = new List<string>();
        await foreach (var name in AllNames.StreamQueryAsync<string>(cnn, ct: TestContext.Current.CancellationToken))
            names.Add(name);
        Assert.Equal(["John", "Victor", "Alice"], names);
    }

    [Fact]
    public async Task Streams_nothing_for_an_empty_result() {
        using var cnn = Db.GetConnection();
        await foreach (var _ in NoRows.StreamQueryAsync<string>(cnn, ct: TestContext.Current.CancellationToken))
            Assert.Fail("no row was expected");
    }

    [Fact]
    public async Task Streams_mapped_objects() {
        using var cnn = Db.GetConnection();
        var query = new QueryCommand("SELECT ID, Name, Email FROM Users ORDER BY ID");
        var ids = new List<long>();
        await foreach (var user in query.StreamQueryAsync<UserRow>(cnn, ct: TestContext.Current.CancellationToken))
            ids.Add(user.ID);
        Assert.Equal([1L, 2L, 3L], ids);
    }

    [Fact]
    public async Task Streaming_a_null_into_a_non_nullable_type_throws_at_that_row() {
        using var cnn = Db.GetConnection();
        var res = WithNull.StreamQueryAsync<string>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken);
        await using var enumerator = res.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("value", enumerator.Current);
        await Assert.ThrowsAsync<NullValueAssignmentException>(async () => await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task Streaming_null_into_MaybeNull_yields_null() {
        using var cnn = Db.GetConnection();
        var values = new List<string?>();
        await foreach (string? v in WithNull.StreamQueryAsync<MaybeNull<string>>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken))
            values.Add(v);
        Assert.Equal(["value", null, "def"], values);
    }

    [Fact]
    public async Task Stream_ends_cleanly_after_the_last_row() {
        using var cnn = Db.GetConnection();
        var res = AllNames.StreamQueryAsync<string>(cnn, ct: TestContext.Current.CancellationToken);
        await using var enumerator = res.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.True(await enumerator.MoveNextAsync());
        Assert.True(await enumerator.MoveNextAsync());
        Assert.False(await enumerator.MoveNextAsync());
    }

    [Fact]
    public void Lazy_enumerable_result_yields_rows_while_iterated() {
        using var cnn = Db.GetConnection();
        var names = AllNames.Query<IEnumerable<string>>(cnn);
        Assert.NotNull(names);
        Assert.Equal(["John", "Victor", "Alice"], names.ToList());
    }

    [Fact]
    public async Task Streaming_with_a_generic_parameter_object() {
        using var cnn = Db.GetConnection();
        var query = new QueryCommand("SELECT Name FROM Users WHERE IsActive = @Active ORDER BY ID");
        var names = new List<string>();
        await foreach (var name in query.StreamQueryAsync<string, ActiveFilter>(cnn, new ActiveFilter(true), ct: TestContext.Current.CancellationToken))
            names.Add(name);
        Assert.Equal(["John", "Alice"], names);
    }
}
