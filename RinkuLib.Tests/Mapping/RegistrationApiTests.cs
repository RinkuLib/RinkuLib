using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>
/// The registration surface: members and construction paths added by hand are validated on entry,
/// merged with discovery, and honored by the emitted parser.
/// </summary>
public class RegistrationApiTests {
    static ParamInfo P<T>(string name) => ParamInfo.Create(typeof(T), name, []);


    class MemberHost {
        public int Plain;
        public readonly int ReadOnlyField = 1;
        public const int ConstField = 2;
        public static int StaticField;
        public int Settable { get; set; }
        public int GetOnly { get; }
        public int PrivateSet { get; private set; }
        public static int StaticProp { get; set; }
        public void SetIt(int value) => Plain = value;
        public int NotASetter(int value) => value;
        public void TwoArgs(int a, int b) { }
        public static void StaticSet(MemberHost host, int value) => host.Plain = value;
        public static void StaticOneArg(int value) { }
        public void GenericInstance<T>(T value) { }
    }
    static class GenericHost<T> {
        public static void Set(GenericBox<T> host, T value) { }
    }
    class GenericBox<T>;
    static class WrongGenericHost {
        public static void Set<T>(int host, T value) { }
        public static void SetMismatch<T, U>(GenericBox<T> host, U value) { }
    }

    static MemberInfo M(string name) => typeof(MemberHost).GetMember(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)[0];

    [Fact]
    public void A_writable_field_property_and_setter_methods_are_valid_members() {
        Assert.True(MemberParser.TryNew(M("Plain"), P<int>("Plain"), out var f));
        Assert.Equal(typeof(MemberHost), f.TargetType);
        Assert.True(MemberParser.TryNew(M("Settable"), P<int>("Settable"), out _));
        Assert.True(MemberParser.TryNew(M("SetIt"), P<int>("value"), out _));
        Assert.True(MemberParser.TryNew(M("StaticSet"), P<int>("value"), out var s));
        Assert.Equal(typeof(MemberHost), s.TargetType);
    }

    [Fact]
    public void A_generic_static_setter_must_mirror_the_instance_generics() {
        var ok = typeof(GenericHost<>).GetMethod("Set")!;
        Assert.True(MemberParser.TryNew(ok, ParamInfo.Create(ok.GetParameters()[1].ParameterType, "value", []), out _));

        var nonGenericTarget = typeof(WrongGenericHost).GetMethod("Set")!;
        Assert.False(MemberParser.TryNew(nonGenericTarget, P<int>("value"), out _));

        var mismatch = typeof(WrongGenericHost).GetMethod("SetMismatch")!;
        Assert.False(MemberParser.TryNew(mismatch, ParamInfo.Create(mismatch.GetParameters()[1].ParameterType, "value", []), out _));
    }

    [Fact]
    public void Invalid_members_are_rejected_with_the_reason() {
        Assert.False(MemberParser.TryNew(M("ReadOnlyField"), P<int>("x"), out _));
        Assert.False(MemberParser.TryNew(M("ConstField"), P<int>("x"), out _));
        Assert.False(MemberParser.TryNew(M("StaticField"), P<int>("x"), out _));
        Assert.False(MemberParser.TryNew(M("GetOnly"), P<int>("x"), out _));
        Assert.False(MemberParser.TryNew(M("PrivateSet"), P<int>("x"), out _));
        Assert.False(MemberParser.TryNew(M("StaticProp"), P<int>("x"), out _));
        Assert.False(MemberParser.TryNew(M("NotASetter"), P<int>("x"), out _));  
        Assert.False(MemberParser.TryNew(M("TwoArgs"), P<int>("x"), out _));    
        Assert.False(MemberParser.TryNew(M("StaticOneArg"), P<int>("x"), out _)); 
        Assert.False(MemberParser.TryNew(M("GenericInstance"), P<int>("x"), out _));
        Assert.False(MemberParser.TryNew(typeof(MemberHost).GetConstructors()[0], P<int>("x"), out _));
        Assert.False(MemberParser.TryNew(M("Plain"), P<string>("Plain"), out _)); 
        Assert.Throws<ArgumentException>(() => new MemberParser(M("ReadOnlyField"), P<int>("x")));
    }


