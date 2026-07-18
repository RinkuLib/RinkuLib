using System.Data;
using System.Data.Common;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Queries;

/// <summary>
/// The parameter binding strategies: each writes its metadata onto the parameter it creates, updates the
/// bound value in place, removes on a null update, and refuses to update something that is not a parameter.
/// </summary>
[Collection("ParamGetterMakers")]
public class ParamInfoTests {
    static FakeCommand Cmd() => new();

    static DbParameter Single(FakeCommand cmd) {
        Assert.Equal(1, cmd.Parameters.Count);
        return (DbParameter)cmd.Parameters[0]!;
    }

    static void AssertCommonLifecycle(DbParamInfo info) {
        var cmd = Cmd();
        object boxed = 41;
        Assert.True(info.SaveUse("@p", cmd, ref boxed));
        var p = Assert.IsAssignableFrom<IDbDataParameter>(boxed);
        Assert.Equal(41, p.Value);

        object? current = boxed;
        Assert.True(info.Update(cmd, ref current, 42));
        Assert.Equal(42, p.Value);

        object? notAParam = "plain";
        Assert.False(info.Update(cmd, ref notAParam, 1));

        Assert.True(info.Update(cmd, ref current, null));
        Assert.Null(current);
        Assert.Equal(0, cmd.Parameters.Count);

        object rebound = 7;
        info.SaveUse("@p", cmd, ref rebound);
        info.Remove(cmd, rebound);
        Assert.Equal(0, cmd.Parameters.Count);
    }

    [Fact]
    public void Scaled_writes_type_precision_and_scale() {
        var info = new ScaledDbParamCache(DbType.Decimal, 18, 4);
        var cmd = Cmd();
        Assert.True(info.Use("@p", (IDbCommand)cmd, 1.5m));
        var p = Single(cmd);
        Assert.Equal(DbType.Decimal, p.DbType);
        Assert.Equal(18, p.Precision);
        Assert.Equal(4, p.Scale);
        Assert.Equal(1.5m, p.Value);

        var cmd2 = Cmd();
        Assert.True(info.Use("@p", (DbCommand)cmd2, 2.5m));
        Assert.Equal(2.5m, Single(cmd2).Value);
        AssertCommonLifecycle(info);
    }

    [Fact]
    public void DirectionalScaled_also_writes_the_direction() {
        var info = new DirectionalScaledDbParamCache(ParameterDirection.Output, DbType.Decimal, 10, 2);
        var cmd = Cmd();
        Assert.True(info.Use("@p", (IDbCommand)cmd, 3m));
        var p = Single(cmd);
        Assert.Equal(ParameterDirection.Output, p.Direction);
        Assert.Equal(10, p.Precision);

        var cmd2 = Cmd();
        Assert.True(info.Use("@p", (DbCommand)cmd2, 4m));
        Assert.Equal(ParameterDirection.Output, Single(cmd2).Direction);
        AssertCommonLifecycle(info);
    }

    [Fact]
    public void DirectionalSized_writes_direction_type_and_size() {
        var info = new DirectionalSizedDbParamCache(ParameterDirection.InputOutput, DbType.String, 200);
        var cmd = Cmd();
        Assert.True(info.Use("@p", (IDbCommand)cmd, "v"));
        var p = Single(cmd);
        Assert.Equal(ParameterDirection.InputOutput, p.Direction);
        Assert.Equal(200, p.Size);

        var cmd2 = Cmd();
        Assert.True(info.Use("@p", (DbCommand)cmd2, "w"));
        Assert.Equal(200, Single(cmd2).Size);
        AssertCommonLifecycle(info);
    }

    [Fact]
    public void Directional_writes_direction_and_type() {
        var info = new DirectionalDbParamCache(ParameterDirection.ReturnValue, DbType.Int32);
        var cmd = Cmd();
        Assert.True(info.Use("@p", (IDbCommand)cmd, 5));
        var p = Single(cmd);
        Assert.Equal(ParameterDirection.ReturnValue, p.Direction);
        Assert.Equal(DbType.Int32, p.DbType);

        var cmd2 = Cmd();
        Assert.True(info.Use("@p", (DbCommand)cmd2, 6));
        Assert.Equal(DbType.Int32, Single(cmd2).DbType);
        AssertCommonLifecycle(info);
    }

