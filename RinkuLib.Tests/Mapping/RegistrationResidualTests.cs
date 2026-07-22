using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>
/// The registration and emission roads a plain mapping never reaches: forced registration of a
/// constructor's parameter types, the seams a custom info or attribute plugs into, and the recovery
/// emission a deeply nested null jump needs.
/// </summary>
public class RegistrationResidualTests {

    public class Unregistered {
        public int A;
        public Unregistered(int a) => A = a;
    }
    public class NeverRegisteredA {
        public int A;
        public NeverRegisteredA(int a) => A = a;
    }
    public class NeverRegisteredB {
        public int A;
        public NeverRegisteredB(int a) => A = a;
    }

    public class OpenHolder<T> : IDbReadable {
        public T Value;
        [AreReadable]
        public OpenHolder([NoName] T value) => Value = value;
    }

    public class PlainOpenHolder<T> : IDbReadable {
        public T Value;
        public PlainOpenHolder([NoName] T value) => Value = value;
    }

    [Fact]
    public void AreReadable_registers_a_parameter_type_that_was_not_registered() {
        ColumnInfo[] cols = [new("A", typeof(int), false)];
        var built = Rows.ParseOne<OpenHolder<Unregistered>>(cols, 5);
        Assert.Equal(5, built.Value.A);
    }

    [Fact]
    public void Without_AreReadable_an_unregistered_parameter_type_fails_the_candidate() {
        ColumnInfo[] cols = [new("A", typeof(int), false)];
        Refusals.NoParserFor<PlainOpenHolder<NeverRegisteredA>>(
            () => Rows.ParseOne<PlainOpenHolder<NeverRegisteredA>>(cols, 5));
    }

    public class NothingMatches : IDbReadable {
        public int Unrelated { get; set; }
    }

    [Fact]
    public void A_shape_whose_members_match_no_column_yields_no_parser() {
        ColumnInfo[] cols = [new("Totally", typeof(int), false)];
        Refusals.NoParserFor<NothingMatches>(() => Rows.ParseOne<NothingMatches>(cols, 1));
    }


    [Fact]
    public void A_null_jump_without_a_label_is_refused() {
        var point = default(NullSetPoint);
        Assert.False(point.HasValue);
        var method = new DynamicMethod("nj", typeof(void), Type.EmptyTypes);
        var gen = Wrap(method.GetILGenerator());
        Refusals.Raises(ErrorCodes.InternalInvariant, () => point.MakeNullJump(gen));
    }

    static Generator Wrap(ILGenerator il) =>
#if DEBUG
        new(il, []);
#else
        new(il);
#endif


    [Fact]
    public void A_plan_can_have_its_modifier_and_fallback_replaced() {
        var plan = new ParamInfoPlus(typeof(int), NotNullHandle.Instance, new NameComparer("A"),
            IColModifier.Nothing, IFallbackParserGetter.Nothing);
        plan.ColModifier = FlagUpdater.CanReuse;
        Assert.Same(FlagUpdater.CanReuse, plan.ColModifier);
        plan.FallbackParserGetter = DefaultValueFallback.Instance;
        Assert.Same(DefaultValueFallback.Instance, plan.FallbackParserGetter);

        var mod = new ColModifier();
        plan.UpdateColModifier(ref mod);
        Assert.True(mod.Flags.HasFlag(UsageFlags.CanReuse));
        Assert.Null(IFallbackParserGetter.Nothing.FallbackTryGetParser(typeof(int)));
    }

    sealed class CustomHandlerAttribute : Attribute, INullColHandlerMaker {
        public INullColHandler MakeColHandler(Type type, string? name, object[] attributes, object? param)
            => NullableTypeHandle.Instance;
    }

    sealed class CustomPlanAttribute : Attribute, IParamInfoMaker {
        public ParamInfo MakeMatcher(Type Type, INullColHandler NullColHandler, INameComparer NameComparer,
            string? name, object[] attributes, UsageFlags usageFlags, object? param)
            => new ParamInfo(Type, NullColHandler, new NameComparer("Renamed"));
    }

    class AttributeSeams {
        [CustomHandler] public int? ViaMaker { get; set; }
        [CustomPlan] public int ViaPlan { get; set; }
        [System.Diagnostics.CodeAnalysis.NotNull] public string? Sworn { get; set; }
    }

    [Fact]
    public void A_custom_attribute_can_supply_the_null_rule_or_the_whole_plan() {
        var viaMaker = ParamInfo.TryNew(typeof(AttributeSeams).GetProperty("ViaMaker")!);
        Assert.Same(NullableTypeHandle.Instance, viaMaker!.NullColHandler);

        var viaPlan = ParamInfo.TryNew(typeof(AttributeSeams).GetProperty("ViaPlan")!);
        Assert.True(viaPlan!.NameComparer.Contains("Renamed"));

        var sworn = ParamInfo.TryNew(typeof(AttributeSeams).GetProperty("Sworn")!);
        Assert.Same(NotNullHandle.Instance, sworn!.NullColHandler);
    }


