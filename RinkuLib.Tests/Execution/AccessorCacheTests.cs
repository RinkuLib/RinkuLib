using System.Data;
using System.Data.Common;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// The compiled accessor cache keyed by mapper instance, the key shapes its emission branches on, and the
/// reflection lookup that reaches the framework's private column type.
/// </summary>
public class AccessorCacheTests {
    public class Args {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    [Fact]
    public void The_same_mapper_asked_twice_reuses_its_compiled_readers() {
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
            });
            contenderStarted.Wait();
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
    public void A_mapper_whose_first_key_is_empty_emits_without_a_variable_char() {
        var cache = TypeAccessorCacher<Args>.GetOrGenerate(Mapper.GetMapper(["", "@Name"]));
        Assert.NotNull(cache);
    }

    [Fact]
    public void A_mapper_whose_first_key_is_named_takes_its_leading_char() {
        var cache = TypeAccessorCacher<Args>.GetOrGenerate(Mapper.GetMapper(["@Id", "@Name"]));
        Assert.True(cache.GetUsage(new Args { Id = 3 }, 0));
        Assert.Equal(3, cache.GetValue(new Args { Id = 3 }, 0));
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
