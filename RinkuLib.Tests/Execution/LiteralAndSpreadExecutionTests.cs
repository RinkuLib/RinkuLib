using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// End-to-end coverage of the literal handlers and the collection spread against a real database,
/// mirroring the ground Dapper's literal-replacement and IN tests cover, in Rinku's syntax
/// (<c>@x_N</c> for an inlined number, <c>@ids_X</c> for a spread IN list).
/// </summary>
public class LiteralAndSpreadExecutionTests(SqliteDb Db) : IClassFixture<SqliteDb> {
    [Fact]
    public void Number_literal_is_inlined_into_the_sql() {
        var query = new QueryCommand("SELECT @val_N AS V");
        using var cnn = Db.GetConnection();
        Assert.Equal(42, query.Query<int>(cnn, new { val = 42 }));
    }

    [Fact]
    public void String_literal_is_inlined_and_quoted() {
        var query = new QueryCommand("SELECT @name_S AS V");
        using var cnn = Db.GetConnection();
        Assert.Equal("Rinku", query.Query<string>(cnn, new { name = "Rinku" }));
    }

    [Fact]
    public void Number_literal_used_in_a_predicate() {
        var query = new QueryCommand("SELECT COUNT(*) FROM Users WHERE ID = @id_N");
        using var cnn = Db.GetConnection();
        Assert.Equal(1, query.Query<int>(cnn, new { id = 2 }));
    }

    [Fact]
    public void Spread_expands_a_collection_into_an_IN_clause() {
        var query = new QueryCommand("SELECT COUNT(*) FROM Users WHERE ID IN (?@ids_X)");
        using var cnn = Db.GetConnection();
        var count = query.Query<int>(cnn, new { ids = new[] { 1, 3, 4 } });
        Assert.Equal(2, count);
    }

    [Fact]
    public void Spread_with_one_element_still_matches() {
        var query = new QueryCommand("SELECT Name FROM Users WHERE ID IN (?@ids_X)");
        using var cnn = Db.GetConnection();
        var names = query.Query<List<string>>(cnn, new { ids = new[] { 2 } });
        Assert.NotNull(names);
        Assert.Equal(["Victor"], names);
    }

    /// <summary>
    /// An empty collection counts as absent, so an optional spread prunes its footprint rather than
    /// rendering an empty list. The <c>&amp;AND</c> welds the static condition to it, so the two leave
    /// together. Running it proves the point a string compare cannot: <c>IN ()</c> would not parse.
    /// </summary>
    [Fact]
    public void Empty_spread_prunes_instead_of_rendering_an_empty_list() {
        var query = new QueryCommand("SELECT COUNT(*) FROM Users WHERE IsActive = 1 &AND ID IN (?@ids_X)");
        var cmd = Render.From(query, new { ids = Array.Empty<int>() });
        Assert.Equal("SELECT COUNT(*) FROM Users", cmd.CommandText);
        Assert.Empty(cmd.BoundParameters);

        using var cnn = Db.GetConnection();
        Assert.Equal(3, query.Query<int>(cnn, new { ids = Array.Empty<int>() }));
    }

    /// <summary>
    /// A spread the query requires has nothing to write when its collection is empty, and the refusal
    /// comes while the SQL is built rather than as an <c>IN ()</c> the database would reject.
    /// </summary>
    [Fact]
    public void Empty_required_spread_is_refused_before_the_database() {
        var query = new QueryCommand("SELECT COUNT(*) FROM Users WHERE ID IN (@ids_X)");
        using var cnn = Db.GetConnection();
        Refusals.Raises(ErrorCodes.RequiredHandlerValue,
            () => query.Query<int>(cnn, new { ids = Array.Empty<int>() }));
    }

    [Fact]
    public void Literal_and_spread_combine_in_one_query() {
        var query = new QueryCommand("SELECT COUNT(*) FROM Users WHERE ID IN (?@ids_X) AND IsActive = @active_N");
        using var cnn = Db.GetConnection();
        var count = query.Query<int>(cnn, new { ids = new[] { 1, 2, 3 }, active = 1 });
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Spread_streams_results_over_the_async_path() {
        var query = new QueryCommand("SELECT Name FROM Users WHERE ID IN (?@ids_X) ORDER BY ID");
        using var cnn = Db.GetConnection();
        var names = new List<string>();
        await foreach (var name in query.StreamQueryAsync<string>(cnn, new { ids = new[] { 3, 1 } }, ct: TestContext.Current.CancellationToken))
            names.Add(name);
        Assert.Equal(["John", "Alice"], names);
    }

    [Fact]
    public void Spread_reused_on_a_bound_command_rebinds_the_list() {
        var query = new QueryCommand("SELECT COUNT(*) FROM Users WHERE ID IN (?@ids_X)");
        using var cnn = Db.Open();
        var builder = query.StartBuilder(cnn.CreateCommand());

        builder.Use("@ids", new[] { 1, 2, 3 });
        Assert.Equal(3, builder.ExecuteScalar<int>());

        builder.Use("@ids", new[] { 2 });
        Assert.Equal(1, builder.ExecuteScalar<int>());
    }
}