    sealed class ReshapableInfo : TypeParsingInfo, ICanUpdateAltNames, ICanUpdateNullColHandlers {
        public readonly ParamInfo Slot = ParamInfo.Create(typeof(int), "Slot", []);
        public int AltCalls, NullCalls;
        public override void ValidateCanUseType(Type TargetType) { }
        public void UpdateAltName(Func<INameComparer, INameComparer?> modifier) {
            AltCalls++;
            Slot.NameComparer = modifier(Slot.NameComparer) ?? Slot.NameComparer;
        }
        public void UpdateNullColHandler(Func<ParamInfo, INullColHandler?> modifier) {
            NullCalls++;
            Slot.NullColHandler = modifier(Slot) ?? Slot.NullColHandler;
        }
        public override DbItemParser? TryGetParser(Type currentClosedType, RecursiveInfo previousUsages,
            ParamInfo paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage) => null;
    }

    [Fact]
    public void An_info_that_reshapes_itself_takes_priority_over_slot_enumeration() {
        var info = new ReshapableInfo();
        Assert.True(TypeParsingInfoHelper.UpdateAltName(info, c => c.AddAltName("Other")));
        Assert.Equal(1, info.AltCalls);
        Assert.True(info.Slot.NameComparer.Contains("Other"));

        Assert.True(TypeParsingInfoHelper.UpdateNullColHandler(info, _ => NotNullHandle.Instance));
        Assert.Same(NotNullHandle.Instance, info.Slot.NullColHandler);
        Assert.True(TypeParsingInfoHelper.UpdateNullColHandler(info, "Slot", NullableTypeHandle.Instance));
        Assert.True(TypeParsingInfoHelper.SetInvalidOnNull(info, "Slot", true));
        Assert.True(TypeParsingInfoHelper.SetInvalidOnNull(info, _ => false));
        Assert.Equal(4, info.NullCalls);
    }


    class NeverLookedUp<T>;
    class NeverLookedUpPlain;
    class AddedByDefinition<T>;

    [Fact]
    public void A_generic_whose_definition_is_unregistered_is_not_found() {
        Assert.Null(TypeParsingInfo.Get(typeof(NeverLookedUp<int>)));
        Assert.Null(TypeParsingInfo.Get(typeof(NeverLookedUpPlain)));
        Assert.False(TypeParsingInfo.IsUsableType(typeof(NeverLookedUpPlain)));
    }

    [Fact]
    public void GetOrAdd_returns_the_definition_entry_once_it_exists() {
        var first = TypeParsingInfo.GetOrAdd(typeof(AddedByDefinition<int>));
        var second = TypeParsingInfo.GetOrAdd(typeof(AddedByDefinition<string>));
        Assert.Same(first, second);
        Assert.Same(first, TypeParsingInfo.Get(typeof(AddedByDefinition<long>)));
    }


    public class UnusableParam {
        public UnusableParam(NeverRegisteredB u) { }
    }

    [Fact]
    public void A_constructor_with_an_unusable_parameter_derives_no_matchers() {
        Assert.Null(MethodCtorInfo.TryMakeParameters(typeof(UnusableParam).GetConstructors()[0]));
        Assert.False(MethodCtorInfo.TryNew(typeof(UnusableParam).GetConstructors()[0], out _));
    }


    class InitTwice : IDbReadable {
        public int A { get; set; }
    }

    [Fact]
    public void Discovery_asked_for_twice_runs_once() {
        var info = new DefaultTypeParsingInfo(typeof(InitTwice));
        info.Init();
        var first = ((ICanProvideMembers)info).AvailableMembers.Length;
        info.Init();
        Assert.Equal(first, ((ICanProvideMembers)info).AvailableMembers.Length);
    }

    [Fact]
    public void A_construction_built_with_explicit_flags_still_validates() {
        var ctor = typeof(InitTwice).GetConstructors()[0];
        Refusals.Raises(ErrorCodes.ConstructionShapeNotUsable, () => new MethodCtorInfo(ctor, [], MethodCtorInfo.AdditionalFlags.CanCompleteWithMembers));
    }


    public record StrictTrack(int Id, string Name, int Code) : IDbReadable;

    public class SettableTrack : IDbReadable {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int Code { get; set; }
    }

    static readonly ColumnInfo[] IdName = [new("Id", typeof(int), false), new("Name", typeof(string), false)];

