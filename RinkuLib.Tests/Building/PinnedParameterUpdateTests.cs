using System.Data;
using System.Data.Common;
using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Building;

/// <summary>
/// A pinned parameter's metadata drives not just the first bind but also the in-place updates and
/// removals a live-command builder performs on later runs.
/// </summary>
public class PinnedParameterUpdateTests {
    private static (QueryBuilderCommand<DbCommand> Builder, FakeCommand Cmd) StartPinned(DbParamInfo info) {
        var query = new QueryCommand("SELECT * FROM T WHERE X = @X");
        Assert.True(query.UpdateParamCache("@X", info));
        var cmd = new FakeCommand();
        return (query.StartBuilder((DbCommand)cmd), cmd);
    }

    public static TheoryData<string, DbParamInfo> PinnedInfos => new() {
        { "typed", TypedDbParamCache.Get(DbType.Int32) },
        { "sized", SizedDbParamCache.Get(DbType.String, 100) },
        { "scaled", new ScaledDbParamCache(DbType.Decimal, 18, 2) },
        { "directional", new DirectionalDbParamCache(ParameterDirection.InputOutput, DbType.Int32) },
        { "directional sized", new DirectionalSizedDbParamCache(ParameterDirection.Input, DbType.String, 500) },
        { "directional scaled", new DirectionalScaledDbParamCache(ParameterDirection.Input, DbType.Decimal, 10, 2) },
    };

    [Theory]
    [MemberData(nameof(PinnedInfos))]
    public void Update_replaces_the_value_in_place(string kind, DbParamInfo info) {
        _ = kind;
        var (builder, cmd) = StartPinned(info);
        builder.Use("@X", 1);
        builder.Use("@X", 2);
        var p = Assert.Single(cmd.BoundParameters);
        Assert.Equal(2, p.Value);
    }

    [Theory]
    [MemberData(nameof(PinnedInfos))]
    public void Update_to_null_removes_the_parameter(string kind, DbParamInfo info) {
        _ = kind;
        var (builder, cmd) = StartPinned(info);
        builder.Use("@X", 1);
        builder.Use("@X", null);
        Assert.Empty(cmd.BoundParameters);
    }

    [Theory]
    [MemberData(nameof(PinnedInfos))]
    public void Remove_takes_the_parameter_off(string kind, DbParamInfo info) {
        _ = kind;
        var (builder, cmd) = StartPinned(info);
        builder.Use("@X", 1);
        builder.Remove("@X");
        Assert.Empty(cmd.BoundParameters);
    }

    [Theory]
    [MemberData(nameof(PinnedInfos))]
    public void Reset_clears_the_pinned_parameter(string kind, DbParamInfo info) {
        _ = kind;
        var (builder, cmd) = StartPinned(info);
        builder.Use("@X", 1);
        builder.Reset();
        Assert.Empty(cmd.BoundParameters);
    }
    /*
    [Fact]
    public void Spread_with_a_pinned_element_type_applies_it_to_every_element() {
        var query = new QueryCommand("SELECT * FROM T WHERE X IN (?@Xs_X)");
        var probe = new FakeCommand();
        var render = query.StartBuilder((DbCommand)probe);
        render.Use("@Xs", new[] { "a", "b" });
        probe.BoundParameters[0].DbType = DbType.String;
        probe.BoundParameters[0].Size = 50;
        probe.BoundParameters[1].DbType = DbType.String;
        probe.BoundParameters[1].Size = 50;
        query.UpdateCache((IDbCommand)probe);

        var cmd = new FakeCommand();
        var builder = query.StartBuilder((DbCommand)cmd);
        builder.Use("@Xs", new[] { "c", "d" });
        Assert.All(cmd.BoundParameters, p => Assert.Equal(DbType.String, p.DbType));
        Assert.All(cmd.BoundParameters, p => Assert.Equal(100, p.Size));
    }*/
}
