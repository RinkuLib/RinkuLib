using System.Diagnostics.CodeAnalysis;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>
/// The null rules at work: invalid-on-null collapsing the owning object, defaults substituted per value
/// kind, and the wrapper shapes built around non-simple element parsers.
/// </summary>
public class NullHandlingAndShapeTests {

    public record struct CollapseInner(int A, [InvalidOnNull] int? B) : IDbReadable;
    public record CollapseOuter(int Id, CollapseInner? Inner) : IDbReadable;

    [Fact]
    public void An_invalid_on_null_slot_collapses_its_owner() {
        ColumnInfo[] cols = [
            new("Id", typeof(int), false),
            new("InnerA", typeof(int), false),
            new("InnerB", typeof(int), true),
        ];
        var full = Rows.ParseOne<CollapseOuter>(cols, 1, 2, 3);
        Assert.Equal(new CollapseInner(2, 3), full.Inner);

        var collapsed = Rows.ParseOne<CollapseOuter>(cols, 1, 2, DBNull.Value);
        Assert.Equal(1, collapsed.Id);
        Assert.Null(collapsed.Inner);
    }

    [Fact]
    public void Every_handle_flips_between_plain_and_invalid_on_null() {
        Assert.Same(InvalidOnNullAndNullableHandle.Instance, NullableTypeHandle.Instance.SetInvalidOnNull(typeof(int?), true));
        Assert.Same(NullableTypeHandle.Instance, NullableTypeHandle.Instance.SetInvalidOnNull(typeof(int?), false));
        Assert.Same(InvalidOnNullAndNullableHandle.Instance, InvalidOnNullAndNullableHandle.Instance.SetInvalidOnNull(typeof(int?), true));
        Assert.Same(NullableTypeHandle.Instance, InvalidOnNullAndNullableHandle.Instance.SetInvalidOnNull(typeof(int?), false));
        Assert.Same(InvalidOnNullAndNotNullHandle.Instance, NotNullHandle.Instance.SetInvalidOnNull(typeof(int), true));
        Assert.Same(NotNullHandle.Instance, NotNullHandle.Instance.SetInvalidOnNull(typeof(int), false));
        Assert.Same(InvalidOnNullAndNotNullHandle.Instance, InvalidOnNullAndNotNullHandle.Instance.SetInvalidOnNull(typeof(int), true));
        Assert.Same(NotNullHandle.Instance, InvalidOnNullAndNotNullHandle.Instance.SetInvalidOnNull(typeof(int), false));
    }


    public record Defaults(
        [MaybeNull] long L,
        [MaybeNull] ulong UL,
        [MaybeNull] float F,
        [MaybeNull] double D,
        [MaybeNull] decimal M,
        [MaybeNull] Guid G,
        [MaybeNull] DayOfWeek E,
        [MaybeNull] string? S) : IDbReadable;

    [Fact]
    public void A_null_column_substitutes_the_kind_default() {
        ColumnInfo[] cols = [
            new("L", typeof(long), true), new("UL", typeof(ulong), true),
            new("F", typeof(float), true), new("D", typeof(double), true),
            new("M", typeof(decimal), true), new("G", typeof(Guid), true),
            new("E", typeof(int), true), new("S", typeof(string), true),
        ];
        var d = Rows.ParseOne<Defaults>(cols,
            DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value,
            DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value);
        Assert.Equal(0L, d.L);
        Assert.Equal(0ul, d.UL);
        Assert.Equal(0f, d.F);
        Assert.Equal(0d, d.D);
        Assert.Equal(0m, d.M);
        Assert.Equal(Guid.Empty, d.G);
        Assert.Equal(default, d.E);
        Assert.Null(d.S);
    }


    public class Rollbackable : IDbReadable {
        public int A { get; set; }
        public Rollbackable() { }
        public Rollbackable(int a, string b) => A = -1;
    }

