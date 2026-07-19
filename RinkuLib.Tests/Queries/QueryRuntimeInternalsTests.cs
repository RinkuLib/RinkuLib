using System.Collections;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Text;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Queries;

/// <summary>
/// The runtime bookkeeping around a command: the learned-parser cache and its merge rules, the parameter
/// ledger, the differential spread updates on a live command, and the emptiness peek for lazy sequences.
/// </summary>
[Collection("ParamGetterMakers")]
public class QueryRuntimeInternalsTests {
    static ITypeParser<int> IntParser() {
        ColumnInfo[] cols = [new("V", typeof(int), false)];
        return TypeParser.GetTypeParser<int>(ref cols);
    }

    static ColumnInfo[] Schema() => [new("V", typeof(int), false)];

    [Fact]
    public void Parser_cache_adds_then_merges_a_matching_shape() {
        var query = new QueryCommand("SELECT V FROM t WHERE /*A*/x = 1 AND /*B*/y = 2");
        var qt = query.QueryText;
        var parser = IntParser();
        var schema = Schema();
        int len = query.Mapper.Count;

        var mapA = new bool[len];
        mapA[query.Mapper.GetIndex("A")] = true;
        var cache = Array.Empty<ParsingCacheItem>().GetUpdatedCache(qt, mapA, schema, parser);
        Assert.Single(cache);
        Assert.Equal(2, cache[0].CondStates.Length);

        var same = cache.GetUpdatedCache(qt, mapA, schema, parser);
        Assert.Same(cache, same);
        Assert.Equal(2, same[0].CondStates.Length);

        var mapB = new bool[len];
        mapB[query.Mapper.GetIndex("B")] = true;
        var narrowed = cache.GetUpdatedCache(qt, mapB, schema, parser);
        Assert.Same(cache, narrowed);
        Assert.Empty(narrowed[0].CondStates);
    }

    [Fact]
    public void Parser_cache_narrowing_keeps_the_common_states() {
        var query = new QueryCommand("SELECT V FROM t WHERE /*A*/x = 1 AND /*B*/y = 2");
        var qt = query.QueryText;
        var parser = IntParser();
        var schema = Schema();
        int len = query.Mapper.Count;

        var both = new bool[len];
        both[query.Mapper.GetIndex("A")] = true;
        both[query.Mapper.GetIndex("B")] = true;
        var cache = Array.Empty<ParsingCacheItem>().GetUpdatedCache(qt, both, schema, parser);

        var onlyA = new bool[len];
        onlyA[query.Mapper.GetIndex("A")] = true;
        cache = cache.GetUpdatedCache(qt, onlyA, schema, parser);
        var state = Assert.Single(cache[0].CondStates);
        Assert.Equal(query.Mapper.GetIndex("A"), state >> 1);
        Assert.Equal(1, state & 1);
    }

    [Fact]
    public void Parser_cache_reorders_wider_entries_ahead_of_a_narrowed_one() {
        var query = new QueryCommand("SELECT V FROM t WHERE /*A*/x = 1 AND /*B*/y = 2");
        var qt = query.QueryText;
        var parser = IntParser();
        int len = query.Mapper.Count;

        ColumnInfo[] otherSchema = [new("W", typeof(long), false)];
        var mapA = new bool[len];
        mapA[query.Mapper.GetIndex("A")] = true;
        var cache = Array.Empty<ParsingCacheItem>().GetUpdatedCache(qt, mapA, otherSchema, parser);
        var mapB = new bool[len];
        mapB[query.Mapper.GetIndex("B")] = true;
        cache = cache.GetUpdatedCache(qt, mapB, Schema(), parser);
        Assert.Equal(2, cache.Length);

        var flipped = new bool[len];
        flipped[query.Mapper.GetIndex("A")] = true;
        cache = cache.GetUpdatedCache(qt, flipped, Schema(), parser);
        Assert.Equal(2, cache.Length);
        Assert.Empty(cache[^1].CondStates);
        Assert.Equal(2, cache[0].CondStates.Length);
    }

    [Fact]
    public void Parser_cache_separates_result_set_indexes() {
        var query = new QueryCommand("SELECT V FROM t WHERE /*A*/x = 1");
        var qt = query.QueryText;
        var parser = IntParser();
        var map = new bool[query.Mapper.Count];

        var cache = Array.Empty<ParsingCacheItem>().GetUpdatedCache(qt, map, Schema(), parser, 0);
        cache = cache.GetUpdatedCache(qt, map, Schema(), parser, 1);
        Assert.Equal(2, cache.Length);
    }

    [Fact]
    public void Cached_parser_lookup_matches_on_condition_states() {
        var query = new QueryCommand("SELECT V FROM t WHERE /*A*/x = 1");
        var parser = IntParser();
        int len = query.Mapper.Count;
        var mapA = new bool[len];
        mapA[query.Mapper.GetIndex("A")] = true;
        query.ParsingCache = Array.Empty<ParsingCacheItem>().GetUpdatedCache(query.QueryText, mapA, Schema(), parser);

        Assert.True(query.TryGetCachedParser<int>(mapA.AsSpan(), out var hit));
        Assert.NotNull(hit);
        var mapOff = new bool[len];
        Assert.False(query.TryGetCachedParser<int>(mapOff.AsSpan(), out _));
        Assert.False(query.TryGetCachedParser<int>(mapA.AsSpan(), out _, resultSetIndex: 1));
        Assert.False(query.TryGetCachedParser<string>(mapA.AsSpan(), out _));

        var variables = new object?[len];
        variables[query.Mapper.GetIndex("A")] = "on";
        Assert.True(query.TryGetCachedParser<int>(variables, out _));
        Assert.False(query.TryGetCachedParser<int>(new object?[len], out _));
        Assert.False(query.TryGetCachedParser<string>(variables, out _));
        Assert.False(query.TryGetCachedParser<int>(variables, out _, resultSetIndex: 1));
    }