    /// <summary>
    /// The refusal carries the columns the negotiation actually had, so a
    /// query returning the wrong ones and a target that cannot be built from the right ones are told apart
    /// by reading <c>Schema</c> rather than by the message.
    /// </summary>
    [Fact]
    public void The_refusal_carries_the_schema_it_was_offered() {
        var refused = Refusals.NoParserFor<StrictTrack>(() => {
            var cols = IdName;
            TypeParser.GetTypeParser<StrictTrack>(ref cols);
        });
        Assert.Equal("Int32 Id, String Name", refused.Schema);

        ColumnInfo[] unrelated = [new("Total", typeof(decimal), false), new("At", typeof(DateTime), false)];
        var wrongColumns = Refusals.NoParserFor<StrictTrack>(() => {
            var cols = unrelated;
            TypeParser.GetTypeParser<StrictTrack>(ref cols);
        });
        Assert.Equal("Decimal Total, DateTime At", wrongColumns.Schema);
    }

    /// <summary>A parameterless constructor with settable members builds from the same columns.</summary>
    [Fact]
    public void A_parameterless_constructor_gives_the_columns_somewhere_to_land() {
        var built = Rows.ParseOne<SettableTrack>(IdName, 1, "x");
        Assert.Equal(1, built.Id);
        Assert.Equal("x", built.Name);
        Assert.Equal(0, built.Code);
    }

    public record NamedTrack(int Id, string Name) : IDbReadable;
    public record AliasedTrack([Alt("TrackId")] int Id, [Alt("TrackName")] string Name) : IDbReadable;

    /// <summary>
    /// The columns and the type are each fine on their own,
    /// they carry different names.
    /// </summary>
    [Fact]
    public void A_query_and_a_type_that_name_things_differently_cannot_be_linked() {
        ColumnInfo[] prefixed = [new("TrackId", typeof(int), false), new("TrackName", typeof(string), false)];

        var refused = Refusals.NoParserFor<NamedTrack>(() => {
            var cols = prefixed;
            TypeParser.GetTypeParser<NamedTrack>(ref cols);
        });
        Assert.Equal("Int32 TrackId, String TrackName", refused.Schema);

        var aliased = Rows.ParseOne<AliasedTrack>(prefixed, 1, "x");
        Assert.Equal(1, aliased.Id);
        Assert.Equal("x", aliased.Name);
    }

    public record TokenHolder(int Id, Guid Token) : IDbReadable;

    /// <summary>The second pairing. The column's value does not convert to the slot's type.</summary>
    [Fact]
    public void A_column_that_does_not_convert_to_its_slot_cannot_be_linked() {
        ColumnInfo[] asInt = [new("Id", typeof(int), false), new("Token", typeof(int), false)];
        Refusals.NoParserFor<TokenHolder>(() => {
            var cols = asInt;
            TypeParser.GetTypeParser<TokenHolder>(ref cols);
        });

        ColumnInfo[] asGuid = [new("Id", typeof(int), false), new("Token", typeof(Guid), false)];
        var token = Guid.NewGuid();
        Assert.Equal(token, Rows.ParseOne<TokenHolder>(asGuid, 1, token).Token);
    }

    public record UnregisteredArtist(int Id, string Name);
    public record AlbumOverUnregistered(int Id, string Title, UnregisteredArtist Artist) : IDbReadable;
    public record RegisteredArtist(int Id, string Name) : IDbReadable;
    public record AlbumOverRegistered(int Id, string Title, RegisteredArtist Artist) : IDbReadable;

    static readonly ColumnInfo[] AlbumCols = [
        new("Id", typeof(int), false), new("Title", typeof(string), false),
        new("ArtistId", typeof(int), false), new("ArtistName", typeof(string), false)];

    /// <summary>
    /// The third pairing, and the one with no sign of trouble on either side. A type reached only as a slot
    /// has to be registered before the engine will consider it, so without that the outer type is what the
    /// refusal names even though the outer type is fine.
    /// </summary>
    [Fact]
    public void A_nested_type_that_was_never_registered_cannot_be_linked() {
        var refused = Refusals.NoParserFor<AlbumOverUnregistered>(() => {
            var cols = AlbumCols;
            TypeParser.GetTypeParser<AlbumOverUnregistered>(ref cols);
        });
        Assert.Equal(typeof(AlbumOverUnregistered), refused.TargetType);

        var built = Rows.ParseOne<AlbumOverRegistered>(AlbumCols, 1, "t", 2, "a");
        Assert.Equal("a", built.Artist.Name);
    }

    public record ReuseInner(int Shared) : IDbReadable;
    public record ReuseOuter([Alt("InnerShared")] int Taken, [MayReuseColSubtree] ReuseInner Inner) : IDbReadable;
    public record UnmarkedOuter([Alt("InnerShared")] int Taken, ReuseInner Inner) : IDbReadable;

    static readonly ColumnInfo[] SharedOnly = [new("InnerShared", typeof(int), false)];

