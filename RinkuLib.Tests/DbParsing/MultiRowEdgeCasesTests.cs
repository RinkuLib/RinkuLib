using System.Data.Common;
using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.DbParsing;

/// <summary>
/// Edge cases and outside-in extensibility of the multi-row road: custom group boundaries written entirely
/// against the public emit surface, method boundaries with several negotiated inputs and alternates, the
/// grouping extremes (one row, every row its own group, one group of every row), composite-key negatives, and
/// the collapse path. Every test checks the whole materialised graph.
/// </summary>
public class MultiRowEdgeCasesTests {

    static MultiRowEdgeCasesTests() {
        ((ICanUpdateGroupKey)TypeParsingInfo.ForceGet(typeof(StepHolder))).GroupKey = new StepMaker("Value", 10);
        ((ICanUpdateGroupKey)TypeParsingInfo.ForceGet(typeof(ParityHolder))).GroupKey = new ParityMaker("Seed");
        TypeParsingInfo.AddOrSet(typeof(Average), new MultiRowTypeParsingInfo(
            typeof(Averager).GetConstructor(Type.EmptyTypes)!,
            typeof(Averager).GetMethod(nameof(Averager.Add), [typeof(double)]),
            typeof(Averager).GetMethod(nameof(Averager.Finish))));
    }

    // --- aggregation: a custom accumulator folds rows into a value that is not a collection --------------

    public sealed class Averager {
        private double Sum;
        private int Count;
        public void Add(double value) {
            Sum += value;
            Count++;
        }
        public Average Finish() => new(Count == 0 ? 0 : Sum / Count, Count);
    }
    public readonly record struct Average(double Mean, int Count);

    [Fact]
    public void An_aggregate_folds_every_row_into_one_value() {
        ColumnInfo[] cols = [new("Amount", typeof(double), false)];
        var parser = TypeParser.GetTypeParser<Average>(ref cols);
        using var reader = Rows.Reader(cols, [10.0], [20.0], [30.0]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(new Average(20, 3), result);
    }

    [Fact]
    public void An_aggregate_of_no_rows_is_its_empty_fold() {
        ColumnInfo[] cols = [new("Amount", typeof(double), false)];
        var parser = TypeParser.GetTypeParser<Average>(ref cols);
        Assert.Equal(new Average(0, 0), parser.Default());
    }

    public sealed record Stats(int GroupId, Average Amount) : IDbReadable;

    [Fact]
    public void An_aggregate_folds_per_inferred_group() {
        ColumnInfo[] cols = [new("GroupId", typeof(int), false), new("Amount", typeof(double), false)];
        var parser = TypeParser.GetTypeParser<List<Stats>>(ref cols);
        using var reader = Rows.Reader(cols, [1, 10.0], [1, 20.0], [2, 30.0], [2, 50.0]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, new Average(15, 2)), (result[0].GroupId, result[0].Amount));
        Assert.Equal((2, new Average(40, 2)), (result[1].GroupId, result[1].Amount));
    }

    // --- outside-in extensibility: a boundary that reads the raw reader ----------------------------------

    public sealed record StepHolder(List<int> Value) : IDbReadable;