    record Payment(int Id, string Method) : IDbReadable {
        public static Payment FromCode(int code) => new(code, "code");
        public static Payment Generic<T>(T seed) => new(0, "no");
        public static int WrongReturn(int x) => x;
        public void NotStatic(int x) { }
    }
    class GenericPay<T> : IDbReadable {
        public GenericPay(T value) { }
        public static GenericPay<TVal> Make<TVal>(TVal value) => new(value);
        public static GenericPay<int> Half<TVal>(TVal value) => new(0);
    }

    static ConstructorInfo Ctor => typeof(Payment).GetConstructors()[0];

    [Fact]
    public void A_ctor_and_a_matching_factory_validate() {
        Assert.True(MethodCtorInfo.TryNew(Ctor, out var mci));
        Assert.Equal(typeof(Payment), mci.TargetType);
        Assert.True(MethodCtorInfo.TryNew(typeof(Payment).GetMethod("FromCode")!, out var factory));
        Assert.Equal(typeof(Payment), factory.TargetType);
        Assert.Equal("FromCode(Int32)", factory.ToString());
    }

    [Fact]
    public void A_generic_factory_must_mirror_its_return_generics() {
        var make = typeof(GenericPay<>).GetMethod("Make")!;
        Assert.True(MethodCtorInfo.TryNew(make, out _));
        var half = typeof(GenericPay<>).GetMethod("Half")!;
        Assert.False(MethodCtorInfo.TryNew(half, out _));
    }

    [Fact]
    public void Invalid_construction_paths_are_rejected() {
        Assert.NotNull(MethodCtorInfo.Validate(Ctor, null));
        Assert.NotNull(MethodCtorInfo.Validate(Ctor, []));
        Assert.NotNull(MethodCtorInfo.Validate(Ctor, [P<int>("Id")]));
        Assert.NotNull(MethodCtorInfo.Validate(Ctor, [P<int>("Id"), P<int>("Method")]));
        Assert.NotNull(MethodCtorInfo.ValidateMethodReturn(typeof(Payment).GetMethod("NotStatic")!));
        Assert.NotNull(MethodCtorInfo.ValidateMethodReturn(typeof(Payment).GetMethod("Generic")!));
        Assert.Null(MethodCtorInfo.ValidateMethodReturn(typeof(Payment).GetMethod("WrongReturn")!));
        Assert.Throws<Exception>(() => new MethodCtorInfo(Ctor, []));
    }

    [Fact]
    public void Flags_come_from_attributes_or_are_passed_explicitly() {
        var flagged = typeof(FlaggedType).GetConstructors()[0];
        var mci = new MethodCtorInfo(flagged);
        Assert.True(mci.CanCompleteWithMembers);
        Assert.True(mci.ParametersAreReadable);

        Assert.True(MethodCtorInfo.TryNew(Ctor, MethodCtorInfo.TryMakeParameters(Ctor),
            MethodCtorInfo.AdditionalFlags.CanCompleteWithMembers, out var explicitFlags));
        Assert.True(explicitFlags.CanCompleteWithMembers);
        Assert.False(explicitFlags.ParametersAreReadable);
        Assert.False(MethodCtorInfo.TryNew(Ctor, null, default, out _));

        var viaCtor = new MethodCtorInfo(flagged, MethodCtorInfo.TryMakeParameters(flagged)!, default);
        Assert.False(viaCtor.CanCompleteWithMembers);
    }

    class FlaggedType : IDbReadable {
        [CanCompleteWithMembers, AreReadable]
        public FlaggedType(int a) { }
    }

    [Fact]
    public void Identical_signatures_are_the_same_and_wider_ones_are_more_specific() {
        var a = new MethodCtorInfo(Ctor);
        var b = new MethodCtorInfo(Ctor);
        Assert.True(a.IsSameSignature(b));

        var factory = new MethodCtorInfo(typeof(Payment).GetMethod("FromCode")!);
        Assert.False(a.IsSameSignature(factory));

        Assert.True(a.IsMoreSpecific(factory));   
        Assert.False(a.IsMoreSpecific(a));       
        Assert.True(a.IsMoreSpecific(null!));
        Assert.False(factory.IsMoreSpecific(a));
    }

