using System;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Tools;

public class ValueStringBuilderTests {
    #region 1. Constructors & Initial State

    [Fact]
    public void Constructor_Span_SetsCorrectInitialState() {
        Span<char> buffer = stackalloc char[10];
        var sb = new ValueStringBuilder(buffer);

        Assert.Equal(0, sb.Length);
        Assert.Equal(10, sb.Capacity);
        Assert.Equal(10, sb.RawChars.Length);
    }

    [Fact]
    public void Constructor_Capacity_RentsFromPool() {
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
    public void Constructor_Zero_HandlesGrowth() {
        var sb = new ValueStringBuilder(0);
        sb.Append('A');
        Assert.Equal("A", sb.ToStringAndDispose());
    }

    #endregion

    #region 2. Append Methods (Standard & Edge)

    [Theory]
    [InlineData('a', 1)]
    [InlineData('z', 10)]
    [InlineData('!', 100)] // Stress growth
    public void Append_Char_Theories(char c, int count) {
        var sb = new ValueStringBuilder(stackalloc char[1]);
        for (int i = 0; i < count; i++)
            sb.Append(c);

        Assert.Equal(count, sb.Length);
        foreach (var ch in sb.AsSpan())
            Assert.Equal(c, ch);
        sb.Dispose();
    }

    [Fact]
    public void Append_String_HandlesNull() {
        var sb = new ValueStringBuilder(stackalloc char[5]);
        sb.Append((string?)null);
        Assert.Equal(0, sb.Length);
    }

    [Fact]
    public void Append_String_Variations() {
        var sb = new ValueStringBuilder(stackalloc char[2]);
        sb.Append("A");      // Single char optimization
        sb.Append("BCDE");   // Multi char + Grow
        Assert.Equal("ABCDE", sb.ToStringAndDispose());
    }

    [Fact]
    public void Append_CharRepeat_FillsCorrectly() {
        var sb = new ValueStringBuilder(stackalloc char[10]);
        sb.Append('x', 5);
        Assert.Equal("xxxxx", sb.ToStringAndDispose());
    }

    [Fact]
    public unsafe void Append_Ptr_CopiesMemory() {
        var sb = new ValueStringBuilder(stackalloc char[10]);
        string test = "Pointer";
        fixed (char* p = test) {
            sb.Append(p, 4);
        }
        Assert.Equal("Poin", sb.ToStringAndDispose());
    }

    [Fact]
    public void AppendSpan_ReturnsWriteableSegment() {
        var sb = new ValueStringBuilder(stackalloc char[10]);
        var span = sb.AppendSpan(3);
        "ABC".AsSpan().CopyTo(span);
        Assert.Equal("ABC", sb.ToStringAndDispose());
    }

    #endregion

    #region 3. Integer Formatting (Full Range)

    [Theory]
    [InlineData(0, "0")]
    [InlineData(9, "9")]
    [InlineData(-1, "-1")]
    [InlineData(int.MaxValue, "2147483647")]
    [InlineData(int.MinValue, "-2147483648")]
    public void Append_Int_AllValues(int value, string expected) {
        var sb = new ValueStringBuilder(stackalloc char[1]);
        sb.Append(value);
        Assert.Equal(expected, sb.ToStringAndDispose());
    }

    [Fact]
    public void DigitCount_FullRangeTest() {
        Assert.Equal(1, ValueStringBuilder.DigitCount(0));
        Assert.Equal(1, ValueStringBuilder.DigitCount(9));
        Assert.Equal(2, ValueStringBuilder.DigitCount(10));
        Assert.Equal(10, ValueStringBuilder.DigitCount(1234567890));
    }

    #endregion

    #region 4. Insert Logic (Displacement)

    [Fact]
    public void Insert_CharRepeat_ShiftsExistingData() {
        var sb = new ValueStringBuilder(stackalloc char[10]);
        sb.Append("AC");
        sb.Insert(1, 'B', 2);
        Assert.Equal("ABBC", sb.ToStringAndDispose());
    }

    [Fact]
    public void Insert_String_HandlesNullAndMiddle() {
        var sb = new ValueStringBuilder(stackalloc char[10]);
        sb.Append("StartEnd");
        sb.Insert(5, "-Mid-");
        Assert.Equal("Start-Mid-End", sb.ToStringAndDispose());
    }

    #endregion

    #region 5. Growth & Buffer Spilling

    [Fact]
    public void Grow_StackToHeap_Transition() {
        Span<char> stack = stackalloc char[2];
        var sb = new ValueStringBuilder(stack);
        sb.Append("ABC"); // Spill to ArrayPool

        Assert.True(sb.Capacity >= 4);
        Assert.Equal("ABC", sb.ToStringAndDispose());
    }

    [Fact]
    public void EnsureCapacity_ManuallyTriggersGrow() {
        var sb = new ValueStringBuilder(4);
        sb.EnsureCapacity(20);
        Assert.True(sb.Capacity >= 20);
        sb.Dispose();
    }

    #endregion

    #region 6. Pointers, Spans & Indexers

    [Fact]
    public void GetPinnableReference_ReturnsFirstChar() {
        var sb = new ValueStringBuilder(stackalloc char[10]);
        sb.Append("XYZ");
        ref char r = ref sb.GetPinnableReference();
        Assert.Equal('X', r);
    }

    [Fact]
    public void Indexer_GetAndSet_ByRef() {
        var sb = new ValueStringBuilder(stackalloc char[10]);
        sb.Append("A");
        sb[0] = 'B';
        Assert.Equal('B', sb[0]);
    }

    [Fact]
    public void AsSpan_WithIndices_ReturnsCorrectSlices() {
        var sb = new ValueStringBuilder(stackalloc char[10]);
        sb.Append("012345");
        Assert.Equal("2345", sb.AsSpan(2).ToString());
        Assert.Equal("23", sb.AsSpan(2, 2).ToString());
        sb.Dispose();
    }

    #endregion

    #region 7. Advanced Edges (The "Tough" Tests)

    [Fact]
    public void AsSpan_Terminate_GrowsIfAtCapacity() {
        var sb = new ValueStringBuilder(stackalloc char[2]);
        sb.Append("AB");

        // This requires space for '\0' at index 2
        var span = sb.AsSpan(terminate: true);

        Assert.Equal(2, span.Length);
        Assert.Equal('\0', sb.RawChars[2]);
        Assert.True(sb.Capacity > 2);
        sb.Dispose();
    }

    [Fact]
    public void Length_Setter_TruncatesCorrectly() {
        var sb = new ValueStringBuilder(stackalloc char[10]);
        sb.Append("12345");
        sb.Length = 2;
        Assert.Equal(2, sb.Length);
        Assert.Equal("12", sb.ToStringAndDispose());
    }

    #endregion

    #region 8. Lifecycle & Destruction

    [Fact]
    public void ToString_DisposesAndResets() {
        var sb = new ValueStringBuilder(10);
        sb.Append("Data");
        _ = sb.ToStringAndDispose();

        Assert.Equal(0, sb.Length);
        Assert.Equal(0, sb.Capacity);
        sb.Dispose();
    }

    [Fact]
    public void TryCopyTo_SuccessAndFail_Paths() {
        // Fail Path
        var sb = new ValueStringBuilder(10);
        sb.Append("TooLong");
        bool success = sb.TryCopyTo(new char[2], out _);
        Assert.False(success);
        Assert.Equal(0, sb.Capacity); // Verify disposal

        // Success Path
        var sb2 = new ValueStringBuilder(10);
        sb2.Append("Ok");
        bool success2 = sb2.TryCopyTo(new char[2], out _);
        Assert.True(success2);
        Assert.Equal(0, sb2.Capacity); // Verify disposal
    }

    [Fact]
    public void Dispose_IsIdempotent() {
        var sb = new ValueStringBuilder(10);
        sb.Dispose();
        sb.Dispose(); // Should not throw
    }

    #endregion
}
   