    [Fact]
    public void Parameter_ledger_updates_and_reindexes() {
        var query = new QueryCommand("SELECT * FROM t WHERE a = ?@A AND b = ?@B");
        var ps = query.Parameters;
        Assert.False(ps.UpdateCache(-1, InferedDbParamCache.ForceInfered));
        Assert.False(ps.UpdateCache(9, InferedDbParamCache.ForceInfered));

        int a = query.Mapper.GetIndex("@A"), b = query.Mapper.GetIndex("@B");
        Assert.True(ps.UpdateCache(b, InferedDbParamCache.ForceInfered));
        Assert.True(ps.IsCached(b));
        Assert.True(ps.UpdateCache(b, InferedDbParamCache.ForceInfered));
        Assert.True(ps.UpdateCache(a, InferedDbParamCache.ForceInfered));
        Assert.True(ps.UpdateCache(a, InferedDbParamCache.Instance));
        Assert.False(ps.IsCached(a));

        var vars = new object?[query.Mapper.Count];
        Assert.False(ps.NeedToCache(vars));
        vars[a] = 1;
        Assert.True(ps.NeedToCache(vars));

        Span<bool> map = stackalloc bool[query.Mapper.Count];
        Assert.False(ps.NeedToCache(map));
        map[a] = true;
        Assert.True(ps.NeedToCache(map));

        ps.UpdateCachedIndexes();
        Assert.True(ps.NeedToCache(map));
        Assert.True(ps.UpdateCache(a, InferedDbParamCache.ForceInfered));
        ps.UpdateCachedIndexes();
        Assert.False(ps.NeedToCache(map));
        Assert.False(ps.NeedToCache(vars));
    }

    [Fact]
    public void Special_handler_ledger_skips_settled_handlers() {
        var query = new QueryCommand("SELECT * FROM t WHERE b IN (?@Ids_X)");
        var cmd = new FakeCommand();
        InferedDbParamCache.Instance.Use("@Ids", (IDbCommand)cmd, 1);
        var handler = query.Parameters.SpecialHandlers[0];
        Assert.False(handler.IsCached);
        query.Parameters.UpdateSpecialHandlers(new ForceInferedParamCache(cmd));
        Assert.True(handler.IsCached);
        query.Parameters.UpdateSpecialHandlers(new ForceInferedParamCache(cmd));
        Assert.True(query.Parameters.IsCached(query.Mapper.GetIndex("@Ids")));
    }
    sealed class MakerProbeCommand : FakeCommand;

    [Fact]
    public void Learning_prefers_a_registered_maker() {
        var query = new QueryCommand("SELECT * FROM t WHERE a = ?@A");
        var cmd = new MakerProbeCommand();
        InferedDbParamCache.Instance.Use("@A", (IDbCommand)cmd, 1);
        IDbParamInfoGetter.ParamGetterMakers.Add(ForceInferedParamCache.GetInfoGetterMaker<MakerProbeCommand>);
        try {
            query.UpdateCache(cmd);
            Assert.True(query.Parameters.IsCached(query.Mapper.GetIndex("@A")));
        }
        finally {
            IDbParamInfoGetter.ParamGetterMakers.Remove(ForceInferedParamCache.GetInfoGetterMaker<MakerProbeCommand>);
        }
    }

    [Fact]
    public void Learning_skips_makers_that_pass() {
        var query = new QueryCommand("SELECT * FROM t WHERE a = ?@A");
        var cmd = new FakeCommand();
        InferedDbParamCache.Instance.Use("@A", (IDbCommand)cmd, 1);
        IDbParamInfoGetter.ParamGetterMakers.Add(ForceInferedParamCache.GetInfoGetterMaker<System.Data.SqlClient.SqlCommand>);
        try {
            query.UpdateCache(cmd);
            Assert.True(query.Parameters.IsCached(query.Mapper.GetIndex("@A")));
        }
        finally {
            IDbParamInfoGetter.ParamGetterMakers.Remove(ForceInferedParamCache.GetInfoGetterMaker<System.Data.SqlClient.SqlCommand>);
        }
    }


    static QueryBuilderCommand<FakeCommand> Bound(out FakeCommand cmd) {
        var query = new QueryCommand("SELECT * FROM t WHERE b IN (?@Ids_X)");
        cmd = new FakeCommand();
        return new QueryBuilderCommand<FakeCommand>(query, cmd);
    }

    [Fact]
    public void Bound_spread_update_transitions() {
        var b = Bound(out var cmd);
        Assert.True(b.Use("@Ids", new[] { 1, 2 }));
        Assert.Equal(2, cmd.Parameters.Count);

        Assert.True(b.Use("@Ids", new[] { 3, 4 }));
        Assert.Equal([3, 4], cmd.BoundParameters.Select(p => p.Value).Cast<int>());

        Assert.True(b.Use("@Ids", new[] { 5 }));
        Assert.Equal(1, cmd.Parameters.Count);

        Assert.True(b.Use("@Ids", Enumerable.Range(1, 12).ToArray()));
        Assert.Equal(12, cmd.Parameters.Count);
        Assert.Equal("@Ids_12", cmd.BoundParameters[^1].ParameterName);

        Assert.True(b.Use("@Ids", null));
        Assert.Equal(0, cmd.Parameters.Count);
        Assert.True(b.Use("@Ids", null));
    }

