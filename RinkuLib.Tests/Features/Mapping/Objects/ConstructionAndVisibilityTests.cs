using System.Data;
using Rinku.Mapping.Defaults;
using Rinku.Mapping;
using Rinku.Querying;
using RinkuLib.Tests.Infrastructure;
using Rinku.Internal;
using Xunit;

namespace RinkuLib.Tests.Mapping;

[Collection("GlobalMappingConfiguration")]
public class ConstructionAndVisibilityTests {
    [Fact]
    public void Enum_member_reads_from_a_numeric_column() {
        ColumnInfo[] numeric = [new("EnumEnum", typeof(byte), false)];
        Assert.Equal(ByteEnum.Bla, Rows.ParseOne<WithByteEnum>(numeric, (byte)1).EnumEnum);
    }

    [Fact]
    public void Nullable_enum_member_reads_null() {
        Assert.Null(Rows.ParseOne<WithNullableByteEnum>([new("EnumEnum", typeof(byte), true)], DBNull.Value).EnumEnum);
    }

    [Fact]
    public void Enum_member_reads_from_a_string_column_case_insensitively() {
        Assert.Equal(ByteEnum.Bla, Rows.ParseOne<WithByteEnum>([new("EnumEnum", typeof(string), false)], "bla").EnumEnum);
    }

    [Fact]
    public void Nullable_enum_member_reads_string_and_null() {
        Assert.Equal(ByteEnum.Bla, Rows.ParseOne<WithNullableByteEnum>([new("EnumEnum", typeof(string), true)], "BLA").EnumEnum);
        Assert.Null(Rows.ParseOne<WithNullableByteEnum>([new("EnumEnum", typeof(string), true)], DBNull.Value).EnumEnum);
    }

    [Fact]
    public void Enum_constructor_parameters_read_values_and_null() {
        ColumnInfo[] enumColumns = [new("E1", typeof(short), false), new("N1", typeof(short), true), new("N2", typeof(short), true)];
        var enums = Rows.ParseOne<CtorWithEnums>(enumColumns, (short)2, (short)5, DBNull.Value);
        Assert.Equal(ShortEnum.Two, enums.E);
        Assert.Equal(ShortEnum.Five, enums.NE1);
        Assert.Null(enums.NE2);

    }

    [Fact]
    public void Enum_constructor_parameters_read_case_insensitive_names() {
        Assert.Equal(ByteEnum.Bla, Rows.ParseOne<StringEnumCtor>([new("EnumEnum", typeof(string), false)], "bla").EnumEnum);
    }

    [Fact]
    public void Char_constructor_parameters_read_values_and_null() {
        var chars = Rows.ParseOne<CtorWithChars>([new("C1", typeof(char), false), new("C2", typeof(char), true), new("C3", typeof(char), true)], '\u0105', DBNull.Value, '\u00F3');
        Assert.Equal('\u0105', chars.Char1);
        Assert.Null(chars.Char2);
        Assert.Equal('\u00F3', chars.Char3);
    }

    [Fact]
    public void Inherited_public_members_map_alongside_own_members() {
        var inherited = Rows.ParseOne<DerivedThing>([new("Base", typeof(int), false), new("Derived", typeof(int), false)], 3, 4);
        Assert.Equal((3, 4), (inherited.Base, inherited.Derived));
    }

    [Fact]
    public void Richest_satisfiable_constructor_wins_over_the_parameterless_one() {
        var selected = Rows.ParseOne<TwoCtors>([new("A", typeof(int), false), new("B", typeof(string), false)], 0, "Rinku");
        Assert.Equal((1, "Rinku!"), (selected.A, selected.B));
    }

    [Fact]
    public void Mixed_ctor_and_member_hydration_fills_both() {
        var hydrated = Rows.ParseOne<MixedHydration>([
            new("Id", typeof(int), false), new("Name", typeof(string), false), new("Extra", typeof(int), true)], 1, "n", 5);
        Assert.Equal((1, "n", 5), (hydrated.Id, hydrated.Name, hydrated.Extra));
    }

    [Fact]
    public void A_private_parameterless_constructor_can_still_be_materialized() {
        var constructed = Rows.ParseOne<WithPrivateConstructor>([new("Foo", typeof(int), false)], 7);
        Assert.Equal(7, constructed.Foo);
    }

    [Fact]
    public void Private_members_stay_out_of_default_discovery() {
        Refusals.NoParserFor<PrivateByDefault>(() => Rows.ParseOne<PrivateByDefault>([new("Id", typeof(int), false)], 7));
    }

    [Fact]
    public void Private_members_require_the_default_info_opt_in() {
        var external = (DefaultTypeParsingInfo)TypeParsingInfo.GetOrAdd<ExternalRow>();
        external.UsePrivateMembers = true;
        Assert.Equal(7, Rows.ParseOne<ExternalRow>([new("Id", typeof(int), false)], 7).ReadId());
    }