    [Fact]
    public void A_subtree_marked_reusable_reads_a_column_its_parent_already_took() {
        var parsed = Rows.ParseOne<ReuseOuter>(SharedOnly, 7);
        Assert.Equal(7, parsed.Taken);
        Assert.Equal(7, parsed.Inner.Shared);
    }

    [Fact]
    public void An_unmarked_subtree_cannot_take_the_column_its_parent_took() {
        Refusals.NoParserFor<UnmarkedOuter>(() => Rows.ParseOne<UnmarkedOuter>(SharedOnly, 7));
    }

    [Fact]
    public void A_reusable_subtree_takes_its_own_column_when_the_schema_has_one() {
        ColumnInfo[] cols = [new("Taken", typeof(int), false), new("InnerShared", typeof(int), false)];
        var parsed = Rows.ParseOne<ReuseOuter>(cols, 7, 8);
        Assert.Equal(7, parsed.Taken);
        Assert.Equal(8, parsed.Inner.Shared);
    }


    [Fact]
    public void A_float_column_reads_through_GetFloat() {
        Assert.Equal("GetFloat", typeof(float).GetDbMethod().Name);
        ColumnInfo[] cols = [new("V", typeof(float), false)];
        Assert.Equal(1.5f, Rows.ParseOne<float>(cols, 1.5f));
    }


    [Fact]
    public void An_object_column_reads_through_GetValue() {
        Assert.Equal("GetValue", typeof(object).GetDbMethod().Name);
        ColumnInfo[] cols = [new("V", typeof(object), false)];
        Assert.Equal("boxed", Rows.ParseOne<object>(cols, "boxed"));
    }


    public class GenericTarget<T> {
        public T? Value;
    }
    public static class GenericSetters {
        public static void SetOk<T>(GenericTarget<T> host, T value) => host.Value = value;
    }

    [Fact]
    public void A_generic_setter_matching_its_instance_generics_is_accepted() {
        var ok = typeof(GenericSetters).GetMethod(nameof(GenericSetters.SetOk))!;
        var param = ParamInfo.Create(ok.GetParameters()[1].ParameterType, "value", []);
        Assert.True(MemberParser.TryNew(ok, param, out var member));
        Assert.Equal(typeof(GenericTarget<>), member.TargetType.GetGenericTypeDefinition());
    }


    [Fact]
    public void The_wide_dyna_shape_refuses_a_mismatched_argument_count() {
        var cols = new ColumnInfo[14];
        for (int i = 0; i < cols.Length; i++)
            cols[i] = new($"C{i + 1}", typeof(int), false);
        var row = new object[14];
        for (int i = 0; i < row.Length; i++)
            row[i] = i + 1;

        var dyna = Rows.ParseOne<DynaObject>(cols, row);
        Assert.Equal(14, dyna.Count);
        Assert.Equal(14, dyna.Get<int>(13));

        var mapper = dyna.Mapper;
        var tooFewParams = new DbItemParser[2];
        Refusals.Raises(ErrorCodes.InternalInvariant, () =>
            new DynaObjParserInfinite([], tooFewParams, mapper).Emit(cols, Wrap(new DynamicMethod("x", typeof(void), Type.EmptyTypes).GetILGenerator()), default, out _));
    }


    [Fact]
    public void Closing_an_already_closed_type_returns_it_untouched() {
        Assert.Same(typeof(int), typeof(int).CloseType(typeof(KeyValuePair<int, string>)));
        Assert.Same(typeof(List<int>), typeof(List<int>).CloseType(typeof(KeyValuePair<int, string>)));
    }

    public record RacingRegistrationA(int Id, string Name);
    public record RacingRegistrationB(int Id, string Name);
    public record RacingRegistrationC(int Id, string Name);

    /// <summary>
    /// The registry is process wide, and the info a caller receives is the one the whole app will parse
    /// through. First sight of a type from several threads has to settle on one entry, otherwise a caller
    /// configures an info nothing else consults and its rules vanish without a word.
    /// </summary>
    [Fact]
    public void First_registration_from_several_threads_settles_on_one_entry() {
        Type[] types = [typeof(RacingRegistrationA), typeof(RacingRegistrationB), typeof(RacingRegistrationC)];
        var seen = new System.Collections.Concurrent.ConcurrentBag<(Type Type, TypeParsingInfo Info)>();
        using var barrier = new Barrier(15);

        Parallel.For(0, 15, i => {
            var type = types[i % types.Length];
            barrier.SignalAndWait();
            seen.Add((type, TypeParsingInfo.GetOrAdd(type)));
        });

        foreach (var group in seen.GroupBy(s => s.Type)) {
            var only = Assert.Single(group.Select(s => s.Info).Distinct());
            Assert.Same(only, TypeParsingInfo.GetOrAdd(group.Key));
        }
    }
}