    [Fact]
    public void Bound_spread_rejects_a_non_collection_update() {
        var b = Bound(out var cmd);
        Assert.True(b.Use("@Ids", new[] { 1 }));
        Assert.False(b.Use("@Ids", 5));
    }

    [Fact]
    public void Bound_spread_shrinks_to_empty_and_drops_all() {
        var b = Bound(out var cmd);
        Assert.True(b.Use("@Ids", new[] { 1, 2, 3 }));
        Assert.True(b.Use("@Ids", Array.Empty<int>()));
        Assert.Equal(0, cmd.Parameters.Count);
        Assert.Null(b["@Ids"]);
    }

    sealed class FlakyParam(int failAt) : DbParamInfo(true) {
        int _uses;
        public override bool Use(string paramName, IDbCommand cmd, object value) => true;
        public override bool Use(string paramName, DbCommand cmd, object value) => true;
        public override void Remove(IDbCommand cmd, object currentValue) { }
        public override bool SaveUse(string paramName, IDbCommand cmd, ref object value)
            => ++_uses != failAt;
        public override bool Update(IDbCommand cmd, ref object? currentValue, object? newValue)
            => ++_uses != failAt;
    }

    static MultiVariableHandler HandlerWith(DbParamInfo cached)
        => new("@Ids") { CachedParam = cached };

    [Fact]
    public void Spread_update_reports_a_failed_same_size_update() {
        var h = HandlerWith(new FlakyParam(failAt: 2));
        var cmd = new FakeCommand();
        object? current = new object[] { 1, 2 };
        Assert.False(h.Update(cmd, ref current, new[] { 3, 4 }));
    }

    [Fact]
    public void Spread_update_reports_a_failed_shrink_update() {
        var h = HandlerWith(new FlakyParam(failAt: 1));
        var cmd = new FakeCommand();
        object? current = new object[] { 1, 2, 3 };
        Assert.False(h.Update(cmd, ref current, new[] { 4, 5 }));
    }

    [Fact]
    public void Spread_update_reports_a_failed_grow_update_and_saveuse() {
        var updateFail = HandlerWith(new FlakyParam(failAt: 1));
        object? current = new object[] { 1 };
        Assert.False(updateFail.Update(new FakeCommand(), ref current, new[] { 2, 3 }));

        var saveFail = HandlerWith(new FlakyParam(failAt: 2));
        object? current2 = new object[] { 1 };
        Assert.False(saveFail.Update(new FakeCommand(), ref current2, new[] { 2, 3 }));
    }

    [Fact]
    public void Spread_saveuse_reports_a_failed_bind() {
        var h = HandlerWith(new FlakyParam(failAt: 2));
        object? value = new[] { 1, 2, 3 };
        Assert.False(h.SaveUse(new FakeCommand(), ref value));
    }

    [Fact]
    public void Spread_saveuse_of_empty_is_absent() {
        var h = HandlerWith(InferedDbParamCache.ForceInfered);
        object? value = Array.Empty<int>();
        Assert.True(h.SaveUse(new FakeCommand(), ref value));
        Assert.Null(value);
    }

    [Fact]
    public void Spread_saveuse_crosses_the_digit_boundary() {
        var h = HandlerWith(InferedDbParamCache.ForceInfered);
        object? value = Enumerable.Range(1, 12).ToArray();
        var cmd = new FakeCommand();
        Assert.True(h.SaveUse(cmd, ref value));
        var arr = Assert.IsType<object[]>(value);
        Assert.Equal(12, arr.Length);
        Assert.Equal(12, cmd.Parameters.Count);
    }

    [Fact]
    public void Spread_update_grows_across_the_digit_boundary() {
        var h = HandlerWith(InferedDbParamCache.ForceInfered);
        object? value = Enumerable.Range(1, 3).ToArray();
        var cmd = new FakeCommand();
        Assert.True(h.SaveUse(cmd, ref value));
        Assert.True(h.Update(cmd, ref value, Enumerable.Range(1, 15).ToArray()));
        var arr = Assert.IsType<object[]>(value);
        Assert.Equal(15, arr.Length);
    }

    [Fact]
    public void Spread_update_from_null_and_to_non_collection() {
        var h = HandlerWith(InferedDbParamCache.ForceInfered);
        object? current = null;
        Assert.True(h.Update(new FakeCommand(), ref current, null));
        Assert.False(h.Update(new FakeCommand(), ref current, 5));
        current = "not saved";
        var stale = current;
        Refusals.Raises(ErrorCodes.ValueNotSet, () => {
            var local = stale;
            h.Update(new FakeCommand(), ref local, new[] { 1 });
        });
    }

    [Fact]
    public void Spread_update_to_null_from_a_bad_current_returns_false() {
        var h = HandlerWith(InferedDbParamCache.ForceInfered);
        object? current = "not an array";
        Assert.False(h.Update(new FakeCommand(), ref current, null));
    }

