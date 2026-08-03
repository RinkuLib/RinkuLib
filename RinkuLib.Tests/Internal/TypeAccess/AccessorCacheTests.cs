using System.Data;
using System.Data.Common;
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
    public void The_same_mapper_asked_twice_reuses_its_compiled_binder() {
        var mapper = Mapper.GetMapper(["@Id", "@Name"]);
        var first = TypeAccessorCacher<Args>.GetOrGenerate(mapper);
        var second = TypeAccessorCacher<Args>.GetOrGenerate(mapper);
        Assert.Same(first, second);

        var other = Mapper.GetMapper(["@Id", "@Name"]);
        Assert.NotSame(first, TypeAccessorCacher<Args>.GetOrGenerate(other));
    }

    public struct StructArgs {
        public int Id { get; set; }
    }

    [Fact]
    public void A_value_type_gets_a_struct_cache_and_reuses_it_too() {
        var mapper = Mapper.GetOneKeyMapper("@Id");
        var first = TypeAccessorCacher<StructArgs>.GetOrGenerate(mapper);
        Assert.IsType<StructTypeAccessorCache<StructArgs>>(first);
        Assert.Same(first, TypeAccessorCacher<StructArgs>.GetOrGenerate(mapper));
    }

    public class ContendedArgs {
        public int Id { get; set; }
    }

    [Fact]
    public async Task Two_threads_racing_on_the_same_mapper_share_one_compilation() {
        var mapper = Mapper.GetOneKeyMapper("@Id");
        using var contenderStarted = new ManualResetEventSlim();
        TypeAccessorCache? fromContender = null;
        Task contender;
        TypeAccessorCache winner;
        lock (TypeAccessorCacher<ContendedArgs>.SharedLock) {
            contender = Task.Run(() => {
                contenderStarted.Set();
                fromContender = TypeAccessorCacher<ContendedArgs>.GetOrGenerate(mapper);
            }, TestContext.Current.CancellationToken);
            contenderStarted.Wait(TestContext.Current.CancellationToken);
            Thread.Sleep(100);
            winner = TypeAccessorCacher<ContendedArgs>.GetOrGenerate(mapper);
        }
        await contender;
        Assert.Same(winner, fromContender);
    }

    [Fact]
    public void A_mapper_with_no_keys_emits_without_a_variable_char() {
        var cache = TypeAccessorCacher<Args>.GetOrGenerate(Mapper.GetEmptyMapper());
        Assert.NotNull(cache);
    }

    [Fact]
    public void The_UseWith_cache_is_independent_from_the_direct_binder_cache() {
        var mapper = Mapper.GetOneKeyMapper("@Id");
        var direct = TypeAccessorCacher<Args>.GetOrGenerate(mapper);
        var useWith = TypeAccessorCacher<Args>.GetOrGenerateUseWith(mapper);

        Assert.NotSame(direct, useWith);
        Assert.Same(direct, TypeAccessorCacher<Args>.GetOrGenerate(mapper));
        Assert.Same(useWith, TypeAccessorCacher<Args>.GetOrGenerateUseWith(mapper));
    }

    [Fact]
    public void A_mapper_whose_first_key_is_empty_emits_without_a_variable_char() {
        var cache = TypeAccessorCacher<Args>.GetOrGenerate(Mapper.GetMapper(["", "@Name"]));
        Assert.NotNull(cache);
    }

    [Fact]
    public void A_mapper_whose_first_key_is_named_binds_through_the_linear_delegate() {
        var mapper = Mapper.GetMapper(["@Id", "@Name"]);
        var cache = TypeAccessorCacher<Args>.GetOrGenerate(mapper, handlersStart: mapper.Count, boolCondStart: mapper.Count);
        using var command = new FakeCommand();
        Span<bool> usage = stackalloc bool[2];
        var values = cache.Bind(new Args { Id = 3 }, command,
            [InferedDbParamCache.Instance, InferedDbParamCache.Instance], ref usage);

        Assert.True(usage[0]);
        Assert.False(usage[1]);
        Assert.Empty(values);
        Assert.Equal(3, command.Parameters[0]!.Value);
    }

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
