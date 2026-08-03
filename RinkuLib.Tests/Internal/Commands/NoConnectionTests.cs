using System.Data;
using System.Data.Common;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// Every entry point that runs a command refuses one that carries no connection, on both the
/// <see cref="DbCommand"/> and the plain <see cref="IDbCommand"/> roads.
/// </summary>
public class NoConnectionTests {
    static readonly QueryCommand Query = new("SELECT ID FROM Users");
    static readonly ColumnInfo[] Cols = [new("ID", typeof(long), false)];

    static ITypeParser<long> Parser() {
        var cols = Cols;
        return TypeParser.GetTypeParser<long>(ref cols);
    }

    static CachedTypeParser<long> Cached() => new();

    static bool[] Usage() => new bool[Query.Mapper.Count];

    /// <summary>
    /// Asserting the code keeps these tests from passing on an unrelated failure, which matters because the
    /// fakes here throw of their own accord once execution is actually reached.
    /// </summary>
    static void AssertRefused(Action run)
        => Refusals.Raises(ErrorCodes.NoConnection, run);

    static Task AssertRefusedAsync(Func<Task> run)
        => Refusals.RaisesAsync(ErrorCodes.NoConnection, run);

    [Fact]
    public void The_DbCommand_road_refuses_a_command_without_a_connection() {
        var cmd = new FakeCommand();
        AssertRefused(() => cmd.Execute(false));
        AssertRefused(() => cmd.ExecuteScalar<long>(false));
        AssertRefused(() => cmd.ExecuteReader(default, null));
        AssertRefused(() => cmd.ExecuteMultiReader(Query, Usage(), false));
        AssertRefused(() => cmd.Query(Cached(), false));
        AssertRefused(() => Parser().Query(cmd));
        AssertRefused(() => Parser().Query(cmd, Query));
    }

    [Fact]
    public async Task The_DbCommand_async_road_refuses_a_command_without_a_connection() {
        var ct = TestContext.Current.CancellationToken;
        var cmd = new FakeCommand();
        await AssertRefusedAsync(() => cmd.ExecuteAsync(false, null, ct));
        await AssertRefusedAsync(() => cmd.ExecuteScalarAsync<long>(false, null, ct));
        await AssertRefusedAsync(() => cmd.ExecuteReaderAsync(default, null, ct));
        await AssertRefusedAsync(() => cmd.ExecuteMultiReaderAsync(Query, Usage(), false, default, ct));
        await AssertRefusedAsync(() => cmd.QueryAsync(Cached(), false, ct));
        await AssertRefusedAsync(() => Parser().QueryAsync(cmd, ct: ct));
        await AssertRefusedAsync(() => Parser().QueryAsync(cmd, Query, ct: ct));
    }

    [Fact]
    public async Task The_DbCommand_streams_refuse_a_command_without_a_connection() {
        var ct = TestContext.Current.CancellationToken;
        var cmd = new FakeCommand();
        await AssertStreamThrows(cmd.StreamQueryAsync(Parser(), null, false, ct));
        await AssertStreamThrows(cmd.StreamQueryAsync(Cached(), false, ct));
        await AssertStreamThrows(Cached().StreamQueryAsync(cmd, false, ct));
    }

    [Fact]
    public void The_plain_command_road_refuses_a_command_without_a_connection() {
        IDbCommand cmd = new LegacyCommand();
        AssertRefused(() => cmd.Execute(false));
        AssertRefused(() => cmd.ExecuteScalar<long>(false));
        AssertRefused(() => cmd.ExecuteReader(default, null));
        AssertRefused(() => cmd.ExecuteMultiReader(Query, Usage(), false));
        AssertRefused(() => cmd.Query(Cached(), false));
        AssertRefused(() => Parser().Query(cmd));
        AssertRefused(() => Parser().Query(cmd, Query));
    }

    [Fact]
    public async Task The_plain_command_async_road_refuses_a_command_without_a_connection() {
        var ct = TestContext.Current.CancellationToken;
        IDbCommand cmd = new LegacyCommand();
        await AssertRefusedAsync(() => cmd.ExecuteAsync(false, null, ct));
        await AssertRefusedAsync(() => cmd.ExecuteScalarAsync<long>(false, null, ct));
        await AssertRefusedAsync(() => cmd.ExecuteReaderAsync(default, null, ct));
        await AssertRefusedAsync(() => cmd.ExecuteMultiReaderAsync(Query, Usage(), false, default, ct));
        await AssertRefusedAsync(() => cmd.QueryAsync(Cached(), false, ct));
        await AssertRefusedAsync(() => Parser().QueryAsync(cmd, ct: ct));
        await AssertRefusedAsync(() => Parser().QueryAsync(cmd, Query, ct: ct));
    }

    [Fact]
    public void The_lazy_shape_refuses_a_command_without_a_connection_on_both_roads() {
        var cols = Cols;
        var lazy = TypeParser.GetTypeParser<IEnumerable<long>>(ref cols);
        var dbCmd = new FakeCommand();
        AssertRefused(() => lazy.Query(dbCmd).ToList());
        AssertRefused(() => lazy.Query(dbCmd, Query).ToList());
        IDbCommand plain = new LegacyCommand();
        AssertRefused(() => lazy.Query(plain).ToList());
        AssertRefused(() => lazy.Query(plain, Query).ToList());
    }

    static Task AssertStreamThrows<T>(IAsyncEnumerable<T> stream)
        => AssertRefusedAsync(async () => {
            await foreach (var _ in stream) { }
        });

    /// <summary>
    /// The extension <c>ExecuteReader</c> shares its name with <see cref="DbCommand.ExecuteReader()"/>, and the
    /// instance method wins overload resolution at the arities they share. A caller writing
    /// <c>cmd.ExecuteReader()</c> gets ADO.NET's, with none of the connection handling or caching this library
    /// adds. Only the two-argument form reaches the extension.
    /// </summary>
    [Fact]
    public void The_no_argument_ExecuteReader_resolves_to_the_ADO_method_not_the_extension() {
        var cmd = new FakeCommand();
        var builtIn = Record.Exception(() => cmd.ExecuteReader());
        Assert.NotNull(builtIn);
        Assert.IsNotAssignableFrom<RinkuException>(builtIn);

        var extension = Refusals.Raises(ErrorCodes.NoConnection, () => cmd.ExecuteReader(default, null));
        Refusals.HasHelpLink(extension);
    }

    [Fact]
    public void A_reader_that_is_already_a_DbDataReader_is_handed_back_unwrapped() {
        using var real = Rows.Reader(Cols, [1L]);
        Assert.Same(real, WrappedBasicReader.Wrap(real));

        using var plain = new PlainReader(Rows.Reader(Cols, [1L]));
        var wrapped = WrappedBasicReader.Wrap(plain);
        Assert.IsType<WrappedBasicReader>(wrapped);
        Assert.True(wrapped.Read());
        Assert.Equal(1L, wrapped.GetInt64(0));
        wrapped.Dispose();
    }

    [Fact]
    public void A_plain_command_handing_back_a_real_reader_is_not_double_wrapped() {
        using var db = new SqliteDb();
        using var cnn = db.GetConnection();
        using var inner = cnn.CreateCommand();
        inner.CommandText = "SELECT ID FROM Users ORDER BY ID";
        IDbCommand passthrough = new PassthroughCommand(inner);
        Assert.Equal(1L, Parser().Query(passthrough));
    }
}