    [Fact]
    public void Spread_use_on_a_DbCommand_binds_and_crosses_the_digit_boundary() {
        var h = HandlerWith(InferedDbParamCache.ForceInfered);
        var cmd = new FakeCommand();
        object? value = Enumerable.Range(1, 12).ToArray();
        Assert.True(h.Use((DbCommand)cmd, ref value));
        Assert.Equal(12, cmd.Parameters.Count);
        Assert.Equal("@Ids_12", cmd.BoundParameters[^1].ParameterName);

        object? notCollection = 5;
        Assert.False(h.Use((DbCommand)cmd, ref notCollection));
    }

    [Fact]
    public void Spread_use_on_an_IDbCommand_binds_and_crosses_the_digit_boundary() {
        var h = HandlerWith(InferedDbParamCache.ForceInfered);
        IDbCommand cmd = new FakeCommand();
        object? value = Enumerable.Range(1, 12).ToArray();
        Assert.True(h.Use(cmd, ref value));
        Assert.Equal(12, cmd.Parameters.Count);

        object? notCollection = 5;
        Assert.False(h.Use(cmd, ref notCollection));
    }

    [Fact]
    public void Spread_use_on_an_IDbCommand_of_a_countless_sequence_rewrites_to_count() {
        var h = HandlerWith(InferedDbParamCache.ForceInfered);
        IDbCommand cmd = new FakeCommand();
        object? value = new PeekableSource(5, 6, 7);
        Assert.True(h.Use(cmd, ref value));
        Assert.Equal(3, value);
    }

    [Fact]
    public void Spread_use_on_a_DbCommand_of_a_countless_sequence_rewrites_to_count() {
        var h = HandlerWith(InferedDbParamCache.ForceInfered);
        var cmd = new FakeCommand();
        object? value = new PeekableSource(5, 6, 7);
        Assert.True(h.Use((DbCommand)cmd, ref value));
        Assert.Equal(3, value);
    }

    [Fact]
    public void Spread_update_from_null_current_with_empty_new_removes_nothing() {
        var h = HandlerWith(InferedDbParamCache.ForceInfered);
        var cmd = new FakeCommand();
        object? current = Array.Empty<object>();
        Assert.True(h.Update(cmd, ref current, null));
        Assert.Null(current);
    }

    sealed class PeekableSource(params int[] items) : IEnumerable {
        public IEnumerator GetEnumerator() {
            foreach (var i in items) yield return i;
        }
    }

    [Fact]
    public void Spread_handle_reads_a_count_int() {
        var h = HandlerWith(InferedDbParamCache.ForceInfered);
        var sb = new ValueStringBuilder(stackalloc char[64]);
        sb.Append("IN (");
        h.Handle(ref sb, 2);
        sb.Append(')');
        Assert.Equal("IN (@Ids_1, @Ids_2)", sb.ToStringAndDispose());
    }

    [Fact]
    public void Spread_handle_rejects_a_value_with_no_count() {
        var h = HandlerWith(InferedDbParamCache.ForceInfered);
        var sb = new ValueStringBuilder(stackalloc char[8]);
        bool threw = false;
        try {
            h.Handle(ref sb, new object());
        }
        catch (RinkuException e) when (e.Code == ErrorCodes.HandlerValueType) {
            threw = true;
        }
        finally {
            sb.Dispose();
        }
        Assert.True(threw);
    }

    [Fact]
    public void Bound_spread_from_an_empty_start_stays_absent() {
        var b = Bound(out var cmd);
        Assert.True(b.Use("@Ids", Array.Empty<int>()));
        Assert.Equal(0, cmd.Parameters.Count);
        Assert.Null(b["@Ids"]);
    }

    [Fact]
    public void Bound_spread_reset_strips_everything() {
        var b = Bound(out var cmd);
        b.Use("@Ids", new[] { 1, 2, 3 });
        b.Reset();
        Assert.Equal(0, cmd.Parameters.Count);
        Assert.Null(b["@Ids"]);
    }

    [Fact]
    public void Bound_spread_remove_by_name_strips_the_parameters() {
        var b = Bound(out var cmd);
        b.Use("@Ids", new[] { 1, 2 });
        b.Remove("@Ids");
        Assert.Equal(0, cmd.Parameters.Count);
        b.Remove("@Ids");
        Assert.Equal(0, cmd.Parameters.Count);
    }


    struct Args {
        public int Min { get; set; }
        public int[]? Ids { get; set; }
    }

    [Fact]
    public void DbCommand_road_binds_spread_and_plain_and_prunes_absent() {
        var query = new QueryCommand("SELECT * FROM t WHERE a > ?@Min AND b IN (?@Ids_X)");
        var usage = new bool[query.Mapper.Count];

        var cmd = new FakeCommand();
        query.SetCommand(cmd, new Args { Min = 2, Ids = [7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17] }, usage);
        Assert.Equal("SELECT * FROM t WHERE a > @Min AND b IN (@Ids_1, @Ids_2, @Ids_3, @Ids_4, @Ids_5, @Ids_6, @Ids_7, @Ids_8, @Ids_9, @Ids_10, @Ids_11)", cmd.CommandText);

        var cmd2 = new FakeCommand();
        query.SetCommand(cmd2, new Args { Min = 2, Ids = [] }, usage);
        Assert.Equal("SELECT * FROM t WHERE a > @Min AND b IN ()", cmd2.CommandText);
    }

