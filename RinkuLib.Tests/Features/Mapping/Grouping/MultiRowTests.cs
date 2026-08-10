using System.Data.Common;
using Rinku.Mapping.Defaults;
using Rinku.Mapping;
using RinkuLib.Tests.Infrastructure;
using Rinku.Internal;
using Rinku.Mapping.Parsers;
using Xunit;

namespace RinkuLib.Tests.DbParsing;

/// <summary>
/// The multi-row road: a result whose rows are grouped by key folds into nested objects and collections. Every
/// test drives a full in-memory result through the normal query path and checks the whole materialised graph.
/// </summary>
public class MultiRowTests {

    static MultiRowTests() =>
        TypeParsingInfo.AddOrSet(typeof(HashSet<>),
            new MultiRowTypeParsingInfo(
                typeof(HashSet<>).GetConstructor(Type.EmptyTypes)!,
                typeof(HashSet<>).GetMethod(nameof(HashSet<int>.Add)),
                null));

    public sealed record Point(int Id, string Name) : IDbReadable;
    public sealed record Boxed(int Id, Point Sub) : IDbReadable;

    public sealed record Child([AbortOnNull] int Id, string Value) : IDbReadable;
    public sealed record Parent([property: GroupKey] int Id, string Name, List<Child> Children) : IDbReadable;
    public sealed record RegionParent([property: GroupKey] int RegionId, [property: GroupKey] int Id, string Name, List<Child> Children) : IDbReadable;
    public sealed record Order([AbortOnNull] int Id, decimal Amount) : IDbReadable;
    public sealed record SiblingParent([property: GroupKey] int Id, string Name, List<Child> Children, List<Order> Orders) : IDbReadable;

    public sealed record Grand([AbortOnNull] int Id, string Data) : IDbReadable;
    public sealed record NChild([property: GroupKey] int Id, string Value, List<Grand> Grands) : IDbReadable;
    public sealed record NParent([property: GroupKey] int Id, string Name, List<NChild> Children) : IDbReadable;

    public sealed record Inner(int InnerId, List<int> Values) : IDbReadable;
    public sealed record Outer([property: GroupKey] int Id, Inner Inner) : IDbReadable;

    public sealed class FactoryParent : IDbReadable {
        private FactoryParent(int id, List<Child> children) {
            Id = id;
            Children = children;
        }
        public int Id { get; }
        public List<Child> Children { get; }
        public static FactoryParent Create(int id, List<Child> children) => new(id, children);
    }

    public readonly record struct ProductKey(int Region, int Sku) : IDbReadable;
    public sealed record Listing([property: GroupKey] ProductKey Key, string Name, List<Child> Children) : IDbReadable;

    public sealed record Item([AbortOnNull] int Id, string Name) : IDbReadable;
    public sealed record Bucket(string Category, List<Item> Items) : IDbReadable {
        [GroupKey]
        public static (bool Same, string Next) SameCategory(string stored, string category) {
            var current = category.ToUpperInvariant();
            return (current == stored, current);
        }
    }

    public sealed record Window(List<int> Points) : IDbReadable {
        [GroupKey]
        public static (bool Same, int Next) WithinFive(int stored, int points) => (points - stored <= 5, points);
    }

    public sealed class MemberParent : IDbReadable {
        [GroupKey] public int Id { get; set; }
        public string Name { get; set; } = "";
        public List<Child> Children { get; set; } = [];
    }
    public sealed class MixedParent : IDbReadable {
        [CanCompleteWithMembers]
        public MixedParent(int id, string name) {
            Id = id;
            Name = name;
        }
        [GroupKey] public int Id { get; }
        public string Name { get; }
        public List<Child> Children { get; set; } = [];
    }

    public sealed record TagParent([property: GroupKey] int Id, [KeepNullElements] List<string?> Tags) : IDbReadable;
    public sealed record NotNullTagParent([property: GroupKey] int Id, [System.Diagnostics.CodeAnalysis.NotNull] List<string> Tags) : IDbReadable;
    public sealed record InferredParent(int Id, string Name, List<Child> Children) : IDbReadable;
    public sealed record ValueAfterCollection(List<Child> Children, int Trailing) : IDbReadable;
    public sealed record DynaHolder(List<DynaObject> Rows) : IDbReadable {
        [GroupKey]
        public static (bool Same, int Next) KeyOf(int stored, int id) => (id == stored, id);
    }

