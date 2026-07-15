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
    /*
    [Fact]
    public void String_from_a_numeric_column() {
        ColumnInfo[] cols = [new("V", typeof(int), false)];
        Assert.Equal("42", Rows.ParseOne<string>(cols, 42));
    }
    */
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

}

public class CtorConverted {
    public string Raw;
    public CtorConverted([NoName] string raw) => Raw = raw;
}
