using Microsoft.Data.SqlClient;
using RinkuLib.Commands;
using RinkuLib.Queries;
using Xunit;

namespace RinkuLib.Tests.TestContainers;

public class MultiResultSetFixture : DBFixture<SqlConnection> {
    // Two result sets, each filtered to a parameter. The shape the benchmark's category 18 runs.
    public QueryCommand TwoSets = new("SELECT Id FROM #mrs WHERE Id = @a; SELECT Id FROM #mrs WHERE Id = @b");
    // Single-statement control: proves parameter binding works outside the multi-reader path.
    public QueryCommand OneSet = new("SELECT Id FROM #mrs WHERE Id = @a");
}

/// <summary>
/// Correctness tests for the multi-reader path, prompted by the benchmark's category 18. They confirm
/// parameters bind and each SELECT filters to one row, so the "dropped params, full table scan" idea
/// is wrong. They seed ids 1..5 and ask each set to filter to a NON-first id (2 and 4), since reading
/// the first row alone would pass on the lowest id either way. Allocation is measured separately in
/// <see cref="MultiReaderIsolationTests"/>. The #mrs temp table is connection-scoped, so each test
/// seeds and queries on one open connection.
/// </summary>
public class MultiResultSetTests(MultiResultSetFixture Fixture) : IClassFixture<MultiResultSetFixture> {
    private readonly MultiResultSetFixture Fixture = Fixture;

    private static async Task<SqlConnection> OpenSeeded(MultiResultSetFixture fixture, CancellationToken ct) {
        var cnn = fixture.GetConnection();
        await cnn.OpenAsync(ct);
        await using var cmd = cnn.CreateCommand();
        cmd.CommandText = "CREATE TABLE #mrs (Id INT NOT NULL); INSERT INTO #mrs (Id) VALUES (1),(2),(3),(4),(5);";
        await cmd.ExecuteNonQueryAsync(ct);
        return cnn;
    }

    [Fact]
    public async Task TwoSets_FirstRowOfEachSet_HonorsParameters() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = await OpenSeeded(Fixture, ct);

        using var multi = await Fixture.TwoSets.ExecuteMultiReaderAsync(cnn, out var cmd, new { a = 2, b = 4 }, ct: ct);
        var first = await multi.QueryAsync<int>(ct);
        var second = await multi.QueryAsync<int>(ct);
        cmd.Dispose();

        Assert.Equal(2, first);
        Assert.Equal(4, second);
    }

    [Fact]
    public async Task TwoSets_EachSetReturnsExactlyOneRow() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = await OpenSeeded(Fixture, ct);

        using var multi = await Fixture.TwoSets.ExecuteMultiReaderAsync(cnn, out var cmd, new { a = 2, b = 4 }, ct: ct);
        var set1 = new List<int>();
        await foreach (var id in multi.StreamQueryAsync<int>(ct: ct))
            set1.Add(id);
        var set2 = new List<int>();
        await foreach (var id in multi.StreamQueryAsync<int>(ct: ct))
            set2.Add(id);
        cmd.Dispose();

        Assert.Equal(new[] { 2 }, set1);
        Assert.Equal(new[] { 4 }, set2);
    }

    [Fact]
    public async Task TwoSets_ConnectionExtension_HonorsParameters() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = await OpenSeeded(Fixture, ct);

        using var multi = await cnn.ExecuteMultiReaderAsync(
            "SELECT Id FROM #mrs WHERE Id = @a; SELECT Id FROM #mrs WHERE Id = @b",
            out var cmd, new { a = 2, b = 4 }, ct: ct);
        var first = await multi.QueryAsync<int>(ct);
        var second = await multi.QueryAsync<int>(ct);
        cmd.Dispose();

        Assert.Equal(2, first);
        Assert.Equal(4, second);
    }

    [Fact]
    public async Task SingleSet_HonorsParameter_Control() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = await OpenSeeded(Fixture, ct);

        var ids = new List<int>();
        await foreach (var id in Fixture.OneSet.StreamQueryAsync<int>(cnn, new { a = 2 }, ct: ct))
            ids.Add(id);

        Assert.Equal(new[] { 2 }, ids);
    }
}