    private static ColumnInfo[] ParentCols() => [
        new("Id", typeof(int), false),
        new("Name", typeof(string), false),
        new("ChildrenId", typeof(int), true),
        new("ChildrenValue", typeof(string), true),
    ];

    private static ITypeParser<T> ForceMultiRow<T>(ColumnInfo[] cols)
        => Assert.IsType<DefaultTypeParserMaker>(TypeParser.DefaultTypeParserMaker).ForceMultiRow<T>(TypeParser.GetDefaultNullColHandler<T>(), cols);

    // --- the single-row road reproduced through the multi-row machine -----------------------------------

    [Fact]
    public void A_scalar_reproduces_the_single_row_value() {
        ColumnInfo[] cols = [new("V", typeof(int), false)];
        using var reader = Rows.Reader(cols, [42]);
        reader.Read();
        var (canContinue, value) = ForceMultiRow<int>(cols).Parse(reader);
        Assert.False(canContinue);
        Assert.Equal(42, value);
    }

    [Fact]
    public void A_record_reproduces_the_single_row_value() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        using var reader = Rows.Reader(cols, [7, "seven"]);
        reader.Read();
        var (canContinue, value) = ForceMultiRow<Point>(cols).Parse(reader);
        Assert.False(canContinue);
        Assert.Equal(new Point(7, "seven"), value);
    }

    [Fact]
    public void A_plain_type_stops_after_its_first_row_and_reads_the_next_on_the_second_parse() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        var parser = ForceMultiRow<Point>(cols);
        using var reader = Rows.Reader(cols, [1, "one"], [2, "two"]);
        reader.Read();

        var first = parser.Parse(reader);
        Assert.True(first.CanContinue);
        Assert.Equal(new Point(1, "one"), first.Result);

        var second = parser.Parse(reader);
        Assert.False(second.CanContinue);
        Assert.Equal(new Point(2, "two"), second.Result);
    }

    [Fact]
    public void A_collapsed_nested_object_reproduces_the_whole_value() {
        ColumnInfo[] cols = [
            new("Id", typeof(int), false),
            new("SubId", typeof(int), false),
            new("SubName", typeof(string), false),
        ];
        using var reader = Rows.Reader(cols, [1, 9, "sub"]);
        reader.Read();
        var value = ForceMultiRow<Boxed>(cols).Parse(reader).Result;
        Assert.Equal(new Boxed(1, new Point(9, "sub")), value);
    }

    // --- tuples with an always-true boundary ------------------------------------------------------------

    [Fact]
    public void A_tuple_of_two_collections_reads_the_whole_result() {
        ColumnInfo[] cols = [new("A", typeof(int), false), new("B", typeof(string), false)];
        var parser = TypeParser.GetTypeParser<(List<int>, List<string>)>(cols);
        using var reader = Rows.Reader(cols, [1, "a"], [2, "b"], [3, "c"]);
        reader.Read();
        var (canContinue, result) = parser.Parse(reader);

        Assert.False(canContinue);
        Assert.Equal([1, 2, 3], result.Item1);
        Assert.Equal(["a", "b", "c"], result.Item2);
    }

    [Fact]
    public void A_scalar_beside_a_collection_is_captured_once() {
        ColumnInfo[] cols = [new("A", typeof(int), false), new("B", typeof(string), false)];
        var parser = TypeParser.GetTypeParser<(int, List<string>)>(cols);
        using var reader = Rows.Reader(cols, [5, "a"], [5, "b"]);
        reader.Read();
        var (canContinue, result) = parser.Parse(reader);

        Assert.False(canContinue);
        Assert.Equal(5, result.Item1);
        Assert.Equal(["a", "b"], result.Item2);
    }

    [Fact]
    public void A_tuple_with_a_value_after_a_collection_asks_for_an_explicit_key() {
        ColumnInfo[] cols = [new("A", typeof(int), false), new("B", typeof(int), false)];
        Refusals.Raises(ErrorCodes.MissingGroupBoundary,
            () => TypeParser.GetTypeParser<(List<int>, int)>(cols));
    }

    // --- keyed grouping ---------------------------------------------------------------------------------

    [Fact]
    public void A_keyed_list_groups_children_under_each_parent() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<List<Parent>>(cols);
        using var reader = Rows.Reader(cols,
            [1, "P1", 10, "c10"],
            [1, "P1", 11, "c11"],
            [2, "P2", 20, "c20"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, "P1"), (result[0].Id, result[0].Name));
        Assert.Equal([new Child(10, "c10"), new Child(11, "c11")], result[0].Children);
        Assert.Equal((2, "P2"), (result[1].Id, result[1].Name));
        Assert.Equal([new Child(20, "c20")], result[1].Children);
    }

    [Fact]
    public void A_left_join_leaves_a_childless_parent_with_an_empty_collection() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<List<Parent>>(cols);
        using var reader = Rows.Reader(cols,
            [1, "P1", 10, "c10"],
            [2, "P2", DBNull.Value, DBNull.Value]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal("P1", result[0].Name);
        Assert.Equal([new Child(10, "c10")], result[0].Children);
        Assert.Equal(2, result[1].Id);
        Assert.Equal("P2", result[1].Name);
        Assert.Empty(result[1].Children);
    }

    [Fact]
    public void A_composite_key_tells_apart_rows_that_share_only_one_part() {
        ColumnInfo[] cols = [
            new("RegionId", typeof(int), false),
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<RegionParent>>(cols);
        using var reader = Rows.Reader(cols,
            [1, 10, "A", 100, "c"],
            [1, 10, "A", 101, "d"],
            [1, 11, "B", 200, "e"],
            [2, 10, "C", 300, "f"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(3, result.Count);
        Assert.Equal((1, 10, "A"), (result[0].RegionId, result[0].Id, result[0].Name));
        Assert.Equal([new Child(100, "c"), new Child(101, "d")], result[0].Children);
        Assert.Equal((1, 11, "B"), (result[1].RegionId, result[1].Id, result[1].Name));
        Assert.Equal([new Child(200, "e")], result[1].Children);
        Assert.Equal((2, 10, "C"), (result[2].RegionId, result[2].Id, result[2].Name));
        Assert.Equal([new Child(300, "f")], result[2].Children);
    }

    [Fact]
    public void A_custom_key_type_groups_by_its_own_equality() {
        ColumnInfo[] cols = [
            new("KeyRegion", typeof(int), false),
            new("KeySku", typeof(int), false),
            new("Name", typeof(string), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<Listing>>(cols);
        using var reader = Rows.Reader(cols,
            [1, 100, "A", 10, "c10"],
            [1, 100, "A", 11, "c11"],
            [1, 101, "B", 20, "c20"],
            [2, 100, "C", 30, "c30"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(3, result.Count);
        Assert.Equal((new ProductKey(1, 100), "A"), (result[0].Key, result[0].Name));
        Assert.Equal([new Child(10, "c10"), new Child(11, "c11")], result[0].Children);
        Assert.Equal((new ProductKey(1, 101), "B"), (result[1].Key, result[1].Name));
        Assert.Equal([new Child(20, "c20")], result[1].Children);
        Assert.Equal((new ProductKey(2, 100), "C"), (result[2].Key, result[2].Name));
        Assert.Equal([new Child(30, "c30")], result[2].Children);
    }

    public sealed class AltKeyParent : IDbReadable {
        [GroupKey, Alt("PKey")] public int Id { get; set; }
        public string Name { get; set; } = "";
        public List<Child> Children { get; set; } = [];
    }

    [Fact]
    public void A_key_member_matches_through_its_alt_name() {
        ColumnInfo[] cols = [
            new("PKey", typeof(int), false),
            new("Name", typeof(string), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<AltKeyParent>>(cols);
        using var reader = Rows.Reader(cols, [1, "P1", 10, "c10"], [1, "P1", 11, "c11"], [2, "P2", 20, "c20"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, "P1"), (result[0].Id, result[0].Name));
        Assert.Equal([new Child(10, "c10"), new Child(11, "c11")], result[0].Children);
        Assert.Equal((2, "P2"), (result[1].Id, result[1].Name));
        Assert.Equal([new Child(20, "c20")], result[1].Children);
    }

    [Fact]
    public void A_method_boundary_decides_same_group_itself() {
        ColumnInfo[] cols = [
            new("Category", typeof(string), false),
            new("ItemsId", typeof(int), true),
            new("ItemsName", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<Bucket>>(cols);
        using var reader = Rows.Reader(cols,
            ["Tech", 1, "a"],
            ["tech", 2, "b"],
            ["Food", 3, "c"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal("Tech", result[0].Category);
        Assert.Equal([new Item(1, "a"), new Item(2, "b")], result[0].Items);
        Assert.Equal("Food", result[1].Category);
        Assert.Equal([new Item(3, "c")], result[1].Items);
    }

    [Fact]
    public void A_method_boundary_can_compare_against_a_running_value_not_equality() {
        ColumnInfo[] cols = [new("Points", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<Window>>(cols);
        using var reader = Rows.Reader(cols, [1], [3], [6], [20], [22]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal([1, 3, 6], result[0].Points);
        Assert.Equal([20, 22], result[1].Points);
    }

    [Fact]
    public void A_top_level_registered_collection_groups_without_a_dedicated_parser() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<HashSet<Parent>>(cols);
        using var reader = Rows.Reader(cols,
            [1, "P1", 10, "c10"],
            [1, "P1", 11, "c11"],
            [2, "P2", 20, "c20"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        var byId = result.ToDictionary(p => p.Id);
        Assert.Equal("P1", byId[1].Name);
        Assert.Equal([new Child(10, "c10"), new Child(11, "c11")], byId[1].Children);
        Assert.Equal("P2", byId[2].Name);
        Assert.Equal([new Child(20, "c20")], byId[2].Children);
    }

    [Fact]
    public void A_top_level_registered_collection_of_scalars_reads_every_row() {
        ColumnInfo[] cols = [new("V", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<HashSet<int>>(cols);
        using var reader = Rows.Reader(cols, [5], [7], [7], [9]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal([5, 7, 9], result.OrderBy(x => x));
    }

    [Fact]
    public void A_top_level_registered_collection_is_empty_for_no_rows() {
        ColumnInfo[] cols = [new("V", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<HashSet<int>>(cols);
        Assert.Empty(parser.Default());
    }

    [Fact]
    public void A_registered_collection_can_be_given_its_add_explicitly() {
        TypeParsingInfo.AddOrSet(typeof(SortedSet<int>), new MultiRowTypeParsingInfo(
            typeof(SortedSet<int>).GetConstructor(Type.EmptyTypes)!,
            typeof(SortedSet<int>).GetMethod(nameof(SortedSet<int>.Add), [typeof(int)]),
            null));
        ColumnInfo[] cols = [new("V", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<SortedSet<int>>(cols);
        using var reader = Rows.Reader(cols, [9], [5], [7], [5]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal([5, 7, 9], result);
    }

    public sealed record PairedAlbum(int Id, string Title);

    [Fact]
    public void A_tuple_pairs_a_grouping_id_with_a_built_object() {
        ColumnInfo[] cols = [new("ArtistId", typeof(int), false), new("Id", typeof(int), false), new("Title", typeof(string), false)];
        var parser = TypeParser.GetTypeParser<List<(int ArtistId, PairedAlbum Album)>>(cols);
        using var reader = Rows.Reader(cols, [1, 10, "High Voltage"], [1, 11, "Let There Be Rock"], [2, 20, "Jazz"]);
        reader.Read();
        var rows = parser.Parse(reader).Result;

        Assert.Equal(3, rows.Count);
        Assert.Equal((1, new PairedAlbum(10, "High Voltage")), rows[0]);
        Assert.Equal((1, new PairedAlbum(11, "Let There Be Rock")), rows[1]);
        Assert.Equal((2, new PairedAlbum(20, "Jazz")), rows[2]);
    }

    public sealed record ArtistShell(int Id, string Name) {
        public List<PairedAlbum> Albums { get; } = [];
    }

    [Fact]
    public void Manual_grouping_streams_children_into_parents_by_key() {
        ColumnInfo[] pcols = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        var pparser = TypeParser.GetTypeParser<List<ArtistShell>>(pcols);
        using var preader = Rows.Reader(pcols, [1, "AC/DC"], [2, "Queen"]);
        preader.Read();
        var artists = pparser.Parse(preader).Result;

        ColumnInfo[] ccols = [new("ArtistId", typeof(int), false), new("Id", typeof(int), false), new("Title", typeof(string), false)];
        var cparser = TypeParser.GetTypeParser<IEnumerable<(int ArtistId, PairedAlbum Album)>>(ccols);
        using var creader = Rows.Reader(ccols, [1, 10, "High Voltage"], [1, 11, "Let There Be Rock"], [2, 20, "Jazz"]);
        creader.Read();
        using var albums = cparser.Parse(creader).Result.GetEnumerator();

        bool more = albums.MoveNext();
        foreach (var artist in artists)
            while (more && albums.Current.ArtistId == artist.Id) {
                artist.Albums.Add(albums.Current.Album);
                more = albums.MoveNext();
            }

        Assert.Equal([new PairedAlbum(10, "High Voltage"), new PairedAlbum(11, "Let There Be Rock")], artists[0].Albums);
        Assert.Equal([new PairedAlbum(20, "Jazz")], artists[1].Albums);
        Assert.Equal([1, 2], artists.Select(a => a.Id));
        Assert.Equal(["AC/DC", "Queen"], artists.Select(a => a.Name));
    }

    [Fact]
    public void A_keyless_type_infers_its_leading_scalars_as_the_key() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<List<InferredParent>>(cols);
        using var reader = Rows.Reader(cols,
            [1, "P1", 10, "c10"],
            [1, "P1", 11, "c11"],
            [1, "P2", 12, "c12"],
            [2, "P2", 20, "c20"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(3, result.Count);
        Assert.Equal((1, "P1"), (result[0].Id, result[0].Name));
        Assert.Equal([new Child(10, "c10"), new Child(11, "c11")], result[0].Children);
        Assert.Equal((1, "P2"), (result[1].Id, result[1].Name));
        Assert.Equal([new Child(12, "c12")], result[1].Children);
        Assert.Equal((2, "P2"), (result[2].Id, result[2].Name));
        Assert.Equal([new Child(20, "c20")], result[2].Children);
    }

    [Fact]
    public void A_value_after_a_collection_with_no_key_throws_at_build_time() {
        ColumnInfo[] cols = [
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
            new("Trailing", typeof(int), false),
        ];
        Refusals.Raises(ErrorCodes.MissingGroupBoundary,
            () => TypeParser.GetTypeParser<ValueAfterCollection>(cols));
    }

    // --- sibling collections ----------------------------------------------------------------------------

    [Fact]
    public void Two_sibling_collections_fill_from_their_own_null_gated_rows() {
        ColumnInfo[] cols = [
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
            new("OrdersId", typeof(int), true),
            new("OrdersAmount", typeof(decimal), true),
        ];
        var parser = TypeParser.GetTypeParser<List<SiblingParent>>(cols);
        using var reader = Rows.Reader(cols,
            [1, "P1", 10, "c10", DBNull.Value, DBNull.Value],
            [1, "P1", 11, "c11", DBNull.Value, DBNull.Value],
            [1, "P1", DBNull.Value, DBNull.Value, 100, 9.99m],
            [1, "P1", DBNull.Value, DBNull.Value, 101, 5.00m]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
        Assert.Equal("P1", result[0].Name);
        Assert.Equal([new Child(10, "c10"), new Child(11, "c11")], result[0].Children);
        Assert.Equal([new Order(100, 9.99m), new Order(101, 5.00m)], result[0].Orders);
    }

    // --- nested levels with the flush cascade -----------------------------------------------------------

    private static ColumnInfo[] NestedCols() => [
        new("Id", typeof(int), false),
        new("Name", typeof(string), false),
        new("ChildrenId", typeof(int), true),
        new("ChildrenValue", typeof(string), true),
        new("ChildrenGrandsId", typeof(int), true),
        new("ChildrenGrandsData", typeof(string), true),
    ];

    [Fact]
    public void Two_levels_group_children_and_grandchildren_with_the_cascade() {
        var cols = NestedCols();
        var parser = TypeParser.GetTypeParser<List<NParent>>(cols);
        using var reader = Rows.Reader(cols,
            [1, "P1", 100, "C100", 1000, "g1000"],
            [1, "P1", 100, "C100", 1001, "g1001"],
            [1, "P1", 101, "C101", 1010, "g1010"],
            [2, "P2", 200, "C200", DBNull.Value, DBNull.Value],
            [3, "P3", DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(3, result.Count);

        Assert.Equal((1, "P1"), (result[0].Id, result[0].Name));
        Assert.Equal(2, result[0].Children.Count);
        Assert.Equal((100, "C100"), (result[0].Children[0].Id, result[0].Children[0].Value));
        Assert.Equal([new Grand(1000, "g1000"), new Grand(1001, "g1001")], result[0].Children[0].Grands);
        Assert.Equal((101, "C101"), (result[0].Children[1].Id, result[0].Children[1].Value));
        Assert.Equal([new Grand(1010, "g1010")], result[0].Children[1].Grands);

        Assert.Equal((2, "P2"), (result[1].Id, result[1].Name));
        Assert.Single(result[1].Children);
        Assert.Equal((200, "C200"), (result[1].Children[0].Id, result[1].Children[0].Value));
        Assert.Empty(result[1].Children[0].Grands);

        Assert.Equal((3, "P3"), (result[2].Id, result[2].Name));
        Assert.Empty(result[2].Children);
    }

    // --- construction paths -----------------------------------------------------------------------------

    [Fact]
    public void A_keyless_nested_object_holds_a_collection_that_spans_the_group() {
        ColumnInfo[] cols = [
            new("Id", typeof(int), false),
            new("InnerInnerId", typeof(int), false),
            new("InnerValues", typeof(int), false),
        ];
        var parser = TypeParser.GetTypeParser<List<Outer>>(cols);
        using var reader = Rows.Reader(cols,
            [1, 10, 100],
            [1, 10, 101],
            [2, 20, 200]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal(10, result[0].Inner.InnerId);
        Assert.Equal([100, 101], result[0].Inner.Values);
        Assert.Equal(2, result[1].Id);
        Assert.Equal(20, result[1].Inner.InnerId);
        Assert.Equal([200], result[1].Inner.Values);
    }

    [Fact]
    public void A_static_factory_can_construct_a_multi_row_parent() {
        ColumnInfo[] cols = [
            new("Id", typeof(int), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<FactoryParent>>(cols);
        using var reader = Rows.Reader(cols,
            [1, 10, "c10"],
            [1, 11, "c11"],
            [2, 20, "c20"]);
        reader.Read();

        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal([new Child(10, "c10"), new Child(11, "c11")], result[0].Children);
        Assert.Equal(2, result[1].Id);
        Assert.Equal([new Child(20, "c20")], result[1].Children);
    }

    [Fact]
    public void Adjacent_grouping_matches_a_hand_written_reference_model() {
        ColumnInfo[] cols = [
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var rows = new[] {
            (Id: 1, Name: "P1", ChildId: (int?)10, ChildValue: "c10"),
            (Id: 1, Name: "P1", ChildId: (int?)11, ChildValue: "c11"),
            (Id: 2, Name: "P2", ChildId: (int?)20, ChildValue: "c20"),
            (Id: 1, Name: "P1-again", ChildId: (int?)12, ChildValue: "c12"),
        };

        // SQL result grouping is adjacent. A later equal key starts a new parent.
        var expected = new[] {
            new Parent(1, "P1", [new Child(10, "c10"), new Child(11, "c11")]),
            new Parent(2, "P2", [new Child(20, "c20")]),
            new Parent(1, "P1-again", [new Child(12, "c12")]),
        };

        var parser = TypeParser.GetTypeParser<List<Parent>>(cols);
        using var reader = Rows.Reader(cols, rows.Select(r => new object?[] { r.Id, r.Name, r.ChildId.HasValue ? r.ChildId.Value : DBNull.Value, r.ChildValue is null ? DBNull.Value : r.ChildValue }).ToArray());
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(expected.Length, result.Count);
        for (int i = 0; i < expected.Length; i++) {
            Assert.Equal((expected[i].Id, expected[i].Name), (result[i].Id, result[i].Name));
            Assert.Equal(expected[i].Children, result[i].Children);
        }
    }

    [Fact]
    public void A_member_reached_collection_groups_the_same_as_a_constructor_argument() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<List<MemberParent>>(cols);
        using var reader = Rows.Reader(cols,
            [1, "P1", 10, "c10"],
            [1, "P1", 11, "c11"],
            [2, "P2", 20, "c20"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, "P1"), (result[0].Id, result[0].Name));
        Assert.Equal([new Child(10, "c10"), new Child(11, "c11")], result[0].Children);
        Assert.Equal((2, "P2"), (result[1].Id, result[1].Name));
        Assert.Equal([new Child(20, "c20")], result[1].Children);
    }

    [Fact]
    public void A_mix_of_constructor_arguments_and_a_member_collection_groups_correctly() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<List<MixedParent>>(cols);
        using var reader = Rows.Reader(cols,
            [1, "P1", 10, "c10"],
            [1, "P1", 11, "c11"],
            [2, "P2", 20, "c20"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, "P1"), (result[0].Id, result[0].Name));
        Assert.Equal([new Child(10, "c10"), new Child(11, "c11")], result[0].Children);
        Assert.Equal((2, "P2"), (result[1].Id, result[1].Name));
        Assert.Equal([new Child(20, "c20")], result[1].Children);
    }

    // --- null element policy ----------------------------------------------------------------------------

    [Fact]
    public void A_null_scalar_element_is_skipped_by_default() {
        ColumnInfo[] cols = [new("A", typeof(int), false), new("B", typeof(string), true)];
        var parser = TypeParser.GetTypeParser<(List<int>, List<string>)>(cols);
        using var reader = Rows.Reader(cols, [1, "a"], [2, DBNull.Value], [3, "c"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal([1, 2, 3], result.Item1);
        Assert.Equal(["a", "c"], result.Item2);
    }

    [Fact]
    public void KeepNullElements_adds_the_null_element_instead_of_skipping() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Tags", typeof(string), true)];
        var parser = TypeParser.GetTypeParser<List<TagParent>>(cols);
        using var reader = Rows.Reader(cols, [1, "a"], [1, DBNull.Value], [1, "b"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
        Assert.Equal(["a", null, "b"], result[0].Tags);
    }

    [Fact]
    public void NotNull_on_collection_throws_when_null_element_encountered() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Tags", typeof(string), true)];
        var parser = TypeParser.GetTypeParser<List<NotNullTagParent>>(cols);
        using var reader = Rows.Reader(cols, [1, "a"], [1, DBNull.Value], [1, "b"]);
        reader.Read();
        Assert.Throws<NullValueAssignmentException>(() => parser.Parse(reader));
    }

    // --- composition and async --------------------------------------------------------------------------

    private static readonly object[][] TwoParentRows = [
        [1, "P1", 10, "c10"],
        [1, "P1", 11, "c11"],
        [2, "P2", 20, "c20"],
    ];

    [Fact]
    public void Optional_wraps_a_multi_row_parent() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<Optional<Parent>>(cols);
        using var reader = Rows.Reader(cols, [1, "P1", 10, "c10"], [1, "P1", 11, "c11"]);
        reader.Read();
        Parent? value = parser.Parse(reader).Result;

        Assert.NotNull(value);
        Assert.Equal((1, "P1"), (value.Id, value.Name));
        Assert.Equal([new Child(10, "c10"), new Child(11, "c11")], value.Children);
    }

    [Fact]
    public void Single_returns_the_lone_parent_and_refuses_a_second() {
        var cols = ParentCols();
        var one = TypeParser.GetTypeParser<Single<Parent>>(cols);
        using (var reader = Rows.Reader(cols, [1, "P1", 10, "c10"], [1, "P1", 11, "c11"])) {
            reader.Read();
            Parent value = one.Parse(reader).Result;
            Assert.Equal((1, "P1"), (value.Id, value.Name));
            Assert.Equal([new Child(10, "c10"), new Child(11, "c11")], value.Children);
        }
        using var two = Rows.Reader(cols, [1, "P1", 10, "c10"], [2, "P2", 20, "c20"]);
        two.Read();
        Refusals.Raises(ErrorCodes.ShapeRefusedResult, () => one.Parse(two));
    }

    [Fact]
    public void IEnumerable_streams_each_parent_group() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<IEnumerable<Parent>>(cols);
        using var reader = Rows.Reader(cols, TwoParentRows);
        reader.Read();
        var result = parser.Parse(reader).Result.ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal((1, "P1"), (result[0].Id, result[0].Name));
        Assert.Equal([new Child(10, "c10"), new Child(11, "c11")], result[0].Children);
        Assert.Equal((2, "P2"), (result[1].Id, result[1].Name));
        Assert.Equal([new Child(20, "c20")], result[1].Children);
    }

    [Fact]
    public async Task The_async_driver_folds_the_same_group_as_the_sync_one() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<Parent>(cols);
        using var reader = Rows.Reader(cols, [1, "P1", 10, "c10"], [1, "P1", 11, "c11"]);
        reader.Read();
        var (canContinue, parent) = await parser.ParseAsync(reader, TestContext.Current.CancellationToken);

        Assert.False(canContinue);
        Assert.Equal((1, "P1"), (parent.Id, parent.Name));
        Assert.Equal([new Child(10, "c10"), new Child(11, "c11")], parent.Children);
    }

    // --- collection kinds -------------------------------------------------------------------------------

    public sealed record ArrayParent([property: GroupKey] int Id, string Name, Child[] Children) : IDbReadable;
    public sealed record EnumerableParent([property: GroupKey] int Id, string Name, IEnumerable<Child> Children) : IDbReadable;
    public sealed record HashParent([property: GroupKey] int Id, string Name, HashSet<Child> Children) : IDbReadable;

    [Fact]
    public void A_user_registered_HashSet_maps_and_holds_the_full_elements() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<List<HashParent>>(cols);
        using var reader = Rows.Reader(cols, TwoParentRows);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.IsType<HashSet<Child>>(result[0].Children);
        Assert.Equal((1, "P1"), (result[0].Id, result[0].Name));
        Assert.Equal([new Child(10, "c10"), new Child(11, "c11")], result[0].Children.OrderBy(c => c.Id));
        Assert.Equal((2, "P2"), (result[1].Id, result[1].Name));
        Assert.Equal([new Child(20, "c20")], result[1].Children);
    }

    [Fact]
    public void A_built_in_array_member_maps_and_holds_the_full_elements() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<List<ArrayParent>>(cols);
        using var reader = Rows.Reader(cols, TwoParentRows);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.IsType<Child[]>(result[0].Children);
        Assert.Equal((1, "P1"), (result[0].Id, result[0].Name));
        Assert.Equal([new Child(10, "c10"), new Child(11, "c11")], result[0].Children);
        Assert.Equal((2, "P2"), (result[1].Id, result[1].Name));
        Assert.Equal([new Child(20, "c20")], result[1].Children);
    }

    [Fact]
    public void A_built_in_IEnumerable_member_maps_and_holds_the_full_elements() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<List<EnumerableParent>>(cols);
        using var reader = Rows.Reader(cols, TwoParentRows);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, "P1"), (result[0].Id, result[0].Name));
        Assert.Equal([new Child(10, "c10"), new Child(11, "c11")], result[0].Children);
        Assert.Equal((2, "P2"), (result[1].Id, result[1].Name));
        Assert.Equal([new Child(20, "c20")], result[1].Children);
    }

    [Fact]
    public void A_dynamic_object_collection_grouped_by_a_method_key_captures_the_whole_row() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        var parser = TypeParser.GetTypeParser<List<DynaHolder>>(cols);
        using var reader = Rows.Reader(cols, [1, "a"], [1, "b"], [2, "c"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Rows.Count);
        Assert.Equal(2, result[0].Rows[0].Count);
        Assert.Equal(1, result[0].Rows[0].Get<int>("Id"));
        Assert.Equal("a", result[0].Rows[0].Get<string>("Name"));
        Assert.Equal(1, result[0].Rows[1].Get<int>("Id"));
        Assert.Equal("b", result[0].Rows[1].Get<string>("Name"));
        Assert.Single(result[1].Rows);
        Assert.Equal(2, result[1].Rows[0].Get<int>("Id"));
        Assert.Equal("c", result[1].Rows[0].Get<string>("Name"));
    }

    public sealed record StrictInventory(int Id, List<string> Items);

    [Fact]
    public void A_non_nullable_collection_element_behavior_when_null_is_encountered() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Items", typeof(string), true)];
        var parser = TypeParser.GetTypeParser<List<StrictInventory>>(cols);
        using var reader = Rows.Reader(cols, [1, "bolt"], [1, null], [1, "nail"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
        // The default policy skips a null collection element.
        Assert.Equal(["bolt", "nail"], result[0].Items);
    }

    public sealed record UnregisteredElement(int Id, string Value);

    public sealed record WithUnregisteredCollection(int Id, List<UnregisteredElement> Items) : IDbReadable;

    [Fact]
    public void An_unregistered_element_type_in_a_closed_collection_rejects_the_path() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("ItemsId", typeof(int), false), new("ItemsValue", typeof(string), true)];
        Assert.Throws<RinkuNoParserException>(() => TypeParser.GetTypeParser<List<WithUnregisteredCollection>>(cols));
    }

    public sealed record GenericContainer<T>(int Id, List<T> Items) : IDbReadable;

    [Fact]
    public void An_unregistered_type_resolving_an_open_generic_collection_rejects_the_path() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("ItemsId", typeof(int), false), new("ItemsValue", typeof(string), true)];
        Assert.Throws<RinkuNoParserException>(() => TypeParser.GetTypeParser<List<GenericContainer<UnregisteredElement>>>(cols));
    }

}