    [Fact]
    public void DbCommand_road_with_prebound_values_array() {
        var query = new QueryCommand("SELECT * FROM t WHERE a > ?@Min AND b IN (?@Ids_X)");
        int min = query.Mapper.GetIndex("@Min"), ids = query.Mapper.GetIndex("@Ids");

        var vars = new object?[query.Mapper.Count];
        vars[min] = 3;
        vars[ids] = new[] { 7, 8 };
        var cmd = new FakeCommand();
        Assert.True(query.SetCommand(cmd, vars));
        Assert.Equal("SELECT * FROM t WHERE a > @Min AND b IN (@Ids_1, @Ids_2)", cmd.CommandText);
        Assert.Equal([3, 7, 8], cmd.BoundParameters.Select(p => p.Value).Cast<int>());

        var empty = new object?[query.Mapper.Count];
        empty[min] = 3;
        empty[ids] = Array.Empty<int>();
        var cmd2 = new FakeCommand();
        query.SetCommand(cmd2, empty);
        Assert.Equal("SELECT * FROM t WHERE a > @Min", cmd2.CommandText);
    }

    [Fact]
    public void IDbCommand_road_with_prebound_values_array() {
        var query = new QueryCommand("SELECT * FROM t WHERE a > ?@Min AND b IN (?@Ids_X)");
        int min = query.Mapper.GetIndex("@Min"), ids = query.Mapper.GetIndex("@Ids");
        var vars = new object?[query.Mapper.Count];
        vars[min] = 3;
        vars[ids] = new[] { 7, 8 };
        IDbCommand cmd = new FakeCommand();
        Assert.True(query.SetCommand(cmd, vars));
        Assert.Equal("SELECT * FROM t WHERE a > @Min AND b IN (@Ids_1, @Ids_2)", cmd.CommandText);
    }

    [Fact]
    public async Task UpdateCacheAsync_learns_like_the_sync_path() {
        var query = new QueryCommand("SELECT * FROM t WHERE a = ?@A");
        var cmd = new FakeCommand();
        InferedDbParamCache.Instance.Use("@A", (IDbCommand)cmd, 1);
        await query.UpdateCacheAsync(cmd, TestContext.Current.CancellationToken);
        Assert.True(query.Parameters.IsCached(query.Mapper.GetIndex("@A")));
    }

    [Fact]
    public void DbCommand_road_spread_of_a_lazy_sequence_binds() {
        var query = new QueryCommand("SELECT * FROM t WHERE b IN (?@Ids_X)");
        var usage = new bool[query.Mapper.Count];
        var cmd = new FakeCommand();
        query.SetCommand(cmd, new { Ids = Lazy(5, 6) }, usage);
        Assert.Equal("SELECT * FROM t WHERE b IN (@Ids_1, @Ids_2)", cmd.CommandText);
        Assert.Equal([5, 6], cmd.BoundParameters.Select(p => p.Value).Cast<int>());
    }


    [Fact]
    public void In_memory_builder_binds_spread_and_renders() {
        var query = new QueryCommand("SELECT * FROM t WHERE a > ?@Min AND b IN (?@Ids_X)");
        var b = query.StartBuilder();
        b.UseWith(new { Min = 4, Ids = new[] { 1, 2 } });
        Assert.Equal("SELECT * FROM t WHERE a > @Min AND b IN (@Ids_1, @Ids_2)", b.GetQueryText());

        var b2 = query.StartBuilder();
        b2.UseWith(new Args { Min = 4 });
        Assert.Equal("SELECT * FROM t WHERE a > @Min", b2.GetQueryText());
    }

    [Fact]
    public void A_template_with_no_conditions_returns_its_string_untouched() {
        var query = new QueryCommand("SELECT 1");
        var map = new bool[query.Mapper.Count];
        Assert.Same(query.QueryText.QueryString, query.QueryText.Parse(map, new NoTypeAccessor()));
    }

    [Fact]
    public void A_required_handler_missing_on_the_usage_map_road_throws() {
        var query = new QueryCommand("SELECT * FROM t WHERE x = @V_N");
        var map = new bool[query.Mapper.Count];
        Assert.Throws<RequiredHandlerValueException>(() => query.QueryText.Parse(map, new NoTypeAccessor()));
    }

    sealed class NonDisposableEnumerable(int[] items) : IEnumerable {
        public IEnumerator GetEnumerator() => new Enum(items);
        sealed class Enum(int[] items) : IEnumerator {
            int _i = -1;
            public object Current => items[_i];
            public bool MoveNext() => ++_i < items.Length;
            public void Reset() => _i = -1;
        }
    }

    [Fact]
    public void Prebound_road_hasany_over_countable_and_nondisposable_shapes() {
        var query = new QueryCommand("SELECT * FROM t WHERE b IN (?@Ids_X)");
        int ids = query.Mapper.GetIndex("@Ids");
        void Render(object? val, string expected) {
            var cmd = new FakeCommand();
            var vars = new object?[query.Mapper.Count];
            vars[ids] = val;
            query.SetCommand(cmd, vars);
            Assert.Equal(expected, cmd.CommandText);
        }
        Render(new HashSet<int>(), "SELECT * FROM t");                
        Render(new HashSet<int> { 1, 2 }, "SELECT * FROM t WHERE b IN (@Ids_1, @Ids_2)");
        Render(new NonDisposableEnumerable([]), "SELECT * FROM t");   
        Render(new NonDisposableEnumerable([9]), "SELECT * FROM t WHERE b IN (@Ids_1)");
    }

    class ClassArgs {
        public int Min { get; set; }
    }