    [Fact]
    public void Typed_writes_the_type_and_shares_instances() {
        var info = TypedDbParamCache.Get(DbType.Int64);
        Assert.Same(info, TypedDbParamCache.Get(DbType.Int64));
        var cmd = Cmd();
        Assert.True(info.Use("@p", (IDbCommand)cmd, 5L));
        Assert.Equal(DbType.Int64, Single(cmd).DbType);

        var cmd2 = Cmd();
        Assert.True(info.Use("@p", (DbCommand)cmd2, 6L));
        Assert.Equal(DbType.Int64, Single(cmd2).DbType);
        AssertCommonLifecycle(info);
    }

    [Fact]
    public void Typed_get_with_a_size_routes_sized_types_to_the_sized_cache() {
        Assert.IsType<SizedDbParamCache>(TypedDbParamCache.Get(DbType.String, 100));
        Assert.IsType<TypedDbParamCache>(TypedDbParamCache.Get(DbType.Int32, 100));
    }

    [Fact]
    public void Sized_writes_type_and_size_and_shares_instances() {
        var info = SizedDbParamCache.Get(DbType.String, 500);
        Assert.Same(info, SizedDbParamCache.Get(DbType.String, 500));
        var cmd = Cmd();
        Assert.True(info.Use("@p", (IDbCommand)cmd, "v"));
        var p = Single(cmd);
        Assert.Equal(DbType.String, p.DbType);
        Assert.Equal(500, p.Size);

        var cmd2 = Cmd();
        Assert.True(info.Use("@p", (DbCommand)cmd2, "w"));
        Assert.Equal(500, Single(cmd2).Size);
        AssertCommonLifecycle(info);
    }

    [Fact]
    public void Sized_rejects_a_type_that_carries_no_size() {
        Assert.Throws<ArgumentException>(() => SizedDbParamCache.Get(DbType.Int32, 10));
        Assert.False(SizedDbParamCache.TryGet(DbType.Boolean, 10, out _));
        Assert.True(SizedDbParamCache.TryGet(DbType.Xml, 10, out var xml));
        Assert.Equal(10, xml.Size);
    }

    [Theory]
    [InlineData(DbType.AnsiString)]
    [InlineData(DbType.AnsiStringFixedLength)]
    [InlineData(DbType.StringFixedLength)]
    public void Sized_covers_every_sized_type(DbType type) {
        var info = SizedDbParamCache.Get(type, 42);
        Assert.Equal(type, info.Type);
    }

    [Fact]
    public void Sized_cache_stops_growing_past_the_limit() {
        var first = SizedDbParamCache.Get(DbType.Binary, 1);
        Assert.Same(first, SizedDbParamCache.Get(DbType.Binary, 1));
        for (int i = 1; i <= 520; i++)
            SizedDbParamCache.Get(DbType.Binary, i * 3);
        var overflow = SizedDbParamCache.Get(DbType.Binary, 2);
        Assert.NotSame(overflow, SizedDbParamCache.Get(DbType.Binary, 2));
    }

    [Fact]
    public void Infered_leaves_the_type_to_the_driver() {
        var info = InferedDbParamCache.Instance;
        Assert.False(info.IsCached);
        Assert.True(InferedDbParamCache.ForceInfered.IsCached);
        var cmd = Cmd();
        Assert.True(info.Use("@p", (IDbCommand)cmd, 5));
        Assert.Equal(5, Single(cmd).Value);

        var cmd2 = Cmd();
        Assert.True(info.Use("@p", (DbCommand)cmd2, 6));
        Assert.Equal(6, Single(cmd2).Value);
        AssertCommonLifecycle(info);
    }

    [Fact]
    public void Legacy_command_parameters_take_precision_scale_through_the_interface() {
        var cmd = new LegacyCommand();
        new ScaledDbParamCache(DbType.Decimal, 12, 3).Use("@p", cmd, 1.5m);
        var p = (IDbDataParameter)cmd.Parameters[0]!;
        Assert.Equal((byte)12, p.Precision);
        Assert.Equal((byte)3, p.Scale);

        var cmd2 = new LegacyCommand();
        new DirectionalScaledDbParamCache(ParameterDirection.Output, DbType.Decimal, 9, 2).Use("@p", cmd2, 2m);
        var p2 = (IDbDataParameter)cmd2.Parameters[0]!;
        Assert.Equal((byte)9, p2.Precision);
        Assert.Equal(ParameterDirection.Output, p2.Direction);
    }

