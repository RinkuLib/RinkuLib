using System.Data;
using System.Data.Common;
using System.Reflection;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;
using RinkuLib.Queries;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// The compiled parameter binder cache keyed by mapper instance, including its by-reference value-type path.
/// </summary>
public class AccessorCacheTests {
    public class Args {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    [Fact]
    public void The_generator_returns_a_fresh_accessor_each_time() {
        var mapper = Mapper.GetMapper(["@Id", "@Name"]);
        var first = ParameterAccessorGenerator.CreateDirect(typeof(Args), mapper);
        var second = ParameterAccessorGenerator.CreateDirect(typeof(Args), mapper);
        Assert.NotSame(first, second);

        var other = Mapper.GetMapper(["@Id", "@Name"]);
        Assert.NotSame(first, ParameterAccessorGenerator.CreateDirect(typeof(Args), other));
    }

    public struct StructArgs {
        public int Id { get; set; }
    }

    [Fact]
    public void A_value_type_gets_an_unboxed_direct_accessor() {
        var mapper = Mapper.GetOneKeyMapper("@Id");
        var accessor = Assert.IsType<DirectAccessor<StructArgs>>(
            ParameterAccessorGenerator.CreateDirect(typeof(StructArgs), mapper));
        using var command = new FakeCommand();
        Span<bool> usage = stackalloc bool[1];
        var value = new StructArgs { Id = 7 };

        accessor.InvokeTyped(ref value, command, [InferedDbParamCache.Instance], ref usage);

        Assert.True(usage[0]);
        Assert.Equal(7, command.Parameters[0]!.Value);
    }

    public class ContendedArgs {
        public int Id { get; set; }
    }

    [Fact]
    public async Task The_generator_has_no_retained_cross_call_state() {
        var mapper = Mapper.GetOneKeyMapper("@Id");
        DirectAccessor? fromContender = null;
        var contender = Task.Run(() => {
            fromContender = ParameterAccessorGenerator.CreateDirect(typeof(ContendedArgs), mapper);
        }, TestContext.Current.CancellationToken);
        var winner = ParameterAccessorGenerator.CreateDirect(typeof(ContendedArgs), mapper);
        await contender;
        Assert.NotSame(winner, fromContender);
    }

    [Fact]
    public void A_mapper_with_no_keys_emits_without_a_variable_char() {
        var cache = ParameterAccessorGenerator.CreateDirect(typeof(Args), Mapper.GetEmptyMapper());
        Assert.NotNull(cache);
    }

    [Fact]
    public void Direct_and_UseWith_generation_are_separate_products() {
        var mapper = Mapper.GetOneKeyMapper("@Id");
        var direct = ParameterAccessorGenerator.CreateDirect(typeof(Args), mapper);
        var useWith = ParameterAccessorGenerator.CreateUseWith(typeof(Args), mapper);

        Assert.NotSame(direct, useWith);
        Assert.NotSame(direct, ParameterAccessorGenerator.CreateDirect(typeof(Args), mapper));
        Assert.NotSame(useWith, ParameterAccessorGenerator.CreateUseWith(typeof(Args), mapper));
    }

    [Fact]
    public void Direct_then_UseWith_promotes_one_type_entry_and_keeps_both_accessors() {
        var query = new QueryCommand("SELECT * FROM Users WHERE Id = ?@Id");
        var handle = typeof(Args).TypeHandle.Value;

        var direct = query.GetDirectAccessor(handle, typeof(Args));
        var useWith = query.GetUseWithAccessor(handle, typeof(Args));

        Assert.Same(direct, query.GetDirectAccessor(handle, typeof(Args)));
        Assert.Same(useWith, query.GetUseWithAccessor(handle, typeof(Args)));
        Assert.Single(GetCacheEntries(query));
    }

    [Fact]
    public void UseWith_then_direct_promotes_one_type_entry_and_keeps_both_accessors() {
        var query = new QueryCommand("SELECT * FROM Users WHERE Id = ?@Id");
        var handle = typeof(Args).TypeHandle.Value;

        var useWith = query.GetUseWithAccessor(handle, typeof(Args));
        var direct = query.GetDirectAccessor(handle, typeof(Args));

        Assert.Same(useWith, query.GetUseWithAccessor(handle, typeof(Args)));
        Assert.Same(direct, query.GetDirectAccessor(handle, typeof(Args)));
        Assert.Single(GetCacheEntries(query));
    }