    class Animal : IDbReadable;
    class Dog : Animal;
    record Kennel(Animal Resident) : IDbReadable {
        public static Kennel OfDog(Dog resident) => new(resident);
    }

    [Fact]
    public void A_derived_parameter_type_is_more_specific() {
        var broad = new MethodCtorInfo(typeof(Kennel).GetConstructors()[0]);
        var narrow = new MethodCtorInfo(typeof(Kennel).GetMethod("OfDog")!);
        Assert.True(narrow.IsMoreSpecific(broad));
        Assert.False(broad.IsMoreSpecific(narrow));

        var ordered = MethodCtorInfo.GetOrderedInfos([broad, narrow]);
        Assert.Same(narrow, ordered[0]);
        Assert.Same(broad, ordered[1]);
    }

    [Fact]
    public void InsertInto_replaces_a_same_signature_entry_and_orders_new_ones() {
        var broad = new MethodCtorInfo(typeof(Kennel).GetConstructors()[0]);
        var narrow = new MethodCtorInfo(typeof(Kennel).GetMethod("OfDog")!);

        var arr = new[] { broad };
        var replacement = new MethodCtorInfo(typeof(Kennel).GetConstructors()[0]);
        replacement.InsertInto(ref arr);
        Assert.Single(arr);
        Assert.Same(replacement, arr[0]);

        narrow.InsertInto(ref arr);
        Assert.Equal(2, arr.Length);
        Assert.Same(narrow, arr[0]);
        Assert.Same(replacement, arr[1]);

        var again = new MethodCtorInfo(typeof(Kennel).GetMethod("OfDog")!);
        again.InsertInto(ref arr);
        Assert.Equal(2, arr.Length);
        Assert.Same(again, arr[0]);
    }


    record RegistryProbe(int A) : IDbReadable;
    record UnregisteredPlain(int A);

    [Fact]
    public void The_registry_resolves_bases_readables_arrays_and_open_generics() {
        Assert.True(TypeParsingInfo.IsUsableType(typeof(int)));
        Assert.True(TypeParsingInfo.IsUsableType(typeof(DayOfWeek)));
        Assert.True(TypeParsingInfo.IsUsableType(typeof(int?)));
        Assert.True(TypeParsingInfo.IsUsableType(typeof(byte[])));
        Assert.True(TypeParsingInfo.IsUsableType(typeof(RegistryProbe)));
        Assert.True(TypeParsingInfo.IsUsableType(typeof((int, string))));
        Assert.False(TypeParsingInfo.IsUsableType(typeof(UnregisteredPlain)));

        Assert.True(TypeParsingInfo.TryGetInfo(typeof(RegistryProbe), out var info));
        Assert.Same(info, TypeParsingInfo.Get(typeof(RegistryProbe)));
        Assert.True(TypeParsingInfo.IsUsableType(typeof(RegistryProbe)));
        Assert.Same(info, TypeParsingInfo.GetOrAdd<RegistryProbe>());
        Assert.NotNull(TypeParsingInfo.Get(typeof((int, string))));
        Assert.Null(TypeParsingInfo.Get(typeof(UnregisteredPlain)));
        Assert.False(TypeParsingInfo.TryGetInfo(typeof(UnregisteredPlain), out _));
        Assert.NotNull(TypeParsingInfo.Get(typeof(int?)));          
    }

    [Fact]
    public void AddOrSet_validates_the_type_against_the_info() {
        Assert.Throws<ArgumentException>(() => TypeParsingInfo.AddOrSet(typeof(RegistryProbe), new DefaultTypeParsingInfo(typeof(Payment))));
    }


    class Assembled : IDbReadable {
        public int Id { get; set; }
        public int Hidden;
        internal Assembled() { }
    }

    [Fact]
    public void A_member_added_before_discovery_survives_the_merge() {
        var info = new DefaultTypeParsingInfo(typeof(Assembled));
        Assert.True(info.AddMember(typeof(Assembled).GetField("Hidden")!));
        var members = ((ICanProvideMembers)info).AvailableMembers; 
        Assert.Equal(3, members.Length);
        Assert.Equal("Hidden", members[0].Member.Name);          
        foreach (var m in members)
            Assert.NotNull(m.Param);
    }