    [Fact]
    public void Private_member_opt_in_covers_internal_protected_and_private_storage() {
        var inherited = (DefaultTypeParsingInfo)TypeParsingInfo.GetOrAdd<PrivateInheritanceRow>();
        inherited.UsePrivateMembers = true;
        var row = Rows.ParseOne<PrivateInheritanceRow>([
            new("Internal", typeof(int), false), new("Protected", typeof(int), false), new("Private", typeof(int), false), new("Public", typeof(int), false)], 1, 2, 3, 4);
        Assert.Equal((1, 2, 3, 4), (row.ReadInternal(), row.ReadProtected(), row.ReadPrivate(), row.Public));
    }

    [Fact]
    public void Private_member_configuration_cannot_change_after_parser_creation() {
        var info = (DefaultTypeParsingInfo)TypeParsingInfo.GetOrAdd<LatePrivateOption>();
        ColumnInfo[] columns = [new("Name", typeof(string), false)];
        _ = TypeParser.GetTypeParser<LatePrivateOption>(columns);

        var error = Assert.Throws<RinkuConfigurationException>(() => info.UsePrivateMembers = true);
        Assert.Equal(ErrorCodes.ConfigurationAfterUse, error.Code);
    }

    [Fact]
    public void Unsigned_integer_scalar_reads_from_a_signed_column() {
        Assert.Equal(300u, Rows.ParseOne<uint>([new("V", typeof(long), false)], 300L));
    }

    [Fact]
    public void TimeSpan_scalar_reads_from_a_string_column() {
        Assert.Equal(TimeSpan.FromMinutes(90), Rows.ParseOne<TimeSpan>([new("V", typeof(string), false)], "01:30:00"));
    }

    [Fact]
    public void Nullable_float_and_guid_constructor_parameters_read_their_values() {
        var guid = Guid.NewGuid();
        var row = Rows.ParseOne<NoDefaults>([
            new("A1", typeof(int), true), new("B1", typeof(int), true), new("F1", typeof(float), true), new("S1", typeof(string), false), new("G1", typeof(Guid), false)], DBNull.Value, DBNull.Value, DBNull.Value, "Rinku", guid);
        Assert.Equal((0, null, 0f, "Rinku", guid), (row.A, row.B, row.F, row.S, row.G));
    }

    [Fact]
    public void Enum_parameters_bind_values_and_explicit_sql_null() {
        const EnumParam a = EnumParam.A;
        EnumParam? b = EnumParam.B;
        object c = DBNull.Value;
        var command = Render.From(new QueryCommand("SELECT @a AS A, @b AS B, @c AS C"), new { a, b, c });

        Assert.Equal(["@a", "@b", "@c"], command.BoundParameters.Select(parameter => parameter.ParameterName));
        Assert.Equal(a, command.BoundParameters[0].Value);
        Assert.Equal(b, command.BoundParameters[1].Value);
        Assert.Equal(DBNull.Value, command.BoundParameters[2].Value);
    }
}

public enum ByteEnum : byte { Bla = 1 }
public enum ShortEnum : short { Zero = 0, Two = 2, Five = 5 }
public enum EnumParam : short { None = 0, A = 1, B = 2 }
public class WithByteEnum { public ByteEnum EnumEnum { get; set; } }
public class WithNullableByteEnum { public ByteEnum? EnumEnum { get; set; } }
public class CtorWithEnums(ShortEnum e1, ShortEnum? n1, ShortEnum? n2) { public ShortEnum E { get; } = e1; public ShortEnum? NE1 { get; } = n1; public ShortEnum? NE2 { get; } = n2; }
public class CtorWithChars(char c1, char? c2, char? c3) { public char Char1 { get; } = c1; public char? Char2 { get; } = c2; public char? Char3 { get; } = c3; }
public class StringEnumCtor(ByteEnum enumEnum) { public ByteEnum EnumEnum { get; } = enumEnum; }
public abstract class BaseThing { public int Base { get; set; } }
public class DerivedThing : BaseThing { public int Derived { get; set; } }
public class TwoCtors { public TwoCtors() => B = default!; public TwoCtors(int a, string b) { A = a + 1; B = b + "!"; } public int A { get; set; } public string B { get; set; } }
[method: CanCompleteWithMembers]
public class MixedHydration(int id, string name) { public int Id { get; } = id; public string Name { get; } = name; public int? Extra { get; set; } }
public class NoDefaults(int? a1, int? b1, float? f1, string s1, Guid g1) { public int A { get; } = a1 ?? 0; public int? B { get; } = b1; public float F { get; } = f1 ?? 0; public string S { get; } = s1; public Guid G { get; } = g1; }
public class WithPrivateConstructor { private WithPrivateConstructor() { } public int Foo { get; set; } }
public class ExternalRow : IDbReadable { private int Id { get; set; } public int ReadId() => Id; }
public class PrivateByDefault { private int Id { get; set; } }
public class PrivateInheritanceRow : PrivateInheritanceBase, IDbReadable { private int Private { get; set; } public int Public { get; set; } public int ReadPrivate() => Private; }
public class PrivateInheritanceBase { internal int Internal { get; set; } protected int Protected { get; set; } public int ReadInternal() => Internal; public int ReadProtected() => Protected; }
public class LatePrivateOption : IDbReadable { private int Id { get; set; } public string Name { get; set; } = null!; }
