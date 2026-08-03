using System.Data;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// <c>ExecuteReader</c> hands back the raw data reader, and <c>ExecuteMultiReader</c> wraps it to
/// read several result sets in sequence, each with its own parser.
/// </summary>
public class ReaderTests(SqliteDb Db) : IClassFixture<SqliteDb> {
    private static readonly QueryCommand ThreeFour = new("SELECT 3 AS three, 4 AS four");
    private static readonly QueryCommand TwoSets = new("SELECT 1; SELECT 2");
    private static readonly QueryCommand TwoTypedSets = new("SELECT CAST(1 AS BIGINT) AS Col1; SELECT CAST(2 AS BIGINT) AS Col2");
    private static readonly QueryCommand FiveSets = new("SELECT 1; SELECT 2; SELECT 3; SELECT 4; SELECT 5");
    private static readonly QueryCommand UsersByFlag = new("SELECT ID FROM Users WHERE IsActive = @a ORDER BY ID; SELECT ID FROM Users WHERE IsActive = @b ORDER BY ID");

    [Fact]
    public void ExecuteReader_returns_the_result() {
        using var cnn = Db.GetConnection();
        var dt = new DataTable();
        using (var reader = ThreeFour.ExecuteReader(cnn, out var cmd)) {
            dt.Load(reader);
            cmd.Dispose();
        }
        Assert.Equal(2, dt.Columns.Count);
        Assert.Equal("three", dt.Columns[0].ColumnName);
        Assert.Equal("four", dt.Columns[1].ColumnName);
        var row = Assert.Single(dt.Rows.Cast<DataRow>());
        Assert.Equal(3L, row[0]);
        Assert.Equal(4L, row[1]);
    }