    [Fact]
    public void Ref_overloads_take_a_class_object_on_both_interfaces() {
        var query = new QueryCommand("SELECT * FROM t WHERE a > ?@Min");
        var args = new ClassArgs { Min = 5 };
        var usage = new bool[query.Mapper.Count];

        var d = new FakeCommand();
        query.SetCommand((DbCommand)d, ref args, usage);
        Assert.Equal("SELECT * FROM t WHERE a > @Min", d.CommandText);

        var i = new FakeCommand();
        query.SetCommand((IDbCommand)i, ref args, usage);
        Assert.Equal("SELECT * FROM t WHERE a > @Min", i.CommandText);
    }

    [Fact]
    public void Struct_object_with_a_spread_through_the_IDbCommand_accessor_road() {
        var query = new QueryCommand("SELECT * FROM t WHERE b IN (?@Ids_X)");
        var usage = new bool[query.Mapper.Count];
        IDbCommand cmd = new FakeCommand();
        query.SetCommand(cmd, new Args { Ids = [7, 8] }, usage);
        Assert.Equal("SELECT * FROM t WHERE b IN (@Ids_1, @Ids_2)", cmd.CommandText);
    }

    [Fact]
    public void Prebound_road_hasany_over_every_shape() {
        var query = new QueryCommand("SELECT * FROM t WHERE b IN (?@Ids_X)");
        int ids = query.Mapper.GetIndex("@Ids");
        void Render(object? val, string expected) {
            var cmd = new FakeCommand();
            var vars = new object?[query.Mapper.Count];
            vars[ids] = val;
            query.SetCommand(cmd, vars);
            Assert.Equal(expected, cmd.CommandText);
        }
        Render(new List<object>(), "SELECT * FROM t");            
        Render(new List<object> { 1, 2 }, "SELECT * FROM t WHERE b IN (@Ids_1, @Ids_2)");
        Render(Array.Empty<int>(), "SELECT * FROM t");            
        Render(Lazy(), "SELECT * FROM t");                         
        Render(Lazy(3), "SELECT * FROM t WHERE b IN (@Ids_1)");   
        Render("ab", "SELECT * FROM t WHERE b IN (@Ids_1, @Ids_2)");
        Render(2, "SELECT * FROM t WHERE b IN (@Ids_1, @Ids_2)");  
    }

    [Fact]
    public void Peekable_wrapper_streams_once_then_is_empty() {
        var source = new List<object> { 1, 2, 3 }.GetEnumerator();
        source.MoveNext();
        var wrapper = new PeekableWrapper(source.Current, source);
        Assert.Equal([1, 2, 3], wrapper.Cast<int>());
        Assert.Empty(wrapper);  
        wrapper.Dispose();
        wrapper.Dispose();      
    }


    static IEnumerable<object> Lazy(params int[] items) {
        foreach (var i in items)
            yield return i;
    }

    sealed class CountlessCollection(int[] items) : ICollection {
        public int Count => items.Length;
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public void CopyTo(Array array, int index) => items.CopyTo(array, index);
        public IEnumerator GetEnumerator() => items.GetEnumerator();
    }

    [Fact]
    public void Value_array_road_prunes_empty_shapes_and_binds_full_ones() {
        var query = new QueryCommand("SELECT * FROM t WHERE b IN (?@Ids_X)");
        int ids = query.Mapper.GetIndex("@Ids");

        var cmd = new FakeCommand();
        var vars = new object?[query.Mapper.Count];
        vars[ids] = Lazy();
        query.SetCommand(cmd, vars);
        Assert.Equal("SELECT * FROM t", cmd.CommandText);
        Assert.Null(vars[ids]);

        cmd = new FakeCommand();
        vars[ids] = Lazy(7, 8);
        query.SetCommand(cmd, vars);
        Assert.Equal("SELECT * FROM t WHERE b IN (@Ids_1, @Ids_2)", cmd.CommandText);
        Assert.Equal([7, 8], cmd.BoundParameters.Select(p => p.Value).Cast<int>());

        cmd = new FakeCommand();
        vars[ids] = new CountlessCollection([]);
        query.SetCommand(cmd, vars);
        Assert.Equal("SELECT * FROM t", cmd.CommandText);

        cmd = new FakeCommand();
        vars[ids] = new CountlessCollection([9]);
        query.SetCommand(cmd, vars);
        Assert.Equal("SELECT * FROM t WHERE b IN (@Ids_1)", cmd.CommandText);

        cmd = new FakeCommand();
        vars[ids] = new List<int>();
        query.SetCommand(cmd, vars);
        Assert.Equal("SELECT * FROM t", cmd.CommandText);
    }

    [Fact]
    public void Parameter_ledger_registers_a_change_after_everything_settled() {
        var query = new QueryCommand("SELECT * FROM t WHERE a = ?@A AND b = ?@B");
        var ps = query.Parameters;
        int a = query.Mapper.GetIndex("@A"), b = query.Mapper.GetIndex("@B");
        Assert.True(ps.UpdateCache(a, InferedDbParamCache.ForceInfered));
        Assert.True(ps.UpdateCache(b, InferedDbParamCache.ForceInfered));
        ps.UpdateCachedIndexes();

        Span<bool> map = stackalloc bool[query.Mapper.Count];
        map[a] = true;
        Assert.False(ps.NeedToCache(map));
        Assert.True(ps.UpdateCache(a, InferedDbParamCache.Instance));
        Assert.True(ps.NeedToCache(map));
    }