    [Fact]
    public void RemoveSingle_on_a_legacy_command_matches_by_name() {
        var cmd = new LegacyCommand();
        InferedDbParamCache.Instance.Use("@a", cmd, 1);
        cmd.Parameters.Add("not a parameter");
        InferedDbParamCache.Instance.Use("@b", cmd, 2);
        Assert.True(DbParamInfo.RemoveSingle("@b", cmd));
        Assert.False(DbParamInfo.RemoveSingle("@missing", cmd));  
    }

    [Fact]
    public void TryGetParamInfo_default_road_over_a_legacy_command() {
        var cmd = new LegacyCommand();
        InferedDbParamCache.Instance.Use("@a", cmd, 1);
        InferedDbParamCache.Instance.Use("@b", cmd, 2);
        cmd.Parameters.Add("a plain object, not a parameter");
        Assert.True(IDbParamInfoGetter.TryGetParamInfo(cmd, "@b", out var info));
        Assert.NotNull(info);
        Assert.False(IDbParamInfoGetter.TryGetParamInfo(cmd, "@nope", out _));
    }

    [Fact]
    public void A_collection_holding_a_non_parameter_is_navigated_safely() {
        var cmd = new LegacyCommand();
        InferedDbParamCache.Instance.Use("@a", cmd, 1);
        cmd.Parameters.Add("plain object");
        InferedDbParamCache.Instance.Use("@b", cmd, 2);

        var cache = new DefaultParamCache(cmd);
        Assert.Equal(["@a", "@b"], cache.EnumerateParameters().Select(kv => kv.Key));
        Assert.ThrowsAny<Exception>(() => cache.MakeInfoAt(1)); 
        Assert.True(cache.TryGetInfo("@b", out _));      
        Assert.False(cache.TryGetInfo("@nope", out _));

        var force = new ForceInferedParamCache(cmd);
        Assert.Equal(["@a", "@b"], force.EnumerateParameters().Select(kv => kv.Key));
        Assert.True(force.TryGetInfo("@b", out _));
        Assert.False(force.TryGetInfo("@nope", out _));
    }

    [Fact]
    public void EnumerateParameters_skips_non_data_parameters() {
        var cmd = Cmd();
        InferedDbParamCache.Instance.Use("@a", (IDbCommand)cmd, 1);
        Assert.Single(new DefaultParamCache(cmd).EnumerateParameters());
        Assert.Single(new ForceInferedParamCache(cmd).EnumerateParameters());
    }

    [Fact]
    public void MakeInfoAt_on_a_missing_parameter_throws() {
        var cmd = Cmd();
        var cache = new DefaultParamCache(cmd);
        Assert.ThrowsAny<Exception>(() => cache.MakeInfoAt(0));
    }

    [Fact]
    public void RemoveSingle_removes_by_name_on_both_interfaces() {
        var cmd = Cmd();
        InferedDbParamCache.Instance.Use("@a", (IDbCommand)cmd, 1);
        InferedDbParamCache.Instance.Use("@b", (IDbCommand)cmd, 2);
        Assert.True(DbParamInfo.RemoveSingle("@a", (IDbCommand)cmd));  
        Assert.False(DbParamInfo.RemoveSingle("@a", (IDbCommand)cmd)); 
        InferedDbParamCache.Instance.Use("@c", (DbCommand)cmd, 3);
        Assert.True(DbParamInfo.RemoveSingle("@b", (DbCommand)cmd));
        Assert.False(DbParamInfo.RemoveSingle("@zzz", (DbCommand)cmd)); 
    }