    [Fact]
    public void The_construction_and_member_sets_can_be_replaced_wholesale_after_discovery() {
        var info = new DefaultTypeParsingInfo(typeof(Payment));
        Assert.Equal(2, ((ICanProvideConstructions)info).PossibleConstructors.Length);
        var ctor = new MethodCtorInfo(Ctor);
        ((ICanProvideConstructions)info).PossibleConstructors = new[] { ctor };
        Assert.Equal(1, ((ICanProvideConstructions)info).PossibleConstructors.Length);

        Assert.True(MemberParser.TryNew(typeof(Assembled).GetProperty("Id")!, P<int>("Id"), out var member));
        var other = new DefaultTypeParsingInfo(typeof(Assembled));
        _ = ((ICanProvideMembers)other).AvailableMembers;                          
        ((ICanProvideMembers)other).AvailableMembers = new[] { member };
        Assert.Equal(1, ((ICanProvideMembers)other).AvailableMembers.Length);
    }

    [Fact]
    public void Replacing_with_a_foreign_target_throws() {
        var info = new DefaultTypeParsingInfo(typeof(Assembled));
        var foreignCtor = new MethodCtorInfo(Ctor);
        Assert.Throws<InvalidOperationException>(() => ((ICanProvideConstructions)info).PossibleConstructors = new[] { foreignCtor });

        Assert.True(MemberParser.TryNew(typeof(Assembled).GetProperty("Id")!, P<int>("Id"), out var member));
        var payInfo = new DefaultTypeParsingInfo(typeof(Payment));
        Assert.Throws<InvalidOperationException>(() => ((ICanProvideMembers)payInfo).AvailableMembers = new[] { member });

        Assert.Throws<Exception>(() => info.AddPossibleConstruction(foreignCtor));
        Assert.Throws<InvalidOperationException>(() => payInfo.AddMember(member));
        Assert.Throws<ArgumentException>(() => info.ValidateCanUseType(typeof(Payment)));
    }

    [Fact]
    public void Capability_helpers_refuse_infos_without_the_capability() {
        Assert.False(BaseTypeInfo.Instance.AddMember(typeof(Assembled).GetField("Hidden")!));
        Assert.False(BaseTypeInfo.Instance.AddPossibleConstruction(Ctor));
        Assert.False(BaseTypeInfo.Instance.UpdateAltName(c => c));
        Assert.False(BaseTypeInfo.Instance.SetInvalidOnNull("x", true));
        BaseTypeInfo.Instance.ValidateCanUseType(typeof(int));
        Assert.ThrowsAny<Exception>(() => BaseTypeInfo.Instance.ValidateCanUseType(typeof(Assembled)));
        CtorTypeInfo.Instance.ValidateCanUseType(typeof((int, int)));
        Assert.ThrowsAny<Exception>(() => CtorTypeInfo.Instance.ValidateCanUseType(typeof(Assembled)));
        DynaObjectTypeInfo.Instance.ValidateCanUseType(typeof(DynaObject));
        Assert.ThrowsAny<Exception>(() => DynaObjectTypeInfo.Instance.ValidateCanUseType(typeof(Assembled)));
    }

    record Renamed(int First, int Second) : IDbReadable;

    [Fact]
    public void Alt_names_and_null_rules_reach_every_slot_through_the_helper() {
        var info = TypeParsingInfo.GetOrAdd<Renamed>();
        Assert.True(info.UpdateAltName(c => c.Contains("First") ? c.AddAltName("Uno") : null));
        Assert.True(info.SetInvalidOnNull("Second", true));
        Assert.True(info.SetInvalidOnNull(p => p.NameComparer.Contains("Second") ? false : null));
        Assert.True(info.UpdateNullColHandler("First", NotNullHandle.Instance));
        Assert.True(info.UpdateNullColHandler(p => null));

        ColumnInfo[] cols = [new("Uno", typeof(int), false), new("Second", typeof(int), false)];
        var parsed = Rows.ParseOne<Renamed>(cols, 7, 8);
        Assert.Equal(7, parsed.First);
        Assert.Equal(8, parsed.Second);
    }

