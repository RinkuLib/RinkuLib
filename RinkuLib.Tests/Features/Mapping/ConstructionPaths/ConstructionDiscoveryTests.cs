using Rinku.Mapping;
using RinkuLib.Tests.Infrastructure;
using Rinku.Internal;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>
/// How discovery walks a type's constructors: which one becomes the parameterless fallback, which are
/// rejected outright, and how a ctor-only registration picks among several.
/// </summary>
public class ConstructionDiscoveryTests {
    public class NotMappable {
        public int A;
        public NotMappable(int a) => A = a;
    }

    public class ParameterlessFirst : IDbReadable {
        public int A { get; set; }
        public ParameterlessFirst() { }
        public ParameterlessFirst(NotMappable rejected) => A = -1;
    }

    public class RejectedFirst : IDbReadable {
        public int A { get; set; }
        public RejectedFirst(NotMappable rejected) => A = -1;
        public RejectedFirst() { }
    }

    [Fact]
    public void A_parameterless_constructor_is_kept_whatever_order_it_is_declared_in() {
        ColumnInfo[] cols = [new("A", typeof(int), false)];
        Assert.Equal(5, Rows.ParseOne<ParameterlessFirst>(cols, 5).A);
        Assert.Equal(6, Rows.ParseOne<RejectedFirst>(cols, 6).A);
    }

    public class TwoWays : IDbReadable {
        public int Taken;
        public TwoWays(int a) => Taken = a;
        public TwoWays(int a, int b) => Taken = a + b;
    }

    [Fact]
    public void A_ctor_registration_keeps_the_first_parameterized_candidate() {
        TypeParsingInfo.AddOrSet<TwoWays>(CtorTypeInfo.Instance);
        ColumnInfo[] cols = [new("A", typeof(int), false), new("B", typeof(int), false)];
        var built = Rows.ParseOne<TwoWays>(cols, 4, 5);
        Assert.Equal(4, built.Taken);
    }

    public class PinnedWay : IDbReadable {
        public int Taken;
        public PinnedWay(int a) => Taken = -1;
        [DbConstructor]
        public PinnedWay(int a, int b) => Taken = a + b;
    }

    [Fact]
    public void A_pinned_ctor_wins_over_an_earlier_candidate() {
        TypeParsingInfo.AddOrSet<PinnedWay>(CtorTypeInfo.Instance);
        ColumnInfo[] cols = [new("A", typeof(int), false), new("B", typeof(int), false)];
        Assert.Equal(9, Rows.ParseOne<PinnedWay>(cols, 4, 5).Taken);
    }

    public record Reuse(int Shared, [Alt("Shared")][MayReuseCol] int Again) : IDbReadable;

    public record ReuseWithoutAlt(int Shared, [MayReuseCol] int Again) : IDbReadable;

    public record ReuseBeforeOwner([Alt("Shared")][MayReuseCol] int Copy, int Shared) : IDbReadable;

    public record NestedReuseBeforeTail(
        [MayReuseCol] NestedReuseValues Child,
        [NoName] Dictionary<string, object> Tail) : IDbReadable;

    public record NestedReuseValues(int A, int B) : IDbReadable;

    public record SequentialReuse(
        int Shared,
        [Alt("Shared")][CanNotLookAnywhere][MayReuseCol] int Again) : IDbReadable;

    static readonly ColumnInfo[] SharedOnly = [new("Shared", typeof(int), false)];

    [Fact]
    public void A_slot_allowed_to_reuse_takes_the_column_already_read() {
        var parsed = Rows.ParseOne<Reuse>(SharedOnly, 7);
        Assert.Equal(7, parsed.Shared);
        Assert.Equal(7, parsed.Again);
    }

    [Fact]
    public void A_reusable_slot_does_not_consume_the_column_for_a_later_owner() {
        var parsed = Rows.ParseOne<ReuseBeforeOwner>(SharedOnly, 7);
        Assert.Equal(7, parsed.Copy);
        Assert.Equal(7, parsed.Shared);
    }

    [Fact]
    public void Slot_scoped_reuse_leaves_only_the_nested_slots_first_column_unconsumed() {
        ColumnInfo[] columns = [new("ChildA", typeof(int), false), new("ChildB", typeof(int), false)];
        var parsed = Rows.ParseOne<NestedReuseBeforeTail>(columns, 7, 8);
        Assert.Equal(7, parsed.Child.A);
        Assert.Equal(8, parsed.Child.B);
        Assert.Equal(new Dictionary<string, object> { ["ChildA"] = 7 }, parsed.Tail);
    }

    [Fact]
    public void Reuse_still_needs_a_name_that_matches_the_column() {
        Refusals.NoParserFor<ReuseWithoutAlt>(() => Rows.ParseOne<ReuseWithoutAlt>(SharedOnly, 7));
    }

    [Fact]
    public void Asking_a_slot_to_be_sequential_and_to_reuse_at_once_is_refused() {
        Refusals.NoParserFor<SequentialReuse>(() => Rows.ParseOne<SequentialReuse>(SharedOnly, 7));
    }
}
