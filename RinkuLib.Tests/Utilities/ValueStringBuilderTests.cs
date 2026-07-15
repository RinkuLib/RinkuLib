using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Utilities;

/// <summary>
/// <see cref="ValueStringBuilder"/> builds strings over a caller buffer, growing into pooled arrays
/// when it runs out.
/// </summary>
public class ValueStringBuilderTests {
    [Fact]
    public void Stack_buffer_constructor_starts_empty() {
        Span<char> buffer = stackalloc char[10];
        var sb = new ValueStringBuilder(buffer);
        Assert.Equal(0, sb.Length);
        Assert.Equal(10, sb.Capacity);
        Assert.Equal(10, sb.RawChars.Length);
    }

    [Fact]
    public void Capacity_constructor_rents_at_least_the_requested_size() {
        var sb = new ValueStringBuilder(128);
        try {
            Assert.Equal(0, sb.Length);
            Assert.True(sb.Capacity >= 128);
        }
        finally {
            sb.Dispose();
        }
    }

    [Fact]
    public void Append_grows_past_the_initial_buffer() {
        var sb = new ValueStringBuilder(stackalloc char[2]);
        for (int i = 0; i < 100; i++)
            sb.Append('x');
        Assert.Equal(100, sb.Length);
        Assert.Equal(new string('x', 100), sb.ToStringAndDispose());
    }

    [Fact]
    public void Append_string_and_null_string() {
        var sb = new ValueStringBuilder(stackalloc char[16]);
        sb.Append("abc");
        sb.Append(null);
        sb.Append("def");
        Assert.Equal("abcdef", sb.ToStringAndDispose());
    }

    [Fact]
    public void Append_span_copies_the_content() {
        var sb = new ValueStringBuilder(stackalloc char[4]);
        sb.Append("hello world".AsSpan());
        Assert.Equal("hello world", sb.ToStringAndDispose());
    }

    [Fact]
    public void Append_repeated_char() {
        var sb = new ValueStringBuilder(stackalloc char[4]);
        sb.Append('-', 5);
        Assert.Equal("-----", sb.ToStringAndDispose());
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(7, "7")]
    [InlineData(42, "42")]
    [InlineData(1234567, "1234567")]
    [InlineData(-1, "-1")]
    [InlineData(-987654, "-987654")]
    [InlineData(int.MaxValue, "2147483647")]
    [InlineData(int.MinValue, "-2147483648")]
    public void Append_int_formats_like_ToString(int value, string expected) {
        var sb = new ValueStringBuilder(stackalloc char[4]);
        sb.Append(value);
        Assert.Equal(expected, sb.ToStringAndDispose());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(9, 1)]
    [InlineData(10, 2)]
    [InlineData(99, 2)]
    [InlineData(100, 3)]
    [InlineData(1000000, 7)]
    [InlineData(int.MaxValue, 10)]
    public void DigitCount_counts_decimal_digits(int value, int expected)
        => Assert.Equal(expected, ValueStringBuilder.DigitCount(value));

    [Fact]
    public void Insert_char_shifts_the_tail() {
        var sb = new ValueStringBuilder(stackalloc char[8]);
        sb.Append("abcd");
        sb.Insert(2, 'X', 2);
        Assert.Equal("abXXcd", sb.ToStringAndDispose());
    }
    [Fact]
    public void Insert_char_over_grows() {
        var sb = new ValueStringBuilder(stackalloc char[8]);
        sb.Append("abcd");
        sb.Insert(2, 'X', 10);
        Assert.Equal("abXXXXXXXXXXcd", sb.ToStringAndDispose());
    }

    [Fact]
    public void Insert_string_shifts_the_tail() {
        var sb = new ValueStringBuilder(stackalloc char[8]);
        sb.Append("abcd");
        sb.Insert(1, "--");
        Assert.Equal("a--bcd", sb.ToStringAndDispose());
    }

    [Fact]
    public void Insert_null_does_nothing() {
        var sb = new ValueStringBuilder(stackalloc char[8]);
        sb.Append("abcd");
        sb.Insert(1, null);
        Assert.Equal("abcd", sb.ToStringAndDispose());
    }

    [Fact]
    public void Indexer_reads_and_writes_in_place() {
        var sb = new ValueStringBuilder(stackalloc char[4]);
        sb.Append("abc");
        Assert.Equal('b', sb[1]);
        sb[1] = 'B';
        Assert.Equal("aBc", sb.ToStringAndDispose());
    }

    [Fact]
    public void Length_can_truncate() {
        var sb = new ValueStringBuilder(stackalloc char[8]);
        sb.Append("abcdef");
        sb.Length = 3;
        Assert.Equal("abc", sb.ToStringAndDispose());
    }

    [Fact]
    public void AsSpan_slices_the_written_content() {
        var sb = new ValueStringBuilder(stackalloc char[8]);
        sb.Append("abcdef");
        Assert.Equal("abcdef", sb.AsSpan().ToString());
        Assert.Equal("cdef", sb.AsSpan(2).ToString());
        Assert.Equal("cd", sb.AsSpan(2, 2).ToString());
        sb.Dispose();
    }

    [Fact]
    public void TryCopyTo_copies_and_disposes() {
        var sb = new ValueStringBuilder(stackalloc char[8]);
        sb.Append("abc");
        Span<char> destination = stackalloc char[8];
        Assert.True(sb.TryCopyTo(destination, out var written));
        Assert.Equal(3, written);
        Assert.Equal("abc", destination[..written].ToString());
    }

    [Fact]
    public void TryCopyTo_fails_on_a_small_destination() {
        var sb = new ValueStringBuilder(stackalloc char[8]);
        sb.Append("abcdef");
        Span<char> destination = stackalloc char[2];
        Assert.False(sb.TryCopyTo(destination, out var written));
        Assert.Equal(0, written);
    }

    [Fact]
    public void AppendSpan_reserves_writable_space() {
        var sb = new ValueStringBuilder(stackalloc char[8]);
        sb.Append("ab");
        var slot = sb.AppendSpan(2);
        slot[0] = 'c';
        slot[1] = 'd';
        Assert.Equal("abcd", sb.ToStringAndDispose());
    }

    [Fact]
    public void AppendSpan_reserves_writable_space_and_grows() {
        var sb = new ValueStringBuilder(stackalloc char[4]);
        sb.Append("ab");
        var slot = sb.AppendSpan(4);
        slot[0] = 'c';
        slot[1] = 'd';
        slot[2] = 'e';
        slot[3] = 'f';
        Assert.Equal("abcdef", sb.ToStringAndDispose());
    }

    [Fact]
    public void EnsureCapacity_grows_but_never_shrinks() {
        var sb = new ValueStringBuilder(stackalloc char[4]);
        sb.EnsureCapacity(64);
        Assert.True(sb.Capacity >= 64);
        var capacity = sb.Capacity;
        sb.EnsureCapacity(8);
        Assert.Equal(capacity, sb.Capacity);
        sb.Dispose();
    }

    [Fact]
    public void Zero_capacity_still_grows_on_demand() {
        var sb = new ValueStringBuilder(0);
        sb.Append('A');
        Assert.Equal("A", sb.ToStringAndDispose());
    }
}
