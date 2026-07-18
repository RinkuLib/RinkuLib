using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>
/// The converter chain between a column's provider type and the slot's CLR type: numeric opcodes,
/// boxing, parsing, ToString, constructors, and nullable wrapping.
/// </summary>
public class TypeConversionTests {
    [Fact]
    public void Identity_when_types_match() {
        ColumnInfo[] cols = [new("V", typeof(int), false)];
        Assert.Equal(5, Rows.ParseOne<int>(cols, 5));
    }

    [Fact]
    public void Numeric_opcode_conversions_between_sizes() {
        ColumnInfo[] cols = [new("V", typeof(long), false)];
        Assert.Equal(300, Rows.ParseOne<int>(cols, 300L));
        Assert.Equal(300f, Rows.ParseOne<float>(cols, 300L));
        Assert.Equal(300d, Rows.ParseOne<double>(cols, 300L));
    }

    [Fact]
    public void Object_column_boxes_into_the_slot() {
        ColumnInfo[] cols = [new("V", typeof(string), false)];
        object value = Rows.ParseOne<object>(cols, "boxed");
        Assert.Equal("boxed", value);
    }

    [Fact]
    public void Guid_from_a_string_column() {
        var guid = Guid.NewGuid();
        ColumnInfo[] cols = [new("V", typeof(string), false)];
        Assert.Equal(guid, Rows.ParseOne<Guid>(cols, guid.ToString()));
    }
    [Fact]
    public void String_from_a_numeric_column() {
        ColumnInfo[] cols = [new("V", typeof(int), false)];
        Assert.Equal("42", Rows.ParseOne<string>(cols, 42));
    }

    [Fact]
    public void String_from_a_struct_without_a_culture_overload() {
        ColumnInfo[] cols = [new("V", typeof(TimeSpan), false)];
        Assert.Equal("01:02:03", Rows.ParseOne<string>(cols, new TimeSpan(1, 2, 3)));
    }

    [Fact]
    public void String_from_a_reference_column() {
        ColumnInfo[] cols = [new("V", typeof(Version), false)];
        Assert.Equal("1.2.3", Rows.ParseOne<string>(cols, new Version(1, 2, 3)));
    }
    [Fact]
    public void Constructor_conversion_builds_the_slot_type() {
        ColumnInfo[] cols = [new("V", typeof(string), false)];
        var wrapped = Rows.ParseOne<CtorConverted>(cols, "raw");
        Assert.Equal("raw", wrapped.Raw);
    }

    [Fact]
    public void Nullable_wrapper_applies_over_a_conversion() {
        ColumnInfo[] cols = [new("V", typeof(long), true)];
        Assert.Equal(300, Rows.ParseOne<int?>(cols, 300L));
        Assert.Null(Rows.ParseOne<int?>(cols, DBNull.Value));
    }

    [Fact]
    public void Enum_from_each_numeric_width() {
        ColumnInfo[] byteCols = [new("V", typeof(byte), false)];
        Assert.Equal(SampleColor.Green, Rows.ParseOne<SampleColor>(byteCols, (byte)2));
        ColumnInfo[] shortCols = [new("V", typeof(short), false)];
        Assert.Equal(SampleColor.Green, Rows.ParseOne<SampleColor>(shortCols, (short)2));
        ColumnInfo[] longCols = [new("V", typeof(long), false)];
        Assert.Equal(SampleColor.Green, Rows.ParseOne<SampleColor>(longCols, 2L));
    }

    [Fact]
    public void DateTime_from_a_string_column() {
        var stamp = new DateTime(2024, 5, 1, 13, 30, 15);
        ColumnInfo[] cols = [new("V", typeof(string), false)];
        Assert.Equal(stamp, Rows.ParseOne<DateTime>(cols, stamp.ToString("O")));
    }

    [Fact]
    public void A_value_column_boxes_into_an_object_slot() {
        ColumnInfo[] cols = [new("V", typeof(int), false)];
        Assert.Equal(5, Rows.ParseOne<object>(cols, 5));
        Assert.True(ITypeConverter.TryGetConverter(typeof(int), typeof(IComparable), out var boxed));
        Assert.IsType<BoxConverter>(boxed);
    }

    [Fact]
    public void A_source_side_implicit_operator_bridges_to_the_slot() {
        ColumnInfo[] cols = [new("V", typeof(WrappedInt), false)];
        Assert.Equal(9, Rows.ParseOne<int>(cols, new WrappedInt(9)));
    }

    [Fact]
    public void Every_numeric_width_narrows_and_widens() {
        ColumnInfo[] intCols = [new("V", typeof(int), false)];
        Assert.Equal((byte)7, Rows.ParseOne<byte>(intCols, 7));
        Assert.Equal((sbyte)-7, Rows.ParseOne<sbyte>(intCols, -7));
        Assert.Equal((short)-300, Rows.ParseOne<short>(intCols, -300));
        Assert.Equal((ushort)300, Rows.ParseOne<ushort>(intCols, 300));
        Assert.Equal(300u, Rows.ParseOne<uint>(intCols, 300));
        Assert.Equal(300ul, Rows.ParseOne<ulong>(intCols, 300));
        Assert.Equal(300L, Rows.ParseOne<long>(intCols, 300));
        Assert.Equal('A', Rows.ParseOne<char>(intCols, 65));
        Assert.True(Rows.ParseOne<bool>(intCols, 1));
    }