    /// <summary>A boundary defined outside the library that groups by <c>column / step</c>, read straight off the reader.</summary>
    private sealed class StepBoundary(int column, int step, IBoundaryField bucket) : GroupingBoundary {
        public override bool CanChange => true;
        public override bool Captures => true;
        private void EmitBucket(Generator g) {
            g.Emit(OpCodes.Ldarg_1);
            g.Emit(OpCodes.Ldc_I4, column);
            g.Emit(OpCodes.Callvirt, typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetInt32), [typeof(int)])!);
            g.Emit(OpCodes.Ldc_I4, step);
            g.Emit(OpCodes.Div);
        }
        public override void EmitCapture(Generator g) {
            bucket.EmitThis(g);
            EmitBucket(g);
            bucket.EmitStore(g);
        }
        public override void EmitCompare(Generator g, Label changed) {
            bucket.EmitLoad(g);
            EmitBucket(g);
            g.Emit(OpCodes.Ceq);
            g.Emit(OpCodes.Brfalse, changed);
        }
    }

    private sealed class StepMaker(string column, int step) : IGroupingKeyMaker {
        public GroupingBoundary MakeBoundary(Type spanningType, ColumnInfo[] columns, ColModifier colModifier, IBoundaryBuild build) {
            int index = Array.FindIndex(columns, c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase));
            return new StepBoundary(index, step, build.Field(typeof(int)));
        }
    }

    [Fact]
    public void A_boundary_defined_outside_the_library_groups_by_reading_the_reader() {
        ColumnInfo[] cols = [new("Value", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<StepHolder>>(ref cols);
        using var reader = Rows.Reader(cols, [3], [7], [12], [25], [28]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(3, result.Count);
        Assert.Equal([3, 7], result[0].Value);
        Assert.Equal([12], result[1].Value);
        Assert.Equal([25, 28], result[2].Value);
    }

    // --- outside-in extensibility: a boundary that negotiates its own reader ------------------------------

    public sealed record ParityHolder(int Seed, List<string> Items) : IDbReadable;

    /// <summary>An outside boundary that groups while a negotiated column stays the same parity, using the build handles.</summary>
    private sealed class ParityBoundary(IBoundaryReader source, IBoundaryField parity) : GroupingBoundary {
        public override bool CanChange => true;
        public override bool Captures => true;
        private void EmitParity(Generator g) {
            source.EmitRead(g);
            g.Emit(OpCodes.Ldc_I4_2);
            g.Emit(OpCodes.Rem);
        }
        public override void EmitCapture(Generator g) {
            parity.EmitThis(g);
            EmitParity(g);
            parity.EmitStore(g);
        }
        public override void EmitCompare(Generator g, Label changed) {
            parity.EmitLoad(g);
            EmitParity(g);
            g.Emit(OpCodes.Ceq);
            g.Emit(OpCodes.Brfalse, changed);
        }
    }

    private sealed class ParityMaker(string column) : IGroupingKeyMaker {
        public GroupingBoundary MakeBoundary(Type spanningType, ColumnInfo[] columns, ColModifier colModifier, IBoundaryBuild build) {
            colModifier.Flags |= UsageFlags.CanReuse | UsageFlags.RemoveSequentialRead;
            var usage = new ColumnUsage(new bool[columns.Length]);
            var plan = TypeParsingInfo.ForceGet(typeof(int)).TryGetParser(typeof(int), new([], 0),
                ParamInfo.Create(typeof(int), column, []), columns, colModifier, ref usage)!;
            return new ParityBoundary(build.Reader(plan, typeof(int)), build.Field(typeof(int)));
        }
    }

    [Fact]
    public void A_boundary_defined_outside_the_library_negotiates_its_own_reader() {
        ColumnInfo[] cols = [new("Seed", typeof(int), false), new("Items", typeof(string), false)];
        var parser = TypeParser.GetTypeParser<List<ParityHolder>>(ref cols);
        using var reader = Rows.Reader(cols, [2, "a"], [4, "b"], [5, "c"], [7, "d"], [8, "e"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(3, result.Count);
        Assert.Equal(2, result[0].Seed);
        Assert.Equal(["a", "b"], result[0].Items);
        Assert.Equal(5, result[1].Seed);
        Assert.Equal(["c", "d"], result[1].Items);
        Assert.Equal(8, result[2].Seed);
        Assert.Equal(["e"], result[2].Items);
    }

    // --- method boundary edge cases ----------------------------------------------------------------------

    public sealed record TwoKeyChild([InvalidOnNull] int Id, string Value) : IDbReadable;
    public sealed record ManhattanBucket(List<TwoKeyChild> Children) : IDbReadable {
        [GroupKey]
        public static (bool Same, (int, int) Next) SameCell((int X, int Y) stored, int gx, int gy)
            => ((gx, gy) == stored, (gx, gy));
    }

    [Fact]
    public void A_method_boundary_reads_several_negotiated_columns() {
        ColumnInfo[] cols = [
            new("Gx", typeof(int), false),
            new("Gy", typeof(int), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<ManhattanBucket>>(ref cols);
        using var reader = Rows.Reader(cols,
            [1, 1, 10, "a"],
            [1, 1, 11, "b"],
            [1, 2, 12, "c"],
            [2, 2, 13, "d"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(3, result.Count);
        Assert.Equal([new TwoKeyChild(10, "a"), new TwoKeyChild(11, "b")], result[0].Children);
        Assert.Equal([new TwoKeyChild(12, "c")], result[1].Children);
        Assert.Equal([new TwoKeyChild(13, "d")], result[2].Children);
    }

    public sealed record AltMethodBucket(List<TwoKeyChild> Children) : IDbReadable {
        [GroupKey]
        public static (bool Same, int Next) SameKey(int stored, [Alt("Grp")] int bucket) => (bucket == stored, bucket);
    }

    [Fact]
    public void A_method_boundary_parameter_resolves_through_its_alternate_name() {
        ColumnInfo[] cols = [
            new("Grp", typeof(int), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<AltMethodBucket>>(ref cols);
        using var reader = Rows.Reader(cols, [1, 10, "a"], [1, 11, "b"], [2, 12, "c"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal([new TwoKeyChild(10, "a"), new TwoKeyChild(11, "b")], result[0].Children);
        Assert.Equal([new TwoKeyChild(12, "c")], result[1].Children);
    }

    public sealed record MGrand([InvalidOnNull] int Id, string Data) : IDbReadable;
    public sealed record MethodChild(List<MGrand> Grands) : IDbReadable {
        [GroupKey]
        public static (bool Same, int Next) SameChild(int stored, int childKey) => (childKey == stored, childKey);
    }
    public sealed record MethodNestParent([property: GroupKey] int Id, string Name, List<MethodChild> Children) : IDbReadable;

    [Fact]
    public void A_method_keyed_sub_level_folds_under_an_equality_keyed_parent() {
        ColumnInfo[] cols = [
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
            new("ChildrenChildKey", typeof(int), false),
            new("ChildrenGrandsId", typeof(int), true),
            new("ChildrenGrandsData", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<MethodNestParent>>(ref cols);
        using var reader = Rows.Reader(cols,
            [1, "P1", 10, 100, "g100"],
            [1, "P1", 10, 101, "g101"],
            [1, "P1", 11, 110, "g110"],
            [2, "P2", 20, 200, "g200"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, "P1"), (result[0].Id, result[0].Name));
        Assert.Equal(2, result[0].Children.Count);
        Assert.Equal([new MGrand(100, "g100"), new MGrand(101, "g101")], result[0].Children[0].Grands);
        Assert.Equal([new MGrand(110, "g110")], result[0].Children[1].Grands);
        Assert.Equal((2, "P2"), (result[1].Id, result[1].Name));
        Assert.Single(result[1].Children);
        Assert.Equal([new MGrand(200, "g200")], result[1].Children[0].Grands);
    }

    [Fact]
    public void A_method_keyed_sub_level_stays_empty_for_a_childless_parent() {
        ColumnInfo[] cols = [
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
            new("ChildrenChildKey", typeof(int), true),
            new("ChildrenGrandsId", typeof(int), true),
            new("ChildrenGrandsData", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<MethodNestParent>>(ref cols);
        using var reader = Rows.Reader(cols,
            [1, "P1", 10, 100, "g100"],
            [2, "P2", DBNull.Value, DBNull.Value, DBNull.Value],
            [3, "P3", 30, 300, "g300"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(3, result.Count);
        Assert.Equal([new MGrand(100, "g100")], result[0].Children[0].Grands);
        Assert.Equal((2, "P2"), (result[1].Id, result[1].Name));
        Assert.Empty(result[1].Children);
        Assert.Equal([new MGrand(300, "g300")], result[2].Children[0].Grands);
    }

    // --- grouping extremes -------------------------------------------------------------------------------

    private static ColumnInfo[] ParentCols() => [
        new("Id", typeof(int), false),
        new("Name", typeof(string), false),
        new("ChildrenId", typeof(int), true),
        new("ChildrenValue", typeof(string), true),
    ];

    [Fact]
    public void A_single_row_yields_one_group_of_one_child() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<List<MultiRowTests.Parent>>(ref cols);
        using var reader = Rows.Reader(cols, [1, "P1", 10, "c10"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Single(result);
        Assert.Equal((1, "P1"), (result[0].Id, result[0].Name));
        Assert.Equal([new MultiRowTests.Child(10, "c10")], result[0].Children);
    }

    [Fact]
    public void A_key_that_changes_every_row_yields_a_singleton_group_each() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<List<MultiRowTests.Parent>>(ref cols);
        using var reader = Rows.Reader(cols, [1, "P1", 10, "c10"], [2, "P2", 20, "c20"], [3, "P3", 30, "c30"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(3, result.Count);
        Assert.Equal([new MultiRowTests.Child(10, "c10")], result[0].Children);
        Assert.Equal([new MultiRowTests.Child(20, "c20")], result[1].Children);
        Assert.Equal([new MultiRowTests.Child(30, "c30")], result[2].Children);
        Assert.Equal((3, "P3"), (result[2].Id, result[2].Name));
    }

    [Fact]
    public void A_key_that_never_changes_folds_every_row_into_one_group() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<List<MultiRowTests.Parent>>(ref cols);
        using var reader = Rows.Reader(cols, [1, "P1", 10, "c10"], [1, "P1", 11, "c11"], [1, "P1", 12, "c12"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Single(result);
        Assert.Equal([new MultiRowTests.Child(10, "c10"), new MultiRowTests.Child(11, "c11"), new MultiRowTests.Child(12, "c12")], result[0].Children);
    }

    [Fact]
    public void A_childless_parent_from_a_left_join_keeps_an_empty_collection() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<List<MultiRowTests.Parent>>(ref cols);
        using var reader = Rows.Reader(cols,
            [1, "P1", 10, "c10"],
            [2, "P2", DBNull.Value, DBNull.Value],
            [3, "P3", 30, "c30"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(3, result.Count);
        Assert.Equal([new MultiRowTests.Child(10, "c10")], result[0].Children);
        Assert.Equal((2, "P2"), (result[1].Id, result[1].Name));
        Assert.Empty(result[1].Children);
        Assert.Equal([new MultiRowTests.Child(30, "c30")], result[2].Children);
    }

    // --- composite key: both parts must match ------------------------------------------------------------

    [Fact]
    public void A_composite_key_splits_when_only_the_second_part_changes() {
        ColumnInfo[] cols = [
            new("RegionId", typeof(int), false),
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<MultiRowTests.RegionParent>>(ref cols);
        using var reader = Rows.Reader(cols,
            [1, 1, "A", 10, "c10"],
            [1, 1, "A", 11, "c11"],
            [1, 2, "B", 20, "c20"],
            [2, 2, "C", 30, "c30"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(3, result.Count);
        Assert.Equal((1, 1, "A"), (result[0].RegionId, result[0].Id, result[0].Name));
        Assert.Equal([new MultiRowTests.Child(10, "c10"), new MultiRowTests.Child(11, "c11")], result[0].Children);
        Assert.Equal((1, 2, "B"), (result[1].RegionId, result[1].Id, result[1].Name));
        Assert.Equal([new MultiRowTests.Child(20, "c20")], result[1].Children);
        Assert.Equal((2, 2, "C"), (result[2].RegionId, result[2].Id, result[2].Name));
        Assert.Equal([new MultiRowTests.Child(30, "c30")], result[2].Children);
    }

    // --- a key resolves the throw and can sit anywhere ---------------------------------------------------

    public sealed record Ledger(List<int> Lines, [property: GroupKey] int AccountId) : IDbReadable;

    [Fact]
    public void A_key_after_the_collection_resolves_the_throw_and_groups() {
        ColumnInfo[] cols = [new("Lines", typeof(int), false), new("AccountId", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<Ledger>>(ref cols);
        using var reader = Rows.Reader(cols, [10, 1], [11, 1], [20, 2]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].AccountId);
        Assert.Equal([10, 11], result[0].Lines);
        Assert.Equal(2, result[1].AccountId);
        Assert.Equal([20], result[1].Lines);
    }

    private static ColumnInfo[] ArtistAlbumCols() => [
        new("Id", typeof(int), false), new("Name", typeof(string), false),
        new("AlbumsId", typeof(int), true), new("AlbumsTitle", typeof(string), true)];

    public sealed record PlainAlbum(int Id, string Title) : IDbReadable;
    public sealed record PlainArtist([property: GroupKey] int Id, string Name, List<PlainAlbum> Albums) : IDbReadable;

    [Fact]
    public void A_null_in_a_required_element_column_throws() {
        var cols = ArtistAlbumCols();
        var parser = TypeParser.GetTypeParser<List<PlainArtist>>(ref cols);
        using var reader = Rows.Reader(cols, [3, "Bjork", DBNull.Value, DBNull.Value]);
        reader.Read();
        Refusals.Raises(ErrorCodes.NullNotAllowed, () => parser.Parse(reader));
    }

    public sealed record CollapsingAlbum([InvalidOnNull] int Id, string Title) : IDbReadable;
    public sealed record CollapsingArtist([property: GroupKey] int Id, string Name, List<CollapsingAlbum> Albums) : IDbReadable;

    [Fact]
    public void An_element_that_collapses_on_null_is_skipped() {
        var cols = ArtistAlbumCols();
        var parser = TypeParser.GetTypeParser<List<CollapsingArtist>>(ref cols);
        using var reader = Rows.Reader(cols, [3, "Bjork", DBNull.Value, DBNull.Value]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Single(result);
        Assert.Empty(result[0].Albums);
    }

    public sealed record NullableAlbum(int? Id, string? Title) : IDbReadable;
    public sealed record NullableArtist([property: GroupKey] int Id, string Name, [KeepNullElements] List<NullableAlbum?> Albums) : IDbReadable;

    [Fact]
    public void An_all_null_element_that_still_builds_is_kept() {
        var cols = ArtistAlbumCols();
        var parser = TypeParser.GetTypeParser<List<NullableArtist>>(ref cols);
        using var reader = Rows.Reader(cols, [3, "Bjork", DBNull.Value, DBNull.Value]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Single(result);
        Assert.Equal([new NullableAlbum(null, null)], result[0].Albums);
    }

    public sealed record KeptArtist([property: GroupKey] int Id, string Name, List<NullableAlbum> Albums) : IDbReadable;

    [Fact]
    public void An_all_null_object_element_is_kept_without_the_attribute() {
        var cols = ArtistAlbumCols();
        var parser = TypeParser.GetTypeParser<List<KeptArtist>>(ref cols);
        using var reader = Rows.Reader(cols, [3, "Bjork", DBNull.Value, DBNull.Value]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Single(result);
        Assert.Equal([new NullableAlbum(null, null)], result[0].Albums);
    }

    public sealed class KeyedWidget : IDbReadable {
        [GroupKey] public int Key { get; set; }
        public List<int> Values { get; set; } = [];
    }

    [Fact]
    public void A_key_that_maps_no_column_throws_its_own_code() {
        ColumnInfo[] cols = [new("Values", typeof(int), false)];
        Refusals.Raises(ErrorCodes.GroupKeyUnmapped,
            () => TypeParser.GetTypeParser<List<KeyedWidget>>(ref cols));
    }

    public sealed record RuntimeKeyed(int Id, string Name, List<int> Values) : IDbReadable;

    [Fact]
    public void Setting_a_group_key_at_runtime_narrows_the_boundary() {
        TypeParsingInfoHelper.SetGroupKey<RuntimeKeyed>(nameof(RuntimeKeyed.Id));
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false), new("Values", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<RuntimeKeyed>>(ref cols);
        using var reader = Rows.Reader(cols, [1, "a", 10], [1, "b", 11], [2, "c", 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal([10, 11], result[0].Values);
        Assert.Equal([20], result[1].Values);
    }

    // --- a tuple is not special: a named type folds the same by default ----------------------------------

    public sealed record Pair(List<int> Numbers, List<string> Words) : IDbReadable;

    [Fact]
    public void A_named_type_with_only_collections_folds_the_whole_result() {
        ColumnInfo[] cols = [new("Numbers", typeof(int), false), new("Words", typeof(string), false)];
        var parser = TypeParser.GetTypeParser<Pair>(ref cols);
        using var reader = Rows.Reader(cols, [1, "a"], [2, "b"], [3, "c"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal([1, 2, 3], result.Numbers);
        Assert.Equal(["a", "b", "c"], result.Words);
    }

    public sealed record Regional(int Region, List<decimal> Amounts) : IDbReadable;

    [Fact]
    public void Querying_a_single_spanning_value_stops_at_the_boundary_change() {
        ColumnInfo[] cols = [new("Region", typeof(int), false), new("Amounts", typeof(decimal), false)];
        var parser = TypeParser.GetTypeParser<Regional>(ref cols);
        using var reader = Rows.Reader(cols, [1, 9.99m], [1, 4.00m], [2, 5.00m]);
        reader.Read();
        var (canContinue, first) = parser.Parse(reader);

        Assert.True(canContinue);
        Assert.Equal(1, first.Region);
        Assert.Equal([9.99m, 4.00m], first.Amounts);
    }

    [Fact]
    public void A_named_type_with_a_leading_value_keys_on_it() {
        ColumnInfo[] cols = [new("Region", typeof(int), false), new("Amounts", typeof(decimal), false)];
        var parser = TypeParser.GetTypeParser<List<Regional>>(ref cols);
        using var reader = Rows.Reader(cols, [1, 9.99m], [1, 4.00m], [2, 5.00m]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Region);
        Assert.Equal([9.99m, 4.00m], result[0].Amounts);
        Assert.Equal(2, result[1].Region);
        Assert.Equal([5.00m], result[1].Amounts);
    }

    // --- values around a collection: before, after, between, one or many ---------------------------------

    public sealed record TwoBefore(int A, int B, List<int> Items) : IDbReadable;

    [Fact]
    public void Several_values_before_the_collection_all_key() {
        ColumnInfo[] cols = [new("A", typeof(int), false), new("B", typeof(int), false), new("Items", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<TwoBefore>>(ref cols);
        using var reader = Rows.Reader(cols, [1, 1, 10], [1, 1, 11], [1, 2, 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, 1), (result[0].A, result[0].B));
        Assert.Equal([10, 11], result[0].Items);
        Assert.Equal((1, 2), (result[1].A, result[1].B));
        Assert.Equal([20], result[1].Items);
    }

    public sealed record TwoAfter(int A, List<int> Items, int X, int Y) : IDbReadable;

    [Fact]
    public void Several_values_after_the_collection_are_captured_once() {
        ColumnInfo[] cols = [new("A", typeof(int), false), new("Items", typeof(int), false), new("X", typeof(int), false), new("Y", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<TwoAfter>>(ref cols);
        using var reader = Rows.Reader(cols, [1, 10, 7, 8], [1, 11, 7, 8], [2, 20, 5, 6]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, 7, 8), (result[0].A, result[0].X, result[0].Y));
        Assert.Equal([10, 11], result[0].Items);
        Assert.Equal((2, 5, 6), (result[1].A, result[1].X, result[1].Y));
        Assert.Equal([20], result[1].Items);
    }

    public sealed record Between(int A, List<int> Items, int Mid, List<int> Others) : IDbReadable;

    [Fact]
    public void Values_between_two_collections_are_captured_once() {
        ColumnInfo[] cols = [new("A", typeof(int), false), new("Items", typeof(int), false), new("Mid", typeof(int), false), new("Others", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<Between>>(ref cols);
        using var reader = Rows.Reader(cols, [1, 10, 99, 20], [1, 11, 99, 21], [2, 12, 88, 22]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, 99), (result[0].A, result[0].Mid));
        Assert.Equal([10, 11], result[0].Items);
        Assert.Equal([20, 21], result[0].Others);
        Assert.Equal((2, 88), (result[1].A, result[1].Mid));
        Assert.Equal([12], result[1].Items);
        Assert.Equal([22], result[1].Others);
    }

    public sealed record Report(List<int> Rows, int Total) : IDbReadable;

    [Fact]
    public void A_value_after_a_collection_with_none_before_throws() {
        ColumnInfo[] cols = [new("Rows", typeof(int), false), new("Total", typeof(int), false)];
        Refusals.Raises(ErrorCodes.MissingGroupBoundary,
            () => TypeParser.GetTypeParser<Report>(ref cols));
    }

    // --- an alt on the collection changes the element prefix ---------------------------------------------

    public sealed record AltAlbum(int Id, string Title) : IDbReadable;
    public sealed record AltArtist(int Id, string Name, [Alt("Album")] List<AltAlbum> Albums) : IDbReadable;

    [Fact]
    public void An_alt_on_a_collection_lets_its_element_columns_drop_the_plural() {
        ColumnInfo[] cols = [
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
            new("AlbumId", typeof(int), true),
            new("AlbumTitle", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<AltArtist>>(ref cols);
        using var reader = Rows.Reader(cols,
            [1, "AC/DC", 10, "High Voltage"],
            [1, "AC/DC", 11, "Let There Be Rock"],
            [2, "Queen", 20, "Jazz"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal([new AltAlbum(10, "High Voltage"), new AltAlbum(11, "Let There Be Rock")], result[0].Albums);
        Assert.Equal([new AltAlbum(20, "Jazz")], result[1].Albums);
    }

    // --- keys on construction parameters -----------------------------------------------------------------

    public sealed record ParamKeyParent([GroupKey] int Id, string Name, List<MultiRowTests.Child> Children) : IDbReadable;

    [Fact]
    public void A_construction_parameter_marked_as_key_groups_by_its_column() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<List<ParamKeyParent>>(ref cols);
        using var reader = Rows.Reader(cols, [1, "P1", 10, "c10"], [1, "P1", 11, "c11"], [2, "P2", 20, "c20"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, "P1"), (result[0].Id, result[0].Name));
        Assert.Equal([new MultiRowTests.Child(10, "c10"), new MultiRowTests.Child(11, "c11")], result[0].Children);
        Assert.Equal([new MultiRowTests.Child(20, "c20")], result[1].Children);
    }

    public sealed record OverrideParent([GroupKey] int CtorKey, [property: GroupKey] int TypeKey, List<MultiRowTests.Child> Children) : IDbReadable;

    [Fact]
    public void A_construction_parameter_key_overrides_the_type_level_member_key() {
        ColumnInfo[] cols = [
            new("CtorKey", typeof(int), false),
            new("TypeKey", typeof(int), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<OverrideParent>>(ref cols);
        using var reader = Rows.Reader(cols,
            [1, 100, 10, "c10"],
            [1, 200, 11, "c11"],
            [2, 100, 20, "c20"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].CtorKey);
        Assert.Equal([new MultiRowTests.Child(10, "c10"), new MultiRowTests.Child(11, "c11")], result[0].Children);
        Assert.Equal(2, result[1].CtorKey);
        Assert.Equal([new MultiRowTests.Child(20, "c20")], result[1].Children);
    }

    public sealed record CompositeParamParent([GroupKey] int A, [GroupKey] int B, List<MultiRowTests.Child> Children) : IDbReadable;

    [Fact]
    public void Composite_construction_parameter_keys_compose() {
        ColumnInfo[] cols = [
            new("A", typeof(int), false),
            new("B", typeof(int), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<CompositeParamParent>>(ref cols);
        using var reader = Rows.Reader(cols, [1, 1, 10, "c10"], [1, 1, 11, "c11"], [1, 2, 20, "c20"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, 1), (result[0].A, result[0].B));
        Assert.Equal([new MultiRowTests.Child(10, "c10"), new MultiRowTests.Child(11, "c11")], result[0].Children);
        Assert.Equal((1, 2), (result[1].A, result[1].B));
        Assert.Equal([new MultiRowTests.Child(20, "c20")], result[1].Children);
    }

    // --- a member key that diverges from the default shape ------------------------------------------------

    public sealed record MemberKeyShape(int Leading, [property: GroupKey] int Actual, List<MultiRowTests.Child> Children) : IDbReadable;

    [Fact]
    public void A_member_key_groups_by_its_column_not_the_leading_shape() {
        ColumnInfo[] cols = [
            new("Leading", typeof(int), false),
            new("Actual", typeof(int), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<MemberKeyShape>>(ref cols);
        using var reader = Rows.Reader(cols, [100, 1, 10, "c10"], [200, 1, 11, "c11"], [300, 2, 20, "c20"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Actual);
        Assert.Equal([new MultiRowTests.Child(10, "c10"), new MultiRowTests.Child(11, "c11")], result[0].Children);
        Assert.Equal(2, result[1].Actual);
        Assert.Equal([new MultiRowTests.Child(20, "c20")], result[1].Children);
    }

    // --- a construction method override and same-level conflicts ------------------------------------------

    public sealed class CtorMethodKeyed : IDbReadable {
        [GroupKeyMethod(nameof(SameTens))]
        public CtorMethodKeyed(int value, List<MultiRowTests.Child> children) {
            Value = value;
            Children = children;
        }
        public int Value { get; }
        public List<MultiRowTests.Child> Children { get; }
        public static (bool Same, int Next) SameTens(int stored, int value) => (value / 10 == stored / 10, value);
    }

    [Fact]
    public void A_construction_method_reference_overrides_the_default_shape() {
        ColumnInfo[] cols = [
            new("Value", typeof(int), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<CtorMethodKeyed>>(ref cols);
        using var reader = Rows.Reader(cols, [1, 10, "c10"], [3, 11, "c11"], [25, 20, "c20"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal([new MultiRowTests.Child(10, "c10"), new MultiRowTests.Child(11, "c11")], result[0].Children);
        Assert.Equal([new MultiRowTests.Child(20, "c20")], result[1].Children);
    }

    public sealed class CtorKeyConflict : IDbReadable {
        [GroupKeyMethod(nameof(Same))]
        public CtorKeyConflict([GroupKey] int a, List<MultiRowTests.Child> children) {
            A = a;
            Children = children;
        }
        public int A { get; }
        public List<MultiRowTests.Child> Children { get; }
        public static (bool Same, int Next) Same(int stored, int a) => (a == stored, a);
    }

    [Fact]
    public void A_construction_with_both_a_method_reference_and_a_key_parameter_throws() {
        ColumnInfo[] cols = [new("A", typeof(int), false), new("ChildrenId", typeof(int), true), new("ChildrenValue", typeof(string), true)];
        Refusals.Raises(ErrorCodes.ConflictingGroupKey, () => TypeParser.GetTypeParser<List<CtorKeyConflict>>(ref cols));
    }

    public sealed class TypeKeyConflict : IDbReadable {
        [GroupKey] public int Id { get; set; }
        public List<MultiRowTests.Child> Children { get; set; } = [];
        [GroupKey] public static (bool Same, int Next) Key(int stored, int id) => (id == stored, id);
    }

    [Fact]
    public void A_member_key_and_a_method_key_on_one_type_throw() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("ChildrenId", typeof(int), true), new("ChildrenValue", typeof(string), true)];
        Refusals.Raises(ErrorCodes.ConflictingGroupKey, () => TypeParser.GetTypeParser<List<TypeKeyConflict>>(ref cols));
    }

    // --- collapse path -----------------------------------------------------------------------------------

    public sealed record KeyedPoint([property: GroupKey] int Id, string Name) : IDbReadable;

    [Fact]
    public void An_all_simple_type_collapses_and_its_key_does_not_span() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        var parser = TypeParser.DefaultTypeParserMaker.ForceMultiRow<KeyedPoint>(TypeParser.GetDefaultNullColHandler<KeyedPoint>(), cols);
        using var reader = Rows.Reader(cols, [1, "one"], [1, "two"]);
        reader.Read();

        var first = parser.Parse(reader);
        Assert.True(first.CanContinue);
        Assert.Equal(new KeyedPoint(1, "one"), first.Result);
        var second = parser.Parse(reader);
        Assert.False(second.CanContinue);
        Assert.Equal(new KeyedPoint(1, "two"), second.Result);
    }
}
