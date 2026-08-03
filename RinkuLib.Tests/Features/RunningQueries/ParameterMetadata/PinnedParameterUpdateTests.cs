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

    /// <summary>
    /// Pinning is there to save the command the round of learning, so a pinned parameter has to count as
    /// settled. A command whose parameters are all pinned has nothing left to learn from a run.
    /// </summary>
    [Theory]
    [MemberData(nameof(PinnedInfos))]
    public void A_pinned_parameter_has_nothing_left_to_learn(string kind, DbParamInfo info) {
        _ = kind;
        var query = new QueryCommand("SELECT * FROM T WHERE X = @X");
        Assert.True(query.UpdateParamCache("@X", info));

        Span<bool> usage = stackalloc bool[query.Mapper.Count];
        usage[0] = true;
        Assert.False(query.NeedToCache(usage));
    }

    /// <summary>Pinning each of several parameters settles them all, it does not pile them up.</summary>
    [Fact]
    public void Pinning_every_parameter_settles_the_command() {
        var query = new QueryCommand("SELECT * FROM T WHERE X = @X AND Y = @Y");
        Assert.True(query.UpdateParamCache("@X", TypedDbParamCache.Get(DbType.Int32)));
        Assert.True(query.UpdateParamCache("@Y", TypedDbParamCache.Get(DbType.Int32)));

        Assert.Equal(0, query.Parameters.NbNonCached);
        Span<bool> usage = stackalloc bool[query.Mapper.Count];
        usage.Fill(true);
        Assert.False(query.NeedToCache(usage));
    }

    /// <summary>
    /// The list of parameters still to learn only ever shrinks as they settle, and grows again when one is
    /// put back to being inferred. It holds each parameter once and in order, whichever way it moved, since
    /// a stale or repeated entry is what makes a settled command keep asking to learn.
    /// </summary>
    [Fact]
    public void The_list_of_what_is_left_to_learn_follows_each_parameter_both_ways() {
        var query = new QueryCommand("SELECT * FROM T WHERE X = @X AND Y = @Y AND Z = @Z");
        var ps = query.Parameters;
        Assert.Equal([0, 1, 2], ps._nonCachedIndexes);

        Assert.True(ps.UpdateCache(1, TypedDbParamCache.Get(DbType.Int32)));
        Assert.Equal([0, 2], ps._nonCachedIndexes);

        Assert.True(ps.UpdateCache(0, TypedDbParamCache.Get(DbType.Int32)));
        Assert.Equal([2], ps._nonCachedIndexes);

        Assert.True(ps.UpdateCache(2, TypedDbParamCache.Get(DbType.Int32)));
        Assert.Empty(ps._nonCachedIndexes);

        Assert.True(ps.UpdateCache(1, InferedDbParamCache.Instance));
        Assert.Equal([1], ps._nonCachedIndexes);

        Assert.True(ps.UpdateCache(0, InferedDbParamCache.Instance));
        Assert.Equal([0, 1], ps._nonCachedIndexes);
    }

    /// <summary>Setting a parameter to what it already is leaves the list alone, and its array with it.</summary>
    [Fact]
    public void A_parameter_that_did_not_move_leaves_the_list_untouched() {
        var query = new QueryCommand("SELECT * FROM T WHERE X = @X AND Y = @Y");
        var ps = query.Parameters;

        var before = ps._nonCachedIndexes;
        Assert.True(ps.UpdateCache(0, InferedDbParamCache.Instance));
        Assert.Same(before, ps._nonCachedIndexes);

        Assert.True(ps.UpdateCache(0, TypedDbParamCache.Get(DbType.Int32)));
        var settled = ps._nonCachedIndexes;
        Assert.True(ps.UpdateCache(0, SizedDbParamCache.Get(DbType.String, 50)));
        Assert.Same(settled, ps._nonCachedIndexes);
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