    [Fact]
    public void DefaultParamCache_reads_a_command_and_rounds_sizes() {
        var cmd = Cmd();
        var p = cmd.CreateParameter();
        p.ParameterName = "@s";
        p.DbType = DbType.String;
        p.Size = 60;
        cmd.Parameters.Add(p);
        var pInt = cmd.CreateParameter();
        pInt.ParameterName = "@i";
        pInt.DbType = DbType.Int32;
        cmd.Parameters.Add(pInt);

        var cache = new DefaultParamCache(cmd);
        Assert.Equal(["@s", "@i"], cache.EnumerateParameters().Select(kv => kv.Key));

        var sized = Assert.IsType<SizedDbParamCache>(cache.MakeInfoAt(0));
        Assert.Equal(100, sized.Size);
        Assert.IsType<TypedDbParamCache>(cache.MakeInfoAt(1));

        Assert.True(cache.TryGetInfo("@i", out var byName));
        Assert.IsType<TypedDbParamCache>(byName);
        Assert.False(cache.TryGetInfo("@nope", out _));
    }

    [Theory]
    [InlineData(90, 100)]
    [InlineData(300, 500)]
    [InlineData(3000, 4000)]
    [InlineData(9000, -1)]
    public void DefaultParamCache_size_tiers(int size, int expected) {
        var cmd = Cmd();
        var p = cmd.CreateParameter();
        p.ParameterName = "@s";
        p.DbType = DbType.String;
        p.Size = size;
        cmd.Parameters.Add(p);
        var sized = Assert.IsType<SizedDbParamCache>(new DefaultParamCache(cmd).MakeInfoAt(0));
        Assert.Equal(expected, sized.Size);
    }

    [Fact]
    public void ForceInfered_claims_only_its_command_type() {
        Assert.True(ForceInferedParamCache.GetInfoGetterMaker<FakeCommand>(Cmd(), out var getter));
        Assert.False(ForceInferedParamCache.GetInfoGetterMaker<System.Data.SqlClient.SqlCommand>(Cmd(), out _));

        var cmd = Cmd();
        InferedDbParamCache.Instance.Use("@a", (IDbCommand)cmd, 1);
        var cache = new ForceInferedParamCache(cmd);
        Assert.Equal(["@a"], cache.EnumerateParameters().Select(kv => kv.Key));
        Assert.Same(InferedDbParamCache.ForceInfered, cache.MakeInfoAt(0));
        Assert.True(cache.TryGetInfo("@a", out var info));
        Assert.Same(InferedDbParamCache.ForceInfered, info);
        Assert.False(cache.TryGetInfo("@nope", out _));
    }

    [Fact]
    public void TryGetParamInfo_uses_the_default_road_when_no_maker_claims() {
        var cmd = Cmd();
        InferedDbParamCache.Instance.Use("@a", (IDbCommand)cmd, 1);
        Assert.True(IDbParamInfoGetter.TryGetParamInfo(cmd, "@a", out var info));
        Assert.NotNull(info);
        Assert.False(IDbParamInfoGetter.TryGetParamInfo(cmd, "@nope", out _));
    }
    sealed class MakerProbeCommand : FakeCommand;

    [Fact]
    public void TryGetParamInfo_skips_a_passing_maker_then_takes_the_one_that_claims() {
        var cmd = new MakerProbeCommand();
        InferedDbParamCache.Instance.Use("@a", (IDbCommand)cmd, 1);
        IDbParamInfoGetter.ParamGetterMakers.Add(ForceInferedParamCache.GetInfoGetterMaker<System.Data.SqlClient.SqlCommand>);
        IDbParamInfoGetter.ParamGetterMakers.Add(ForceInferedParamCache.GetInfoGetterMaker<MakerProbeCommand>);
        try {
            Assert.True(IDbParamInfoGetter.TryGetParamInfo(cmd, "@a", out var info));
            Assert.Same(InferedDbParamCache.ForceInfered, info);
            Assert.False(IDbParamInfoGetter.TryGetParamInfo(cmd, "@nope", out _));
        }
        finally {
            IDbParamInfoGetter.ParamGetterMakers.Remove(ForceInferedParamCache.GetInfoGetterMaker<System.Data.SqlClient.SqlCommand>);
            IDbParamInfoGetter.ParamGetterMakers.Remove(ForceInferedParamCache.GetInfoGetterMaker<MakerProbeCommand>);
        }
    }
}
