using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>
/// A scalar <c>T</c> reads the first column, converting between provider and requested types when
/// they differ.
/// </summary>
public class ScalarMappingTests {
    [Fact]
    public void Int_reads_the_first_column() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        Assert.Equal(7, Rows.ParseOne<int>(cols, 7, "ignored"));
    }

    [Fact]
    public void Parser_reports_whether_another_row_awaits() {
        ColumnInfo[] cols = [new("Id", typeof(int), false)];
        using var reader = Rows.Reader(cols, [1], [2]);
        var parser = TypeParser.GetTypeParser<int>(ref cols);
        reader.Read();
        var (canContinue, first) = parser.Parse(reader);
        Assert.Equal(1, first);
        Assert.True(canContinue);
        var (canContinueAfterLast, second) = parser.Parse(reader);
        Assert.Equal(2, second);
        Assert.False(canContinueAfterLast);
    }

    [Fact]
    public void String_reads_text() {
        ColumnInfo[] cols = [new("Name", typeof(string), false)];
        Assert.Equal("abc", Rows.ParseOne<string>(cols, "abc"));
    }

    [Fact]
    public void Value_types_read_their_own_representation() {
        var guid = Guid.NewGuid();
        var date = new DateTime(2023, 5, 10);
        ColumnInfo[] guidCols = [new("V", typeof(Guid), false)];
        Assert.Equal(guid, Rows.ParseOne<Guid>(guidCols, guid));
        ColumnInfo[] dateCols = [new("V", typeof(DateTime), false)];
        Assert.Equal(date, Rows.ParseOne<DateTime>(dateCols, date));
        ColumnInfo[] boolCols = [new("V", typeof(bool), false)];
        Assert.True(Rows.ParseOne<bool>(boolCols, true));
        ColumnInfo[] decimalCols = [new("V", typeof(decimal), false)];
        Assert.Equal(9.5m, Rows.ParseOne<decimal>(decimalCols, 9.5m));
        ColumnInfo[] charCols = [new("V", typeof(char), false)];
        Assert.Equal('A', Rows.ParseOne<char>(charCols, 'A'));
        ColumnInfo[] blobCols = [new("V", typeof(byte[]), false)];
        Assert.Equal(new byte[] { 1, 2 }, Rows.ParseOne<byte[]>(blobCols, new byte[] { 1, 2 }));
    }

    [Fact]
    public void Widening_and_narrowing_conversions_apply() {
        ColumnInfo[] longCols = [new("V", typeof(long), false)];
        Assert.Equal(123, Rows.ParseOne<int>(longCols, 123L));
        ColumnInfo[] intCols = [new("V", typeof(int), false)];
        Assert.Equal(123L, Rows.ParseOne<long>(intCols, 123));
        Assert.Equal((short)123, Rows.ParseOne<short>(intCols, 123));
        Assert.Equal((byte)123, Rows.ParseOne<byte>(intCols, 123));
        ColumnInfo[] doubleCols = [new("V", typeof(double), false)];
        Assert.Equal(1.5m, Rows.ParseOne<decimal>(doubleCols, 1.5));
        Assert.Equal(1.5f, Rows.ParseOne<float>(doubleCols, 1.5));
    }

    [Fact]
    public void Enum_reads_from_its_underlying_type() {
        ColumnInfo[] cols = [new("V", typeof(int), false)];
        Assert.Equal(SampleColor.Green, Rows.ParseOne<SampleColor>(cols, 2));
    }

    [Fact]
    public void Nullable_scalar_reads_a_value() {
        ColumnInfo[] cols = [new("V", typeof(int), true)];
        Assert.Equal(5, Rows.ParseOne<int?>(cols, 5));
    }

    [Fact]
    public void Nullable_scalar_reads_null() {
        ColumnInfo[] cols = [new("V", typeof(int), true)];
        Assert.Null(Rows.ParseOne<int?>(cols, DBNull.Value));
    }

    [Fact]
    public void Null_into_a_non_nullable_scalar_throws() {
        ColumnInfo[] cols = [new("V", typeof(string), true)];
        Assert.Throws<NullValueAssignmentException>(() => Rows.ParseOne<string>(cols, DBNull.Value));
    }

    [Fact]
    public void Implicit_operator_converts_the_column_type() {
        ColumnInfo[] cols = [new("Nb1", typeof(int), false), new("Nb2", typeof(long), true)];
        using var reader = Rows.Reader(cols, [1, 1L]);
        var parser = TypeParser.GetTypeParser<(WrappedAmount, WrappedAmount)>(ref cols);
        reader.Read();
        var (nb1, nb2) = parser.Parse(reader).Result;
        Assert.Equal(new WrappedAmount(1), nb1);
        Assert.Equal(new WrappedAmount(1), nb2);
    }
}

public enum SampleColor { Red = 1, Green = 2, Blue = 3 }

public record struct WrappedAmount(int Amount) {
    public static implicit operator WrappedAmount([NoName] int amount) => new(amount);
    public static implicit operator WrappedAmount([NoName] long amount) => new((int)amount);
}