    [Fact]
    public void Parameter_ledger_reindexes_past_the_stack_buffer_size() {
        var sb = new StringBuilder("SELECT * FROM t WHERE 1 = 1");
        for (int i = 0; i < 300; i++)
            sb.Append(" AND c = ?@V").Append(i);
        var query = new QueryCommand(sb.ToString());
        var ps = query.Parameters;
        Assert.True(ps.UpdateCache(query.Mapper.GetIndex("@V0"), InferedDbParamCache.ForceInfered));
        ps.UpdateCachedIndexes();
        Assert.Equal(299, ps.NbNonCached);
    }

    [Fact]
    public void Spread_saveuse_rejects_a_non_collection() {
        var h = HandlerWith(InferedDbParamCache.ForceInfered);
        object? value = new object();
        Assert.False(h.SaveUse(new FakeCommand(), ref value));
    }

    [Fact]
    public void Spread_use_leaves_a_cheaply_countable_generic_set_in_place() {
        var h = HandlerWith(InferedDbParamCache.ForceInfered);
        IDbCommand cmd = new FakeCommand();
        object? value = new HashSet<object> { 1, 2 };
        Assert.True(h.Use(cmd, ref value));
        Assert.IsType<HashSet<object>>(value);
        Assert.Equal(2, cmd.Parameters.Count);
    }

    [Fact]
    public void Spread_handle_counts_a_sequence_with_a_non_disposable_enumerator() {
        var h = HandlerWith(InferedDbParamCache.ForceInfered);
        var sb = new ValueStringBuilder(stackalloc char[64]);
        h.Handle(ref sb, new NonDisposableEnumerable([4, 5]));
        Assert.Equal("@Ids_1, @Ids_2", sb.ToStringAndDispose());
    }

    [Fact]
    public void Prebound_IDbCommand_road_takes_a_string_as_a_char_spread() {
        var query = new QueryCommand("SELECT * FROM t WHERE b IN (?@Ids_X)");
        int ids = query.Mapper.GetIndex("@Ids");
        IDbCommand cmd = new FakeCommand();
        var vars = new object?[query.Mapper.Count];
        vars[ids] = "ab";
        Assert.True(query.SetCommand(cmd, vars));
        Assert.Equal("SELECT * FROM t WHERE b IN (@Ids_1, @Ids_2)", cmd.CommandText);
        Assert.Equal(['a', 'b'], ((FakeCommand)cmd).BoundParameters.Select(p => p.Value).Cast<char>());
    }

    [Fact]
    public void Prebound_IDbCommand_road_takes_a_manual_count_without_binding() {
        var query = new QueryCommand("SELECT * FROM t WHERE b IN (?@Ids_X)");
        int ids = query.Mapper.GetIndex("@Ids");
        IDbCommand cmd = new FakeCommand();
        var vars = new object?[query.Mapper.Count];
        vars[ids] = 2;
        Assert.True(query.SetCommand(cmd, vars));
        Assert.Equal("SELECT * FROM t WHERE b IN (@Ids_1, @Ids_2)", cmd.CommandText);
        Assert.Equal(0, cmd.Parameters.Count);
    }

    record class FlagArgs {
        [ForBoolCond] public bool F;
    }

    [Fact]
    public void IDbCommand_accessor_road_reads_bool_condition_keys() {
        var query = new QueryCommand("SELECT * FROM t WHERE /*F*/x = 1");
        var usage = new bool[query.Mapper.Count];

        IDbCommand on = new FakeCommand();
        query.SetCommand(on, new FlagArgs { F = true }, usage);
        Assert.Equal("SELECT * FROM t WHERE x = 1", on.CommandText);

        IDbCommand off = new FakeCommand();
        query.SetCommand(off, new FlagArgs { F = false }, usage);
        Assert.Equal("SELECT * FROM t", off.CommandText);
    }

    [Fact]
    public void A_render_landing_on_the_stack_buffer_boundary_moves_the_estimate_off_it() {
        var pad = new string('a', 365);
        var query = new QueryCommand($"SELECT {pad} WHERE x = @V_N");
        var usage = new bool[query.Mapper.Count];

        var cmd = new FakeCommand();
        query.SetCommand((DbCommand)cmd, new { V = 7 }, usage);
        Assert.Equal($"SELECT {pad} WHERE x = 7", cmd.CommandText);
        Assert.Equal(384, cmd.CommandText.Length);

        var cmd2 = new FakeCommand();
        query.SetCommand((DbCommand)cmd2, new { V = 12 }, usage);
        Assert.Equal($"SELECT {pad} WHERE x = 12", cmd2.CommandText);
    }

    [Fact]
    public void A_required_handler_null_through_a_property_accessor_throws() {
        var query = new QueryCommand("SELECT * FROM t WHERE x = @V_N");
        var map = new bool[query.Mapper.Count];
        map[query.Mapper.GetIndex("@V")] = true;
        bool threw = false;
        try {
            query.QueryText.Parse(map, new TypeAccessor(new object(), (o, i) => true, (o, i) => null!));
        }
        catch (RequiredHandlerValueException) {
            threw = true;
        }
        Assert.True(threw);
    }

    record class GateArgs {
        [ForBoolCond] public bool A;
        [ForBoolCond] public bool B;
    }
    struct GateStruct {
        [ForBoolCond] public bool A;
        [ForBoolCond] public bool B;
    }
    record class ShowArgs { [ForBoolCond] public bool Show; }
    struct ShowStruct { [ForBoolCond] public bool Show; }