    class SetterAssembled : IDbReadable {
        public int Stored;
        public void Push(int stored) => Stored = stored;
    }

    [Fact]
    public void A_setter_method_registered_as_member_fills_after_construction() {
        var info = TypeParsingInfo.GetOrAdd<SetterAssembled>();
        Assert.True(info.AddMember(typeof(SetterAssembled).GetMethod("Push")!));
        ColumnInfo[] cols = [new("stored", typeof(int), false)];
        var parsed = Rows.ParseOne<SetterAssembled>(cols, 42);
        Assert.Equal(42, parsed.Stored);
    }

    [Fact]
    public void Registering_an_invalid_member_reports_the_reason() {
        var info = TypeParsingInfo.GetOrAdd<SetterAssembled>();
        Assert.Throws<ArgumentException>(() => info.AddMember(typeof(SetterAssembled).GetConstructors()[0]));
        Assert.Throws<ArgumentException>(() => info.AddMember(typeof(UnusableHolder).GetProperty("Value")!));
    }

    class UnusableHolder : IDbReadable {
        public UnregisteredPlain? Value { get; set; }
    }


    class AttributedMembers {
        public void ByParam([Alt("Other")] int value) { }
        [Alt("One")]
        [Alt("Two")]
        public int ManyAlts { get; set; }
        [NoName]
        public int Anonymous { get; set; }
        [NoName, Alt("OnlyAlt")]
        public int AltOnly { get; set; }
        [NoName, Alt("A"), Alt("B")]
        public int AltsOnly { get; set; }
        [NotNull]
        public string? SwearsNotNull { get; set; }
        [InvalidOnNull]
        public int? RowInvalidOnNull { get; set; }
        public UnregisteredPlain? Unusable { get; set; }
        public UnregisteredPlain? UnusableField;
    }

    [Fact]
    public void ParamInfo_factories_read_names_alts_and_null_rules_from_attributes() {
        var byParam = ParamInfo.TryNew(typeof(AttributedMembers).GetMethod("ByParam")!.GetParameters()[0]);
        Assert.NotNull(byParam);
        Assert.True(byParam.NameComparer.Contains("value"));
        Assert.True(byParam.NameComparer.Contains("Other"));

        var many = ParamInfo.TryNew(typeof(AttributedMembers).GetProperty("ManyAlts")!);
        Assert.NotNull(many);
        Assert.True(many.NameComparer.Contains("One"));
        Assert.True(many.NameComparer.Contains("Two"));
        Assert.True(many.NameComparer.Contains("ManyAlts"));

        var anonymous = ParamInfo.TryNew(typeof(AttributedMembers).GetProperty("Anonymous")!);
        Assert.Same(NoNameComparer.Instance, anonymous!.NameComparer);

        var altOnly = ParamInfo.TryNew(typeof(AttributedMembers).GetProperty("AltOnly")!);
        Assert.False(altOnly!.NameComparer.Contains("AltOnly"));
        Assert.True(altOnly.NameComparer.Contains("OnlyAlt"));

        var altsOnly = ParamInfo.TryNew(typeof(AttributedMembers).GetProperty("AltsOnly")!);
        Assert.True(altsOnly!.NameComparer.Contains("A"));
        Assert.True(altsOnly.NameComparer.Contains("B"));
        Assert.False(altsOnly.NameComparer.Contains("AltsOnly"));

        Assert.Same(NotNullHandle.Instance, ParamInfo.TryNew(typeof(AttributedMembers).GetProperty("SwearsNotNull")!)!.NullColHandler);
        Assert.Null(ParamInfo.TryNew(typeof(AttributedMembers).GetProperty("Unusable")!));
        Assert.Null(ParamInfo.TryNew(typeof(AttributedMembers).GetField("UnusableField")!));
        Assert.Null(ParamInfo.TryNew(typeof(GenericPay<UnregisteredPlain>).GetConstructors()[0].GetParameters()[0]));
    }