    [Fact]
    public async Task Concurrent_cross_path_access_promotes_without_losing_either_accessor() {
        var query = new QueryCommand("SELECT * FROM Users WHERE Id = ?@Id");
        var handle = typeof(Args).TypeHandle.Value;
        DirectAccessor? direct = null;
        UseWithAccessor? useWith = null;
        using var barrier = new Barrier(2);

        var directTask = Task.Run(() => {
            barrier.SignalAndWait();
            direct = query.GetDirectAccessor(handle, typeof(Args));
        }, TestContext.Current.CancellationToken);
        var useWithTask = Task.Run(() => {
            barrier.SignalAndWait();
            useWith = query.GetUseWithAccessor(handle, typeof(Args));
        }, TestContext.Current.CancellationToken);
        await Task.WhenAll(directTask, useWithTask);

        Assert.Same(direct, query.GetDirectAccessor(handle, typeof(Args)));
        Assert.Same(useWith, query.GetUseWithAccessor(handle, typeof(Args)));
        Assert.Single(GetCacheEntries(query));
    }

    [Fact]
    public void A_mapper_whose_first_key_is_empty_emits_without_a_variable_char() {
        var cache = ParameterAccessorGenerator.CreateDirect(typeof(Args), Mapper.GetMapper(["", "@Name"]));
        Assert.NotNull(cache);
    }

    [Fact]
    public void A_mapper_whose_first_key_is_named_binds_through_the_linear_delegate() {
        var mapper = Mapper.GetMapper(["@Id", "@Name"]);
        var cache = ParameterAccessorGenerator.CreateDirect(typeof(Args), mapper);
        using var command = new FakeCommand();
        Span<bool> usage = stackalloc bool[2];
        var values = cache.Invoke(new Args { Id = 3 }, command,
            [InferedDbParamCache.Instance, InferedDbParamCache.Instance], ref usage);

        Assert.True(usage[0]);
        Assert.False(usage[1]);
        Assert.Empty(values);
        Assert.Equal(3, command.Parameters[0]!.Value);
    }

    [Fact]
    public void A_direct_binder_replaces_a_reused_usage_map() {
        var mapper = Mapper.GetMapper(["@Id", "@Name"]);
        var cache = ParameterAccessorGenerator.CreateDirect(typeof(Args), mapper);
        using var command = new FakeCommand();
        Span<bool> usage = stackalloc bool[2];

        cache.Invoke(new Args { Id = 3, Name = "first" }, command,
            [InferedDbParamCache.Instance, InferedDbParamCache.Instance], ref usage);
        cache.Invoke(new Args { Id = 4 }, command,
            [InferedDbParamCache.Instance, InferedDbParamCache.Instance], ref usage);

        Assert.True(usage[0]);
        Assert.False(usage[1]);
    }

    private static (IntPtr Handle, object Accessor)[] GetCacheEntries(QueryCommand query)
        => ((ValueTuple<IntPtr, object>[])typeof(QueryCommand)
            .GetField("_accessors", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(query)!)!;

    class NoNestedType;

    class WrongNestedCtor {
        private sealed class DataRowDbColumn {
            public DataRowDbColumn(int unrelated) { }
        }
    }

    [Fact]
    public void The_column_factory_reports_what_it_could_not_find() {
        Assert.NotNull(WrappedBasicReader.GetPrivateDataRowCtor(typeof(DbDataReaderExtensions)));

        var missingType = Refusals.Raises(ErrorCodes.InternalInvariant,
            () => WrappedBasicReader.GetPrivateDataRowCtor(typeof(NoNestedType)));
        Assert.Contains("DataRowDbColumn", missingType.Message);

        var missingCtor = Refusals.Raises(ErrorCodes.InternalInvariant,
            () => WrappedBasicReader.GetPrivateDataRowCtor(typeof(WrongNestedCtor)));
        Assert.Contains("constructor", missingCtor.Message);
    }
}