    [Fact]
    public void A_partial_ctor_match_rolls_back_to_the_parameterless_road() {
        ColumnInfo[] cols = [new("A", typeof(int), false)];
        var built = Rows.ParseOne<Rollbackable>(cols, 9);
        Assert.Equal(9, built.A);
    }

    public class SkippingMember : IDbReadable {
        public int Id { get; set; }
        [InvalidOnNull] public int? Extra { get; set; }
    }

    [Fact]
    public void An_invalid_on_null_member_is_skipped_when_its_column_is_null() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Extra", typeof(int), true)];
        var present = Rows.ParseOne<SkippingMember>(cols, 1, 5);
        Assert.Equal(5, present.Extra);
        var absent = Rows.ParseOne<SkippingMember>(cols, 1, DBNull.Value);
        Assert.Equal(1, absent.Id);
        Assert.Null(absent.Extra);
    }

    public struct BagS : IDbReadable {
        public int Extra;
        public int Id;
        [CanCompleteWithMembers]
        public BagS(int id) { Id = id; Extra = 0; }
    }

    [Fact]
    public void A_struct_completes_members_through_its_address() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Extra", typeof(int), false)];
        var bag = Rows.ParseOne<BagS>(cols, 1, 2);
        Assert.Equal(1, bag.Id);
        Assert.Equal(2, bag.Extra);
    }


    public record DocTrack(int Id, string Name, double Weight) : IDbReadable;
    public record DocTrackNullable(int Id, string Name, double? Weight) : IDbReadable;
    public record DocTrackCollapsing(int Id, string Name, [InvalidOnNull] double Weight) : IDbReadable;
    public record DocTrackNotNull(int Id, [System.Diagnostics.CodeAnalysis.NotNull] string Name) : IDbReadable;

    static readonly ColumnInfo[] TrackCols = [
        new("Id", typeof(int), false), new("Name", typeof(string), true), new("Weight", typeof(double), true)];

    /// <summary>
    /// The rule follows the runtime type, so a plain struct slot is what rejects NULL inside an object while
    /// a reference slot takes it, whatever its annotation says.
    /// </summary>
    [Fact]
    public void A_struct_slot_rejects_null_where_a_reference_slot_takes_it() {
        Refusals.Raises(ErrorCodes.NullNotAllowed,
            () => Rows.ParseOne<DocTrack>(TrackCols, 1, "n", DBNull.Value));

        Assert.Null(Rows.ParseOne<DocTrackNullable>(TrackCols, 1, "n", DBNull.Value).Weight);
        Assert.Null(Rows.ParseOne<DocTrack>(TrackCols, 1, DBNull.Value, 2.0).Name);

        ColumnInfo[] idName = [new("Id", typeof(int), false), new("Name", typeof(string), true)];
        Refusals.Raises(ErrorCodes.NullNotAllowed,
            () => Rows.ParseOne<DocTrackNotNull>(idName, 1, DBNull.Value));
    }

    public record DocAlbum([InvalidOnNull] int Id, string Title) : IDbReadable;
    public record DocTrackWithAlbum(int Id, string Name, DocAlbum? Album) : IDbReadable;

    /// <summary>
    /// The slot that identifies the joined object carries
    /// <c>[InvalidOnNull]</c>, so a row that matched nothing leaves the slot null rather than an object of
    /// zeroes.
    /// </summary>
    [Fact]
    public void An_unmatched_outer_join_collapses_to_null_rather_than_zeroes() {
        ColumnInfo[] cols = [
            new("Id", typeof(int), false), new("Name", typeof(string), false),
            new("AlbumId", typeof(int), true), new("AlbumTitle", typeof(string), true)];

        var matched = Rows.ParseOne<DocTrackWithAlbum>(cols, 1, "Song", 7, "Greatest Hits");
        Assert.Equal(7, matched.Album!.Id);
        Assert.Equal("Greatest Hits", matched.Album.Title);

        var unmatched = Rows.ParseOne<DocTrackWithAlbum>(cols, 2, "Demo", DBNull.Value, DBNull.Value);
        Assert.Null(unmatched.Album);
    }

    /// <summary>The stricter rule at the root, where only a null-taking shape survives a NULL.</summary>
    [Fact]
    public void The_root_needs_a_null_taking_shape() {
        ColumnInfo[] one = [new("Name", typeof(string), true)];
        Refusals.Raises(ErrorCodes.NullNotAllowed, () => Rows.ParseOne<string>(one, DBNull.Value));
        Assert.Null(Rows.ParseOne<RinkuLib.TypeAccessing.MaybeNull<string>>(one, DBNull.Value).Value);
    }

    public class Node : IDbReadable {
        public Node(Node inner, int x) { }
    }

    [Fact]
    public void A_self_referential_ctor_registration_stops_instead_of_recursing() {
        TypeParsingInfo.AddOrSet<Node>(CtorTypeInfo.Instance);
        ColumnInfo[] cols = [new("X", typeof(int), false)];
        Refusals.NoParserFor<Node>(() => TypeParser.GetTypeParser<Node>(ref cols));
    }

    public class Picky : IDbReadable {
        public Picky(int a, string b) { }
    }

    [Fact]
    public void A_ctor_registration_missing_a_column_rolls_back_and_fails() {
        TypeParsingInfo.AddOrSet<Picky>(CtorTypeInfo.Instance);
        ColumnInfo[] cols = [new("A", typeof(int), false)];
        Refusals.NoParserFor<Picky>(() => TypeParser.GetTypeParser<Picky>(ref cols));
    }

    public class TwoDoors : IDbReadable {
        public int Got;
        public TwoDoors(int a, string b) => Got = -1;
        [DbConstructor]
        public TwoDoors(int a) => Got = a;
    }

    [Fact]
    public void The_DbConstructor_attribute_pins_the_ctor() {
        TypeParsingInfo.AddOrSet<TwoDoors>(CtorTypeInfo.Instance);
        ColumnInfo[] cols = [new("A", typeof(int), false)];
        var built = Rows.ParseOne<TwoDoors>(cols, 7);
        Assert.Equal(7, built.Got);
    }


    [Fact]
    public void Wrappers_hold_a_whole_list_result() {
        ColumnInfo[] cols = [new("V", typeof(int), false)];
        using var optReader = Rows.Reader(cols, [1], [2]);
        var optParser = TypeParser.GetTypeParser<Optional<List<int>>>(ref cols);
        optReader.Read();
        Assert.Equal([1, 2], optParser.Parse(optReader).Result.Value);

        using var singleReader = Rows.Reader(cols, [3], [4]);
        var singleParser = TypeParser.GetTypeParser<Single<List<int>>>(ref cols);
        singleReader.Read();
        Assert.Equal([3, 4], singleParser.Parse(singleReader).Result.Value);
    }

    [Fact]
    public void A_custom_root_null_rule_reaches_the_wrapped_element() {
        ColumnInfo[] cols = [new("V", typeof(int), true)];
        var parser = TypeParser.GetTypeParser<List<int?>>(ref cols, NullableTypeHandle.Instance);
        Assert.NotNull(parser);
        var maker = new ReusingBaseTypeParserMaker([typeof(List<>)],
            (Type def, Type item, ref object?[] _) => typeof(ListTypeParser<>).MakeGenericType(item));
        Assert.False(maker.TryMakeParser<int>(NotNullHandle.Instance, cols, out _));  
    }

    [Fact]
    public void A_list_of_lists_wraps_the_whole_result_once() {
        ColumnInfo[] cols = [new("V", typeof(int), false)];
        using var reader = Rows.Reader(cols, [1], [2], [3]);
        var parser = TypeParser.GetTypeParser<List<List<int>>>(ref cols);
        reader.Read();
        var nested = parser.Parse(reader).Result;
        Assert.Single(nested);
        Assert.Equal([1, 2, 3], nested[0]);
    }

    public class NoFit : IDbReadable {
        public NoFit(int a, string b) { }
    }

    [Fact]
    public void A_type_with_no_matching_construction_and_no_fallback_fails() {
        ColumnInfo[] cols = [new("A", typeof(int), false)];
        Refusals.NoParserFor<NoFit>(() => TypeParser.GetTypeParser<NoFit>(ref cols));
    }

    public class OpenPocket<T> : IDbReadable {
        public OpenPocket(T value) { }
    }
    public class PlainPocket : IDbReadable {
        public PlainPocket(int value) { }
    }

    [Fact]
    public void GetOrAdd_stores_a_provided_info_on_both_roads() {
        var openInfo = new DefaultTypeParsingInfo(typeof(OpenPocket<>));
        Assert.Same(openInfo, TypeParsingInfo.GetOrAdd(typeof(OpenPocket<int>), openInfo));   
        var plainInfo = new DefaultTypeParsingInfo(typeof(PlainPocket));
        Assert.Same(plainInfo, TypeParsingInfo.GetOrAdd(typeof(PlainPocket), plainInfo));       
    }


    [Fact]
    public void Flag_updater_singletons_carry_their_flags() {
        Assert.Equal(UsageFlags.CanReuse, FlagUpdater.CanReuse.Flags);
        Assert.Equal(UsageFlags.SequentialRead, FlagUpdater.SequentialRead.Flags);
        Assert.Equal(UsageFlags.RemoveSequentialRead, FlagUpdater.RemoveSequentialRead.Flags);
        Assert.Equal(UsageFlags.CanReuse | UsageFlags.SequentialRead, FlagUpdater.CanReuseAndSequential.Flags);
        Assert.Equal(UsageFlags.CanReuse | UsageFlags.RemoveSequentialRead, FlagUpdater.CanReuseAndRemoveSequential.Flags);

        var mod = new ColModifier();
        FlagUpdater.CanReuse.UpdateColModifier(ref mod);
        Assert.True(mod.Flags.HasFlag(UsageFlags.CanReuse));
        FlagUpdater.SequentialRead.EnterSubtree(ref mod, 3);
        Assert.Equal(3, mod.SwapFirstAt);
        Assert.Equal(UsageFlags.SequentialRead, mod.SwapFirstFlags);
        var subtree = new FlagUpdater(UsageFlags.SequentialRead, Subtree: true);
        var mod2 = new ColModifier();
        subtree.EnterSubtree(ref mod2, 0);
        Assert.True(mod2.Flags.HasFlag(UsageFlags.SequentialRead));
    }


    class MakerStub(Func<INameComparer, INameComparer> make) : INameComparerMaker {
        public INameComparer MakeComparer(Type type, ref INameComparer defaultComparer, object[] attributes, object? param)
            => make(defaultComparer);
    }

    [Fact]
    public void Comparer_dispatch_counts_the_surviving_pieces() {
        List<INameComparerMaker> noOp = [new MakerStub(_ => NoNameComparer.Instance)];
        Assert.Same(NoNameComparer.Instance,
            ParamInfo.DispatchComparer(typeof(int), null, [], [], null, noOp));

        var single = ParamInfo.DispatchComparer(typeof(int), "N", [], [], null, noOp);
        Assert.IsType<NameComparer>(single);
        Assert.True(single.Contains("N"));

        List<INameComparerMaker> two = [
            new MakerStub(_ => new NameComparer("X")),
            new MakerStub(_ => new NameComparer("Y")),
        ];
        var grouped = ParamInfo.DispatchComparer(typeof(int), "N", [], [], null, two);
        Assert.IsType<NameComparerGroup>(grouped);
        Assert.True(grouped.Contains("X"));
        Assert.True(grouped.Contains("Y"));
        Assert.True(grouped.Contains("N"));
    }
}