    [Fact]
    public async Task ExecuteReaderAsync_works_on_a_closed_connection() {
        using var cnn = Db.GetConnection();
        var dt = new DataTable();
        using (var reader = await ThreeFour.ExecuteReaderAsync(cnn, out var cmd, ct: TestContext.Current.CancellationToken)) {
            dt.Load(reader);
            cmd.Dispose();
        }
        Assert.Equal(["three", "four"], dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
        var row = Assert.Single(dt.Rows.Cast<DataRow>());
        Assert.Equal(3L, row[0]);
        Assert.Equal(4L, row[1]);
    }

    [Fact]
    public async Task ExecuteReaderAsync_works_on_an_open_connection() {
        using var cnn = Db.Open();
        var dt = new DataTable();
        using (var reader = await ThreeFour.ExecuteReaderAsync(cnn, out var cmd, ct: TestContext.Current.CancellationToken)) {
            dt.Load(reader);
            cmd.Dispose();
        }
        Assert.Equal(["three", "four"], dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
        var row = Assert.Single(dt.Rows.Cast<DataRow>());
        Assert.Equal(3L, row[0]);
        Assert.Equal(4L, row[1]);
        Assert.Equal(ConnectionState.Open, cnn.State);
    }

    [Fact]
    public void ExecuteReader_allows_columns_to_be_read_out_of_order() {
        using var cnn = Db.Open();
        using var reader = new QueryCommand("SELECT 0, 1, 2").ExecuteReader(cnn, out var cmd);
        Assert.True(reader.Read());
        Assert.Equal(2L, reader.GetInt64(2));
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.False(reader.Read());
        cmd.Dispose();
    }

    [Fact]
    public async Task ExecuteReaderAsync_allows_columns_to_be_read_out_of_order() {
        using var cnn = Db.Open();
        using var reader = await new QueryCommand("SELECT 0, 1, 2")
            .ExecuteReaderAsync(cnn, out var cmd, ct: TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2L, reader.GetInt64(2));
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
        cmd.Dispose();
    }

    [Fact]
    public async Task MultiReader_reads_each_set_with_QueryAsync() {
        using var cnn = Db.Open();
        using var multi = await TwoSets.ExecuteMultiReaderAsync(cnn, out var cmd, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await multi.QueryAsync<int>(TestContext.Current.CancellationToken));
        Assert.Equal(2, await multi.QueryAsync<int>(TestContext.Current.CancellationToken));
        cmd.Dispose();
    }

    [Fact]
    public void MultiReader_reads_each_set_with_Query() {
        using var cnn = Db.Open();
        using var multi = TwoSets.ExecuteMultiReader(cnn, out var cmd);
        Assert.Equal(1, multi.Query<int>());
        Assert.Equal(2, multi.Query<int>());
        cmd.Dispose();
    }

    [Fact]
    public async Task MultiReader_alternates_between_stream_and_single_reads() {
        using var cnn = Db.Open();
        using var multi = await FiveSets.ExecuteMultiReaderAsync(cnn, out var cmd, ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, await multi.QueryAsync<int>(TestContext.Current.CancellationToken));

        var second = new List<int>();
        await foreach (var v in multi.StreamQueryAsync<int>(ct: TestContext.Current.CancellationToken))
            second.Add(v);
        Assert.Equal([2], second);

        Assert.Equal(3, await multi.QueryAsync<int>(TestContext.Current.CancellationToken));

        var fourth = new List<int>();
        await foreach (var v in multi.StreamQueryAsync<int>(ct: TestContext.Current.CancellationToken))
            fourth.Add(v);
        Assert.Equal([4], fourth);

        Assert.Equal(5, await multi.QueryAsync<int>(TestContext.Current.CancellationToken));
        cmd.Dispose();
    }

    [Fact]
    public async Task MultiReader_converts_each_set_independently() {
        using var cnn = Db.Open();
        using var multi = await TwoTypedSets.ExecuteMultiReaderAsync(cnn, out var cmd, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await multi.QueryAsync<int>(TestContext.Current.CancellationToken));
        Assert.Equal(2, await multi.QueryAsync<int>(TestContext.Current.CancellationToken));
        cmd.Dispose();
    }

    [Fact]
    public async Task MultiReader_works_from_a_closed_connection() {
        using var cnn = Db.GetConnection();
        using var multi = await TwoSets.ExecuteMultiReaderAsync(cnn, out var cmd, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await multi.QueryAsync<int>(TestContext.Current.CancellationToken));
        Assert.Equal(2, await multi.QueryAsync<int>(TestContext.Current.CancellationToken));
        cmd.Dispose();
    }

    [Fact]
    public async Task MultiReader_binds_parameters_for_every_set() {
        using var cnn = Db.Open();
        using var multi = await UsersByFlag.ExecuteMultiReaderAsync(cnn, out var cmd, new { a = 1, b = 0 }, ct: TestContext.Current.CancellationToken);

        var actives = new List<long>();
        await foreach (var id in multi.StreamQueryAsync<long>(ct: TestContext.Current.CancellationToken))
            actives.Add(id);
        var inactives = new List<long>();
        await foreach (var id in multi.StreamQueryAsync<long>(ct: TestContext.Current.CancellationToken))
            inactives.Add(id);
        cmd.Dispose();

        Assert.Equal([1L, 3L], actives);
        Assert.Equal([2L], inactives);
    }

    [Fact]
    public void MultiReader_Get_reads_one_row_of_the_current_set() {
        using var cnn = Db.Open();
        using var multi = TwoSets.ExecuteMultiReader(cnn, out var cmd);
        Assert.True(multi.Read());
        var (canContinue, value) = multi.Get<int>();
        Assert.Equal(1, value);
        Assert.False(canContinue);
        cmd.Dispose();
    }

    [Fact]
    public async Task MultiReader_GetAsync_reads_one_row_of_the_current_set() {
        using var cnn = Db.Open();
        using var multi = await TwoSets.ExecuteMultiReaderAsync(cnn, out var cmd, ct: TestContext.Current.CancellationToken);
        Assert.True(await multi.ReadAsync(TestContext.Current.CancellationToken));
        var (canContinue, value) = await multi.GetAsync<int>(TestContext.Current.CancellationToken);
        Assert.Equal(1, value);
        Assert.False(canContinue);
        cmd.Dispose();
    }

    [Fact]
    public void MultiReader_is_itself_a_usable_data_reader() {
        var query = new QueryCommand("SELECT ID, Name, Email, Salary, IsActive FROM Users ORDER BY ID");
        using var cnn = Db.Open();
        using var multi = query.ExecuteMultiReader(cnn, out var cmd);

        Assert.Equal(5, multi.FieldCount);
        Assert.True(multi.HasRows);
        Assert.True(multi.Read());
        Assert.Equal(1L, multi.GetInt64(0));
        Assert.Equal("John", multi.GetString(1));
        Assert.True(multi.IsDBNull(2));
        Assert.Equal(10.5, multi.GetDouble(3));
        Assert.True(multi.GetBoolean(4));
        Assert.Equal("ID", multi.GetName(0));
        Assert.Equal(1, multi.GetOrdinal("Name"));
        Assert.Equal(typeof(long), multi.GetFieldType(0));
        Assert.Equal(1L, multi["ID"]);
        Assert.Equal("John", multi[1]);
        Assert.False(multi.IsClosed);
        Assert.Equal(0, multi.Depth);
        var values = new object[5];
        Assert.Equal(5, multi.GetValues(values));
        Assert.Equal("John", values[1]);
        Assert.False(multi.NextResult());
        cmd.Dispose();
    }

    [Fact]
    public async Task MultiReader_manual_NextResult_moves_between_sets() {
        using var cnn = Db.Open();
        using var multi = await TwoSets.ExecuteMultiReaderAsync(cnn, out var cmd, ct: TestContext.Current.CancellationToken);
        Assert.True(multi.Read());
        Assert.Equal(1L, multi.GetInt64(0));
        Assert.True(await multi.NextResultAsync(TestContext.Current.CancellationToken));
        Assert.True(multi.Read());
        Assert.Equal(2L, multi.GetInt64(0));
        cmd.Dispose();
    }

    [Fact]
    public void MultiReader_can_choose_a_parser_for_each_row_from_a_discriminator() {
        var query = new QueryCommand("SELECT 'abc' AS Name, 1 AS Kind, 3.0 AS Value UNION ALL SELECT 'def', 2, 4.0");
        using var cnn = Db.Open();
        using var multi = query.ExecuteMultiReader(cnn, out var cmd);

        var fooParser = (IStepParser<DiscriminatedFoo>)multi.GetCurrentSetParser<DiscriminatedFoo>();
        var barParser = (IStepParser<DiscriminatedBar>)multi.GetCurrentSetParser<DiscriminatedBar>();
        var foos = new List<DiscriminatedFoo>();
        var bars = new List<DiscriminatedBar>();

        while (multi.Read()) {
            if (multi.GetInt32(1) == 1)
                foos.Add(fooParser.ParseStep(multi));
            else
                bars.Add(barParser.ParseStep(multi));
        }

        Assert.Equal([new DiscriminatedFoo("abc", 1)], foos);
        Assert.Equal([new DiscriminatedBar("def", 4.0)], bars);
        cmd.Dispose();
    }
}

public sealed record DiscriminatedFoo(string Name, int Kind) : IDbReadable;
public sealed record DiscriminatedBar(string Name, double Value) : IDbReadable;