    [Fact]
    public void TimeSpan_and_DateTimeOffset_slots_read_their_own_columns() {
        var span = new TimeSpan(1, 2, 3);
        ColumnInfo[] spanCols = [new("V", typeof(TimeSpan), false)];
        Assert.Equal(span, Rows.ParseOne<TimeSpan>(spanCols, span));

        var stamp = new DateTimeOffset(2024, 5, 1, 13, 30, 15, TimeSpan.FromHours(-4));
        ColumnInfo[] dtoCols = [new("V", typeof(DateTimeOffset), false)];
        Assert.Equal(stamp, Rows.ParseOne<DateTimeOffset>(dtoCols, stamp));

        ColumnInfo[] nullableSpan = [new("V", typeof(TimeSpan), true)];
        Assert.Equal(span, Rows.ParseOne<TimeSpan?>(nullableSpan, span));
        Assert.Null(Rows.ParseOne<TimeSpan?>(nullableSpan, DBNull.Value));
    }

    [Fact]
    public void Selection_prefers_assignment_then_cast_then_operators() {
        Assert.True(ITypeConverter.TryGetConverter(typeof(int), typeof(int), out var identity));
        Assert.IsType<IdentityConverter>(identity);
        Assert.Equal(typeof(int), identity.OutputType);

        Assert.True(ITypeConverter.TryGetConverter(typeof(object), typeof(string), out var downcast));
        Assert.IsType<CastClassConverter>(downcast);

        Assert.True(ITypeConverter.TryGetConverter(typeof(int), typeof(nint), out var toNative));
        Assert.IsType<OpCodeConverter>(toNative);
        Assert.True(ITypeConverter.TryGetConverter(typeof(int), typeof(nuint), out var toUNative));
        Assert.IsType<OpCodeConverter>(toUNative);

        Assert.True(ITypeConverter.TryGetConverter(typeof(string), typeof(int), out var parsable));
        Assert.IsType<ParsableConverter>(parsable);

        Assert.True(ITypeConverter.TryGetConverter(typeof(DayOfWeek), typeof(int), out var fromEnum));
        Assert.IsType<IdentityConverter>(fromEnum);
        Assert.True(ITypeConverter.TryGetConverter(typeof(long), typeof(DayOfWeek), out var toEnum));
        Assert.IsType<OpCodeConverter>(toEnum);

        Assert.True(ITypeConverter.TryGetConverter(typeof(WrappedInt), typeof(int), out var viaOperator));
        Assert.IsType<MethodCallConverter>(viaOperator);
        Assert.True(ITypeConverter.TryGetConverter(typeof(int), typeof(WrappedInt), out var viaCtor));
        Assert.IsType<ConstructorConverter>(viaCtor);
        Assert.True(ITypeConverter.TryGetConverter(typeof(double), typeof(ExplicitOnly), out var explicitOp));
        Assert.IsType<MethodCallConverter>(explicitOp);

        Assert.True(ITypeConverter.TryGetConverter(typeof(long), typeof(int?), out var wrapped));
        Assert.IsType<NullableWrapperConverter>(wrapped);
        Assert.Equal(typeof(int?), wrapped.OutputType);

        Assert.True(ITypeConverter.TryGetConverter(typeof(string), typeof(ParseTarget), out var viaParse));
        Assert.IsType<MethodCallConverter>(viaParse);
        Assert.True(ITypeConverter.TryGetConverter(typeof(int), typeof(ImplicitOnly), out var intoImplicit));
        Assert.IsType<MethodCallConverter>(intoImplicit);
        Assert.True(ITypeConverter.TryGetConverter(typeof(ExplicitSource), typeof(long), out var outOfExplicit));
        Assert.IsType<MethodCallConverter>(outOfExplicit);
        Assert.True(ITypeConverter.TryGetConverter(typeof(DayOfWeek), typeof(ConsoleColor), out var enumToEnum));
        Assert.IsType<IdentityConverter>(enumToEnum);

        Assert.False(ITypeConverter.TryGetConverter(typeof(Version), typeof(Guid), out _));
        Assert.False(ITypeConverter.TryGetConverter(typeof(Version), typeof(Guid?), out _));
        Assert.False(ITypeConverter.TryGetConverter(typeof(WrappedInt), typeof(Guid), out _));
    }
}

public readonly struct ImplicitOnly {
    public readonly int Value;
    private ImplicitOnly(int value) => Value = value;
    public static implicit operator ImplicitOnly(int v) => new(v);
}

public readonly struct ExplicitSource {
    public readonly long Value;
    public static explicit operator long(ExplicitSource v) => v.Value;
}

public readonly struct ParseTarget {
    public readonly int V;
    private ParseTarget(int v) => V = v;
    public static ParseTarget Parse(string s) => new(int.Parse(s));
}

public readonly struct WrappedInt(int value) {
    public readonly int Value = value;
    public static implicit operator int(WrappedInt w) => w.Value;
    public static implicit operator WrappedInt(int v) => new(v);
    public override string ToString() => Value.ToString();
}

public readonly struct ExplicitOnly {
    public readonly double Value;
    private ExplicitOnly(double value) => Value = value;
    public static explicit operator ExplicitOnly(double v) => new(v);
}

public class CtorConverted {
    public string Raw;
    public CtorConverted([NoName] string raw) => Raw = raw;
}
