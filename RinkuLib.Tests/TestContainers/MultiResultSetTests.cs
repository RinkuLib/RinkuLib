using Microsoft.Data.SqlClient;
using RinkuLib.Commands;
using RinkuLib.Queries;
using Xunit;

namespace RinkuLib.Tests.TestContainers;

public class MultiResultSetFixture : DBFixture<SqlConnection> {
    public QueryCommand TwoSets = new("SELECT Id FROM #mrs WHERE Id = @a; SELECT Id FROM #mrs WHERE Id = @b");
    public QueryCommand OneSet = new("SELECT Id FROM #mrs WHERE Id = @a");
}

/// <summary>
/// Checks the multi-reader binds parameters and filters each result set to its own rows. Seeds ids 1
/// to 5 and asks each set for a non-first id (2 and 4), so a query that dropped the parameter shows up.
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