    [Fact]
    public void Declared_null_handlers_compose_with_invalid_on_null() {
        Assert.Null(ParamInfo.GetDeclaredNullColHandler(typeof(int), "x", []));
        Assert.Same(NotNullHandle.Instance, ParamInfo.GetDeclaredNullColHandler(typeof(string), "x", [new NotNullAttribute()]));
        Assert.Same(NullableTypeHandle.Instance, ParamInfo.GetDeclaredNullColHandler(typeof(string), "x", [new MaybeNullAttribute()]));
        Assert.NotNull(ParamInfo.GetDeclaredNullColHandler(typeof(int?), "x", [new InvalidOnNullAttribute()]));
        Assert.NotNull(ParamInfo.GetDeclaredNullColHandler(typeof(int), "x", [new InvalidOnNullAttribute()]));
        Assert.NotNull(ParamInfo.GetDeclaredNullColHandler(typeof(int), "x", [new InvalidOnNullAttribute(), new NotNullAttribute()]));

        var invalidOnNull = ParamInfo.TryNew(typeof(AttributedMembers).GetProperty("RowInvalidOnNull")!);
        Assert.NotNull(invalidOnNull);

        var p = P<int?>("x");
        var before = p.NullColHandler;
        p.SetInvalidOnNull(true);
        Assert.NotSame(before, p.NullColHandler);
    }

    [Fact]
    public void A_true_name_attribute_overrides_the_declared_name() {
        var p = ParamInfo.Create(typeof(int), "original", [new TrueNameAttribute("Actual")]);
        Assert.True(p.NameComparer.Contains("Actual"));
        Assert.False(p.NameComparer.Contains("original"));
        Assert.True(INameComparer.TryGetTrueName(new TrueNameAttribute("N"), out var name));
        Assert.Equal("N", name);
        Assert.False(INameComparer.TryGetTrueName(new NoNameAttribute(), out _));
        Assert.False(INameComparer.TryGetTrueName(new TrueNameAttribute(null!), out _));
    }

    public sealed class TrueNameAttribute(string? name) : Attribute {
        public string? Name { get; } = name;
    }


    class GenericDonor<T> {
        public static void Give(Assembled host, int value) => host.Hidden = value;
        public static Payment Make(int id) => new(id, "gen");
    }

    [Fact]
    public void A_member_or_construction_from_a_foreign_generic_type_is_refused() {
        var give = typeof(GenericDonor<int>).GetMethod("Give")!;
        Assert.True(MemberParser.TryNew(give, P<int>("value"), out var member));
        var info = new DefaultTypeParsingInfo(typeof(Assembled));
        Assert.Throws<Exception>(() => info.AddMember(member));
        Assert.ThrowsAny<Exception>(() => ((ICanProvideMembers)info).AvailableMembers = new[] { member });

        var make = new MethodCtorInfo(typeof(GenericDonor<int>).GetMethod("Make")!);
        var payInfo = new DefaultTypeParsingInfo(typeof(Payment));
        Assert.Throws<Exception>(() => payInfo.AddPossibleConstruction(make));
        Assert.ThrowsAny<Exception>(() => ((ICanProvideConstructions)payInfo).PossibleConstructors = new[] { make });
    }

    class OpenBox<T> : IDbReadable {
        public OpenBox(T value) { Value = value; }
        public T Value;
        public static OpenBox<TVal> Wrap<TVal>(TVal value) => new(value);
    }

    [Fact]
    public void An_open_generic_info_accepts_a_factory_returning_its_closed_form() {
        var info = new DefaultTypeParsingInfo(typeof(OpenBox<>));
        info.AddPossibleConstruction(new MethodCtorInfo(typeof(OpenBox<>).GetMethod("Wrap")!));
        Assert.Contains(((ICanProvideConstructions)info).PossibleConstructors.ToArray(),
            c => c.MethodBase.Name == "Wrap");
    }

    [Fact]
    public void A_construction_added_before_discovery_survives_the_merge() {
        var info = new DefaultTypeParsingInfo(typeof(Payment));
        info.AddPossibleConstruction(new MethodCtorInfo(typeof(Payment).GetMethod("FromCode")!));
        var ctors = ((ICanProvideConstructions)info).PossibleConstructors; 
        Assert.Equal(3, ctors.Length);
        foreach (var c in ctors)
            Assert.NotNull(c.MethodBase);
    }