    static void ExpectObj<T>(QueryCommand q, T args, string expected) where T : notnull {
        var usage = new bool[q.Mapper.Count];
        var d = new FakeCommand();
        Assert.True(q.SetCommand((DbCommand)d, args, usage));
        Assert.Equal(expected, d.CommandText);
        IDbCommand i = new FakeCommand();
        Assert.True(q.SetCommand(i, args, usage));
        Assert.Equal(expected, i.CommandText);
    }

    [Fact]
    public void Object_roads_evaluate_an_and_gate() {
        var q = new QueryCommand("SELECT ID, /*A&B*/Secret FROM t");
        ExpectObj(q, new GateArgs { A = true, B = false }, "SELECT ID FROM t");
        ExpectObj(q, new GateArgs { A = true, B = true }, "SELECT ID, Secret FROM t");
        ExpectObj(q, new GateStruct { A = true, B = false }, "SELECT ID FROM t");
        ExpectObj(q, new GateStruct { A = true, B = true }, "SELECT ID, Secret FROM t");
    }

    [Fact]
    public void Object_roads_evaluate_an_or_gate() {
        var q = new QueryCommand("SELECT * FROM p /*A|B*/JOIN c ON c.i = p.i");
        ExpectObj(q, new GateArgs { A = false, B = true }, "SELECT * FROM p JOIN c ON c.i = p.i");
        ExpectObj(q, new GateArgs { A = false, B = false }, "SELECT * FROM p");
        ExpectObj(q, new GateStruct { A = false, B = true }, "SELECT * FROM p JOIN c ON c.i = p.i");
        ExpectObj(q, new GateStruct { A = false, B = false }, "SELECT * FROM p");
    }

    [Fact]
    public void Object_roads_trim_a_removed_projection_before_a_section() {
        var q = new QueryCommand("SELECT Id, Name, /*Show*/Price FROM t");
        ExpectObj(q, new ShowArgs { Show = false }, "SELECT Id, Name FROM t");
        ExpectObj(q, new ShowArgs { Show = true }, "SELECT Id, Name, Price FROM t");
        ExpectObj(q, new ShowStruct { Show = false }, "SELECT Id, Name FROM t");
        ExpectObj(q, new ShowStruct { Show = true }, "SELECT Id, Name, Price FROM t");
    }

    [Fact]
    public void A_null_parameter_object_renders_everything_off() {
        var q = new QueryCommand("SELECT * FROM t WHERE a > ?@Min AND b IN (?@Ids_X)");
        var usage = new bool[q.Mapper.Count];
        var d = new FakeCommand();
        Assert.True(q.SetCommand((DbCommand)d, (object?)null, usage));
        Assert.Equal("SELECT * FROM t", d.CommandText);
        Assert.Equal(0, d.Parameters.Count);
        IDbCommand i = new FakeCommand();
        Assert.True(q.SetCommand(i, (object?)null, usage));
        Assert.Equal("SELECT * FROM t", i.CommandText);
    }

    [Fact]
    public void A_long_template_renders_off_the_stack_on_every_accessor_road() {
        var pad = new string('a', 600);
        var q = new QueryCommand($"SELECT {pad} FROM t WHERE x > ?@Min");
        var usage = new bool[q.Mapper.Count];

        var nullRoad = new FakeCommand();
        Assert.True(q.SetCommand((DbCommand)nullRoad, (object?)null, usage));
        Assert.Equal($"SELECT {pad} FROM t", nullRoad.CommandText);

        var structRoad = new FakeCommand();
        Assert.True(q.SetCommand((DbCommand)structRoad, new Args { Min = 3 }, usage));
        Assert.Equal($"SELECT {pad} FROM t WHERE x > @Min", structRoad.CommandText);
    }

    [Fact]
    public async Task Accessor_cache_registration_is_safe_under_contention() {
        var query = new QueryCommand("SELECT * FROM t WHERE a > ?@Min");
        var handle = typeof(ClassArgs).TypeHandle.Value;
        TypeAccessorCache? fromBlocked = null;
        Task blocked;
        TypeAccessorCache winner;
        lock (QueryCommand.TypeAccessorSharedLock) {
            blocked = Task.Run(() => { fromBlocked = query.GetAccessorCache(handle, typeof(ClassArgs)); });
            Thread.Sleep(200);
            winner = query.GetAccessorCache(handle, typeof(ClassArgs));
        }
        await blocked;
        Assert.Same(winner, fromBlocked);
    }

    [Fact]
    public void A_required_handler_null_through_a_struct_accessor_throws() {
        var query = new QueryCommand("SELECT * FROM t WHERE x = @V_N");
        var map = new bool[query.Mapper.Count];
        map[query.Mapper.GetIndex("@V")] = true;
        int probe = 0;
        bool threw = false;
        try {
            query.QueryText.Parse(map, new TypeAccessor<int>(ref probe, (ref int o, int i) => true, (ref int o, int i) => null!));
        }
        catch (RequiredHandlerValueException) {
            threw = true;
        }
        Assert.True(threw);
    }

    sealed class TrackingEnumerator : IEnumerator, IDisposable {
        public bool Disposed;
        public object Current => 1;
        public bool MoveNext() => false;
        public void Reset() { }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void An_abandoned_peekable_wrapper_finalizes_its_enumerator() {
        var tracker = new TrackingEnumerator();
        Abandon(tracker);
        for (int i = 0; i < 10 && !tracker.Disposed; i++) {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Assert.True(tracker.Disposed);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Abandon(TrackingEnumerator e) => _ = new PeekableWrapper(1, e);
    }
}
