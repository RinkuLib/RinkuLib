using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Npgsql;
using RinkuLib.Commands;
using RinkuLib.Queries;
using Xunit;

namespace RinkuLib.Tests.TestContainers;

/// <summary>
/// A seeded table both dialects accept, so one set of conditional templates can be run against either
/// engine unchanged.
/// </summary>
public abstract class ConditionalSqlFixture<TCnn> : DBFixture<TCnn> where TCnn : IDbConnection {
    private static readonly QueryCommand Drop = new("DROP TABLE IF EXISTS Tracks");
    private static readonly QueryCommand Create = new(
        "CREATE TABLE Tracks (Id INT NOT NULL, Name VARCHAR(50) NOT NULL, GenreId INT NOT NULL, UnitPrice DECIMAL(9,2) NOT NULL)");
    private static readonly QueryCommand Insert = new(
        "INSERT INTO Tracks (Id, Name, GenreId, UnitPrice) VALUES (@Id, @Name, @GenreId, @UnitPrice)");

    /// <summary>The rows every test in the suite reads.</summary>
    public static readonly (int Id, string Name, int GenreId, decimal UnitPrice)[] Seed = [
        (1, "Kashmir", 1, 0.99m),
        (2, "Black Dog", 1, 1.29m),
        (3, "Nutshell", 2, 0.99m),
        (4, "Would", 2, 1.99m),
        (5, "Cochise", 3, 2.49m),
    ];

    public override async ValueTask InitializeAsync() {
        await base.InitializeAsync();
        using var cnn = (DbConnection)(object)GetConnection()!;
        await cnn.OpenAsync();
        await Drop.ExecuteAsync(cnn);
        await Create.ExecuteAsync(cnn);
        foreach (var row in Seed)
            await Insert.ExecuteAsync(cnn, new { row.Id, row.Name, row.GenreId, row.UnitPrice });
    }
}

public class SqlServerConditionalFixture : ConditionalSqlFixture<SqlConnection> { }
public class PostgresConditionalFixture : ConditionalSqlFixture<NpgsqlConnection> { }

/// <summary>
/// The conditional-SQL rules run against a real engine, which is the only thing that can say whether a
/// rendered template actually parses. A string comparison accepts <c>IN ()</c> or a half-closed
/// parenthesis; a database does not. Every case here renders a template one way when a value is supplied
/// and another way when it is not, and both renderings have to execute.
/// </summary>
public abstract class ConditionalSqlSuite {
    /// <summary>A fresh connection to whichever engine this run is aimed at.</summary>
    protected abstract DbConnection Connect();

    /// <summary>
    /// One command per suite rather than one shared across the engines. A command reuses the row parser it
    /// learned from the first result it saw, and nothing accounts for a provider typing the same query
    /// differently, <c>COUNT(*)</c> as an <c>int</c> on one and a <c>bigint</c> on the other.
    /// </summary>
    private readonly QueryCommand OptionalSpread = new(
        "SELECT Id, Name FROM Tracks WHERE GenreId IN (?@GenreIds_X) ORDER BY Id");
    private readonly QueryCommand RequiredSpread = new(
        "SELECT Id, Name FROM Tracks WHERE GenreId IN (@GenreIds_X) ORDER BY Id");
    private readonly QueryCommand WeldedSpread = new(
        "SELECT COUNT(*) FROM Tracks WHERE UnitPrice > 0 &AND GenreId IN (?@GenreIds_X)");
    private readonly QueryCommand TwoOptionals = new(
        "SELECT Id FROM Tracks WHERE GenreId = ?@GenreId AND UnitPrice >= ?@MinPrice ORDER BY Id");
    private readonly QueryCommand SpreadBesideAnother = new(
        "SELECT Id FROM Tracks WHERE Id IN (@Keep, /*@GenreIds*/@GenreIds_X) ORDER BY Id");

    /// <summary>A supplied collection filters, and the numbered parameters bind to the right rows.</summary>
    [Fact]
    public async Task A_supplied_spread_filters_on_its_elements() {
        using var cnn = Connect();
        var ids = await OptionalSpread.QueryAsync<List<int>>(cnn, new { GenreIds = new[] { 1, 3 } });
        Assert.Equal([1, 2, 5], ids);
    }

    /// <summary>
    /// An empty collection prunes the clause instead of rendering <c>IN ()</c>. Before the prune reached
    /// every road this rendered a parenthesis with nothing in it, which no engine here will parse, so the
    /// row count is the assertion and the fact that it runs at all is the point.
    /// </summary>
    [Fact]
    public async Task An_empty_spread_prunes_into_sql_the_engine_accepts() {
        using var cnn = Connect();
        var all = await OptionalSpread.QueryAsync<List<int>>(cnn, new { GenreIds = Array.Empty<int>() });
        Assert.Equal([1, 2, 3, 4, 5], all);

        var absent = await OptionalSpread.QueryAsync<List<int>>(cnn);
        Assert.Equal([1, 2, 3, 4, 5], absent);
    }