    class Mixed : IDbReadable {
        public int Field;
        public virtual int Virt { get; set; }
        public int Stored;
        public static void Deposit(Mixed host, int stored) => host.Stored = stored;
    }

    static class SwappedGenerics {
        public static void Set<T, U>(GenericPair<U, T> host, U value) { }
    }
    class GenericPair<T, U>;

    [Fact]
    public void A_generic_setter_with_reordered_type_parameters_is_rejected() {
        var swapped = typeof(SwappedGenerics).GetMethod("Set")!;
        Assert.False(MemberParser.TryNew(swapped,
            ParamInfo.Create(swapped.GetParameters()[1].ParameterType, "value", []), out _));
    }

    static class FactoryHost {
        public static int GenericToPlain<T>(T x) => 0;
        public static List<int> ClosedReturn() => [];
        public static Dictionary<TKey, int> HalfGenerics<TKey>(TKey k) where TKey : notnull => [];
    }

    [Fact]
    public void Factory_return_generics_must_mirror_the_method_generics() {
        Assert.NotNull(MethodCtorInfo.ValidateMethodReturn(typeof(FactoryHost).GetMethod("GenericToPlain")!));
        Assert.NotNull(MethodCtorInfo.ValidateMethodReturn(typeof(FactoryHost).GetMethod("ClosedReturn")!));
        Assert.NotNull(MethodCtorInfo.ValidateMethodReturn(typeof(FactoryHost).GetMethod("HalfGenerics")!));
    }

    [Fact]
    public void InsertInto_orders_around_more_and_less_specific_entries() {
        var broad = new MethodCtorInfo(typeof(Kennel).GetConstructors()[0]);
        var narrow = new MethodCtorInfo(typeof(Kennel).GetMethod("OfDog")!);

        var arr = new[] { narrow };
        broad.InsertInto(ref arr);                    
        Assert.Same(narrow, arr[0]);
        Assert.Same(broad, arr[1]);

        var unordered = new[] { broad, narrow };
        var narrow2 = new MethodCtorInfo(typeof(Kennel).GetMethod("OfDog")!);
        narrow2.InsertInto(ref unordered);          
        Assert.Equal(2, unordered.Length);
        Assert.Same(narrow2, unordered[0]);
        Assert.Same(broad, unordered[1]);
    }

    [Fact]
    public void The_helper_overloads_taking_built_pieces_reach_the_capability() {
        var info = new DefaultTypeParsingInfo(typeof(Assembled));
        Assert.True(MemberParser.TryNew(typeof(Assembled).GetProperty("Id")!, P<int>("Id"), out var member));
        Assert.True(((TypeParsingInfo)info).AddMember(member));
        Assert.False(BaseTypeInfo.Instance.AddMember(member));

        var payInfo = new DefaultTypeParsingInfo(typeof(Payment));
        var mci = new MethodCtorInfo(Ctor);
        Assert.True(((TypeParsingInfo)payInfo).AddPossibleConstruction(mci));
        Assert.False(BaseTypeInfo.Instance.AddPossibleConstruction(mci));
    }

    [Fact]
    public void GetOrAdd_validates_a_provided_info_on_the_generic_road() {
        Assert.Throws<ArgumentException>(() =>
            TypeParsingInfo.GetOrAdd(typeof(GenericPay<Guid>), new DefaultTypeParsingInfo(typeof(Payment))));
        Assert.True(TypeParsingInfo.IsUsableType(typeof(RegistryProbe[])));  
    }

    [Fact]
    public void Field_virtual_property_and_static_setter_members_fill_after_construction() {
        var info = TypeParsingInfo.GetOrAdd<Mixed>();
        Assert.True(info.AddMember(typeof(Mixed).GetMethod("Deposit")!));
        ColumnInfo[] cols = [
            new("Field", typeof(int), false),
            new("Virt", typeof(int), false),
            new("stored", typeof(int), false),
        ];
        var parsed = Rows.ParseOne<Mixed>(cols, 1, 2, 3);
        Assert.Equal(1, parsed.Field);
        Assert.Equal(2, parsed.Virt);
        Assert.Equal(3, parsed.Stored);
    }
}
