using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>
/// Naming attributes widen or narrow how a slot finds its column: <c>[Alt]</c> adds names,
/// <c>[AltUpTo]</c> and <c>[AltSkippingSegments]</c> reach across prefix segments, and the
/// look-anywhere family changes the reading order a slot may use.
/// </summary>
public class AltNamesAndScopeTests {
    [Fact]
    public void Alt_maps_a_column_with_a_different_name() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Label", typeof(string), false)];
        var item = Rows.ParseOne<Renamed>(cols, 1, "shown");
        Assert.Equal(1, item.Id);
        Assert.Equal("shown", item.Name);
    }

    [Fact]
    public void Alt_still_accepts_the_original_name() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        var item = Rows.ParseOne<Renamed>(cols, 1, "shown");
        Assert.Equal("shown", item.Name);
    }

    [Fact]
    public void AltUpTo_matches_the_name_within_its_segment_scope() {
        ColumnInfo[] cols = [
            new("First", typeof(int), true),
            new("NotTooDeep", typeof(int), true),
            new("SuperDeep", typeof(int), true),
            new("TwoSemiDeep", typeof(int), true),
        ];
        var layered = Rows.ParseOne<TierOne>(cols, 1, 2, 3, 4);
        Assert.Equal(1, layered.First);
        Assert.Equal(2, layered.Two.Second);
        Assert.Equal(3, layered.Two.Three.Third);
        Assert.Equal(4, layered.Two.Three.Deep);
    }

    [Fact]
    public void AltUpTo_out_of_scope_fails_to_build_the_parser() {
        ColumnInfo[] cols = [
            new("First", typeof(int), true),
            new("NotTooDeep", typeof(int), true),
            new("SuperDeep", typeof(int), true),
            new("SemiDeep", typeof(int), true),
        ];
        Assert.ThrowsAny<Exception>(() => {
            var localCols = cols;
            TypeParser.GetTypeParser<TierOne>(ref localCols);
        });
    }

    [Fact]
    public void Free_complex_member_may_skip_columns_between_its_own() {
        ColumnInfo[] cols = [
            new("Id", typeof(int), false),
            new("SubA", typeof(int), false),
            new("Gap", typeof(int), false),
            new("SubB", typeof(int), true),
        ];
        var outer = Rows.ParseOne<ScopeOuterFree>(cols, 1, 10, 99, 20);
        Assert.Equal(10, outer.Sub.A);
        Assert.Equal(20, outer.Sub.B);
    }

    [Fact]
    public void CanNotLookAnywhereSubtree_forces_sequential_reads() {
        ColumnInfo[] cols = [
            new("Id", typeof(int), false),
            new("SubA", typeof(int), false),
            new("Gap", typeof(int), false),
            new("SubB", typeof(int), true),
        ];
        var outer = Rows.ParseOne<ScopeOuterSequential>(cols, 1, 10, 99, 20);
        Assert.Equal(10, outer.Sub.A);
        Assert.Null(outer.Sub.B);
    }

    [Fact]
    public void CanLookAnywhere_frees_only_the_first_column_of_the_slot() {
        ColumnInfo[] cols = [
            new("X", typeof(int), false),
            new("Key", typeof(int), false),
            new("Junk", typeof(int), false),
            new("DataA", typeof(int), false),
            new("Gap", typeof(int), false),
            new("DataB", typeof(int), true),
        ];
        var (x, holder) = Rows.ParseOne<(int, ScopeHolderSlot)>(cols, 1, 2, 99, 10, 88, 20);
        Assert.Equal(1, x);
        Assert.Equal(2, holder.Key);
        Assert.Equal(10, holder.Data.A);
        Assert.Null(holder.Data.B);
    }

    [Fact]
    public void CanLookAnywhereSubtree_frees_every_column_of_the_slot() {
        ColumnInfo[] cols = [
            new("X", typeof(int), false),
            new("Key", typeof(int), false),
            new("Junk", typeof(int), false),
            new("DataA", typeof(int), false),
            new("Gap", typeof(int), false),
            new("DataB", typeof(int), true),
        ];
        var (_, holder) = Rows.ParseOne<(int, ScopeHolderSubtree)>(cols, 1, 2, 99, 10, 88, 20);
        Assert.Equal(10, holder.Data.A);
        Assert.Equal(20, holder.Data.B);
    }

    [Fact]
    public void NoName_slot_matches_without_consuming_a_name() {
        ColumnInfo[] cols = [new("Whatever", typeof(int), false)];
        var wrapped = Rows.ParseOne<NamelessInt>(cols, 5);
        Assert.Equal(5, wrapped.Value);
    }

    [Fact]
    public void AltSkippingSegments_matches_across_one_nesting_prefix() {
        ColumnInfo[] cols = [new("First", typeof(int), false), new("Flat", typeof(int), false)];
        var outer = Rows.ParseOne<SkipOuter>(cols, 1, 2);
        Assert.Equal(1, outer.First);
        Assert.Equal(2, outer.Two.Second);
    }

    [Fact]
    public void MayReuseCol_lets_two_slots_share_one_column() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        var pair = Rows.ParseOne<ReusingPair>(cols, 7, "x");
        Assert.Equal(7, pair.Id);
        Assert.Equal(7, pair.IdAgain);
    }
}

public record Renamed(int Id, [Alt("Label")] string Name);
public record TierOne(int First, TierTwo Two);
public record TierTwo([AltUpTo("NotTooDeep", "Two")] int Second, TierThree Three) : IDbReadable;
public record TierThree([AltUpTo("SuperDeep", "Two")] int Third, [AltUpTo("SemiDeep", "Three")] int Deep) : IDbReadable;
public record ScopeInner(int A, int? B = null) : IDbReadable;
public record ScopeOuterFree(int Id, ScopeInner Sub);
public record ScopeOuterSequential(int Id, [CanNotLookAnywhereSubtree] ScopeInner Sub);
public record ScopeHolderSlot(int Key, [CanLookAnywhere] ScopeInner Data) : IDbReadable;
public record ScopeHolderSubtree(int Key, [CanLookAnywhereSubtree] ScopeInner Data) : IDbReadable;
public record struct NamelessInt([NoName] int Value);
public record SkipOuter(int First, SkipInner Two);
public record SkipInner([AltSkippingSegments("Flat", 2)] int Second) : IDbReadable;
public record ReusingPair(int Id, [MayReuseCol][Alt("Id")] int IdAgain);