    /// <summary>A required spread has nothing to render, and the refusal comes before the engine is asked.</summary>
    [Fact]
    public async Task An_empty_required_spread_never_reaches_the_engine() {
        using var cnn = Connect();
        await Assert.ThrowsAsync<RequiredHandlerValueException>(
            () => RequiredSpread.QueryAsync<List<int>>(cnn, new { GenreIds = Array.Empty<int>() }));

        var supplied = await RequiredSpread.QueryAsync<List<int>>(cnn, new { GenreIds = new[] { 2 } });
        Assert.Equal([3, 4], supplied);
    }

    /// <summary>
    /// The <c>&amp;AND</c> welds the static condition to the optional one, so an empty collection takes the
    /// whole <c>WHERE</c> with it and the statement is still a valid <c>SELECT</c>.
    /// </summary>
    [Fact]
    public async Task A_welded_condition_leaves_with_its_partner() {
        using var cnn = Connect();
        Assert.Equal(2, await WeldedSpread.QueryAsync<int>(cnn, new { GenreIds = new[] { 1 } }));
        Assert.Equal(5, await WeldedSpread.QueryAsync<int>(cnn, new { GenreIds = Array.Empty<int>() }));
    }

    /// <summary>
    /// A marker holds the footprint to the spread rather than growing out of the parenthesis, so an empty
    /// collection drops only itself and the comma binding it, leaving a list the engine can still read.
    /// Written <c>?@GenreIds_X</c> instead, the footprint would grow past the parenthesis and take the whole
    /// <c>IN</c> condition with it.
    /// </summary>
    [Fact]
    public async Task A_pruned_spread_leaves_the_rest_of_its_list_intact() {
        using var cnn = Connect();
        var both = await SpreadBesideAnother.QueryAsync<List<int>>(cnn, new { Keep = 4, GenreIds = new[] { 1, 2 } });
        Assert.Equal([1, 2, 4], both);

        var keepOnly = await SpreadBesideAnother.QueryAsync<List<int>>(cnn, new { Keep = 4, GenreIds = Array.Empty<int>() });
        Assert.Equal([4], keepOnly);
    }

    /// <summary>Every combination of two optional filters renders a statement the engine accepts.</summary>
    [Theory]
    [InlineData(null, null, 5)]
    [InlineData(1, null, 2)]
    [InlineData(null, 1.29, 3)]
    [InlineData(2, 1.50, 1)]
    public async Task Each_combination_of_optional_filters_runs(int? genreId, double? minPrice, int expected) {
        using var cnn = Connect();
        var b = TwoOptionals.StartBuilder();
        if (genreId is not null)
            b.Use("@GenreId", genreId.Value);
        if (minPrice is not null)
            b.Use("@MinPrice", (decimal)minPrice.Value);
        var ids = await b.QueryAsync<List<int>>(cnn);
        Assert.Equal(expected, ids.Count);
    }

    /// <summary>
    /// A bound command rebinds the spread between runs, growing and shrinking the parameter list in place,
    /// and every intermediate state has to remain runnable.
    /// </summary>
    [Fact]
    public async Task A_bound_command_rebinds_a_spread_across_runs() {
        using var cnn = Connect();
        await cnn.OpenAsync();
        using var cmd = cnn.CreateCommand();
        var b = WeldedSpread.StartBuilder(cmd);

        b.Use("@GenreIds", new[] { 1, 2 });
        Assert.Equal(4, await b.ExecuteScalarAsync<int>());

        b.Use("@GenreIds", new[] { 3 });
        Assert.Equal(1, await b.ExecuteScalarAsync<int>());

        b.Use("@GenreIds", new[] { 1, 2, 3 });
        Assert.Equal(5, await b.ExecuteScalarAsync<int>());

        b.Use("@GenreIds", Array.Empty<int>());
        Assert.Equal(5, await b.ExecuteScalarAsync<int>());
    }
}

public class ConditionalSqlOnSqlServer(SqlServerConditionalFixture Fixture)
    : ConditionalSqlSuite, IClassFixture<SqlServerConditionalFixture> {
    protected override DbConnection Connect() => Fixture.GetConnection();
}

public class ConditionalSqlOnPostgres(PostgresConditionalFixture Fixture)
    : ConditionalSqlSuite, IClassFixture<PostgresConditionalFixture> {
    protected override DbConnection Connect() => Fixture.GetConnection();
}
