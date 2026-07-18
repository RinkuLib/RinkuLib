using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>
/// Mapping behaviors carried over from Dapper's suite, for the ground both libraries cover: enums in
/// members and constructors, inherited members, and constructor preference.
/// </summary>
public class DapperParityMappingTests {
    [Fact]
    public void Enum_member_reads_from_a_numeric_column() {
        ColumnInfo[] cols = [new("EnumEnum", typeof(byte), false)];
        var row = Rows.ParseOne<WithByteEnum>(cols, (byte)1);
        Assert.Equal(ByteEnum.Bla, row.EnumEnum);
    }

    [Fact]
    public void Nullable_enum_member_reads_null() {
        ColumnInfo[] cols = [new("EnumEnum", typeof(byte), true)];
        var row = Rows.ParseOne<WithNullableByteEnum>(cols, DBNull.Value);
        Assert.Null(row.EnumEnum);
    }
    /*
    [Fact]
    public void Enum_member_reads_from_a_string_column_case_insensitively() {
        ColumnInfo[] cols = [new("EnumEnum", typeof(string), false)];
        Assert.Equal(ByteEnum.Bla, Rows.ParseOne<WithByteEnum>(cols, "BLA").EnumEnum);
        Assert.Equal(ByteEnum.Bla, Rows.ParseOne<WithByteEnum>(cols, "bla").EnumEnum);
    }
    */
    [Fact]
    public void Enum_constructor_parameters_read_values_and_null() {
        ColumnInfo[] cols = [
            new("E1", typeof(short), false),
            new("N1", typeof(short), true),
            new("N2", typeof(short), true),
        ];
        var row = Rows.ParseOne<CtorWithEnums>(cols, (short)2, (short)5, DBNull.Value);
        Assert.Equal(ShortEnum.Two, row.E);
        Assert.Equal(ShortEnum.Five, row.NE1);
        Assert.Null(row.NE2);
    }

    [Fact]
    public void Char_constructor_parameters_read_values_and_null() {
        ColumnInfo[] cols = [
            new("C1", typeof(char), false),
            new("C2", typeof(char), true),
            new("C3", typeof(char), true),
        ];
        var row = Rows.ParseOne<CtorWithChars>(cols, 'ą', DBNull.Value, 'ó');
        Assert.Equal('ą', row.Char1);
        Assert.Null(row.Char2);
        Assert.Equal('ó', row.Char3);
    }

    [Fact]
    public void Inherited_public_members_map_alongside_own_members() {
        ColumnInfo[] cols = [new("Base", typeof(int), false), new("Derived", typeof(int), false)];
        var row = Rows.ParseOne<DerivedThing>(cols, 3, 4);
        Assert.Equal(3, row.Base);
        Assert.Equal(4, row.Derived);
    }

    [Fact]
    public void Richest_satisfiable_constructor_wins_over_the_parameterless_one() {
        ColumnInfo[] cols = [new("A", typeof(int), false), new("B", typeof(string), false)];
        var row = Rows.ParseOne<TwoCtors>(cols, 0, "Rinku");
        Assert.Equal(1, row.A);
        Assert.Equal("Rinku!", row.B);
    }

    [Fact]
    public void Mixed_ctor_and_member_hydration_fills_both() {
        ColumnInfo[] cols = [
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
            new("Extra", typeof(int), true),
        ];
        var row = Rows.ParseOne<MixedHydration>(cols, 1, "n", 5);
        Assert.Equal(1, row.Id);
        Assert.Equal("n", row.Name);
        Assert.Equal(5, row.Extra);
    }
    
    [Fact]
    public void Unsigned_integer_scalar_reads_from_a_signed_column() {
        ColumnInfo[] cols = [new("V", typeof(long), false)];
        Assert.Equal(300u, Rows.ParseOne<uint>(cols, 300L));
    }

    [Fact]
    public void TimeSpan_scalar_reads_from_a_string_column() {
        ColumnInfo[] cols = [new("V", typeof(string), false)];
        Assert.Equal(TimeSpan.FromMinutes(90), Rows.ParseOne<TimeSpan>(cols, "01:30:00"));
    }
    
    [Fact]
    public void Nullable_float_and_guid_ctor_parameters_read_their_values() {
        var guid = Guid.NewGuid();
        ColumnInfo[] cols = [
            new("A1", typeof(int), true),
            new("B1", typeof(int), true),
            new("F1", typeof(float), true),
            new("S1", typeof(string), false),
            new("G1", typeof(Guid), false),
        ];
        var row = Rows.ParseOne<NoDefaults>(cols, DBNull.Value, DBNull.Value, DBNull.Value, "Dapper", guid);
        Assert.Equal(0, row.A);
        Assert.Null(row.B);
        Assert.Equal(0, row.F);
        Assert.Equal("Dapper", row.S);
        Assert.Equal(guid, row.G);
    }
}

public enum ByteEnum : byte { Bla = 1 }
public enum ShortEnum : short { Zero = 0, Two = 2, Five = 5 }
public class WithByteEnum {
    public ByteEnum EnumEnum { get; set; }
}
public class WithNullableByteEnum {
    public ByteEnum? EnumEnum { get; set; }
}
public class CtorWithEnums(ShortEnum e1, ShortEnum? n1, ShortEnum? n2) {
    public ShortEnum E { get; } = e1;
    public ShortEnum? NE1 { get; } = n1;
    public ShortEnum? NE2 { get; } = n2;
}
public class CtorWithChars(char c1, char? c2, char? c3) {
    public char Char1 { get; } = c1;
    public char? Char2 { get; } = c2;
    public char? Char3 { get; } = c3;
}
public abstract class BaseThing {
    public int Base { get; set; }
}
public class DerivedThing : BaseThing {
    public int Derived { get; set; }
}
public class TwoCtors {
    public TwoCtors() => B = default!;
    public TwoCtors(int a, string b) {
        A = a + 1;
        B = b + "!";
    }
    public int A { get; set; }
    public string B { get; set; }
}
[method: CanCompleteWithMembers]
public class MixedHydration(int id, string name) {
    public int Id { get; } = id;
    public string Name { get; } = name;
    public int? Extra { get; set; }
}
public class NoDefaults(int? a1, int? b1, float? f1, string s1, Guid g1) {
    public int A { get; } = a1 ?? 0;
    public int? B { get; } = b1;
    public float F { get; } = f1 ?? 0;
    public string S { get; } = s1;
    public Guid G { get; } = g1;
}
