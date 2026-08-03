using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Utilities;

/// <summary>
/// <see cref="PooledArray{T}"/> is a growable buffer over pool-rented storage.
/// </summary>
public class PooledArrayTests {
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(128)]
    public void Constructor_starts_empty_with_at_least_the_requested_capacity(int capacity) {
        using var array = new PooledArray<int>(capacity);
        Assert.Equal(0, array.Length);
        Assert.True(array.Capacity >= capacity);
        Assert.NotNull(array.RawArray);
    }

    [Fact]
    public void Default_constructor_rents_a_small_buffer() {
        using var array = new PooledArray<int>();
        Assert.True(array.Capacity >= 4);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 10)]
    [InlineData(100, 1000)]
    public void Add_grows_as_needed_and_keeps_content(int initialCapacity, int addCount) {
        using var array = new PooledArray<int>(initialCapacity);
        for (int i = 0; i < addCount; i++)
            array.Add(i);
        Assert.Equal(addCount, array.Length);
        for (int i = 0; i < addCount; i++)
            Assert.Equal(i, array[i]);
    }

    [Fact]
    public void Indexer_rejects_reads_past_the_length() {
        var array = new PooledArray<int>(8);
        try {
            array.Add(1);
            Assert.Throws<IndexOutOfRangeException>(() => array[1]);
            Assert.Throws<IndexOutOfRangeException>(() => array[-1]);
        }
        finally {
            array.Dispose();
        }
    }

    [Fact]
    public void Indexer_returns_a_writable_reference() {
        using var array = new PooledArray<int>(4);
        array.Add(1);
        array[0] = 9;
        Assert.Equal(9, array[0]);
    }

    [Fact]
    public void Last_points_to_the_most_recent_item() {
        using var array = new PooledArray<int>(4);
        array.Add(1);
        array.Add(2);
        Assert.Equal(2, array.Last);
    }

    [Fact]
    public void Set_extends_the_length_to_include_the_index() {
        using var array = new PooledArray<int>(8);
        array.Set(3, 42);
        Assert.Equal(4, array.Length);
        Assert.Equal(42, array[3]);
    }

    [Fact]
    public void RemoveAt_shifts_the_tail_left() {
        using var arrayInt = new PooledArray<int>(8);
        arrayInt.Add(1);
        arrayInt.Add(2);
        arrayInt.Add(3);
        arrayInt.RemoveAt(1);
        Assert.Equal(2, arrayInt.Length);
        Assert.Equal(1, arrayInt[0]);
        Assert.Equal(3, arrayInt[1]);
        using var arrayStr = new PooledArray<string>(8);
        arrayStr.Add("One");
        arrayStr.Add("Two");
        arrayStr.Add("Three");
        arrayStr.RemoveAt(1);
        Assert.Equal(2, arrayStr.Length);
        Assert.Same("One", arrayStr[0]);
        Assert.Same("Three", arrayStr[1]);
    }

    [Fact]
    public void Clear_resets_the_length() {
        using var array = new PooledArray<int>(4);
        array.Add(1);
        array.Clear();
        Assert.Equal(0, array.Length);
    }

    [Fact]
    public void Span_covers_only_the_active_part() {
        using var array = new PooledArray<int>(16);
        array.Add(1);
        array.Add(2);
        Assert.Equal(2, array.Span.Length);
        Assert.Equal([1, 2], array.Span.ToArray());
        Assert.Equal([2], array.AsSpan(1).ToArray());
        Assert.Equal([1], array.AsSpan(0, 1).ToArray());
    }

    [Fact]
    public void AsSpan_rejects_slices_past_the_length() {
        var array = new PooledArray<int>(16);
        try {
            array.Add(1);
            Assert.Throws<ArgumentOutOfRangeException>(() => array.AsSpan(0, 5));
        }
        finally {
            array.Dispose();
        }
    }
    [Fact]
    public void Locked_AsSpan_rejects_slices_past_the_length() {
        var array = new PooledArray<int>(16);
        array.Add(1);
        var locked = array.LockTransfer();
        try {
            Assert.Throws<ArgumentOutOfRangeException>(() => locked.AsSpan(0, 5));
        }
        finally {
            locked.Dispose();
            locked.Dispose();
        }
    }

    [Fact]
    public void ToArray_and_ToList_copy_the_content() {
        using var array = new PooledArray<string>(4);
        array.Add("a");
        array.Add("b");
        Assert.Equal(["a", "b"], array.ToArray());
        Assert.Equal(["a", "b"], array.ToList());
    }

    [Fact]
    public void Empty_ToArray_is_empty() {
        using var array = new PooledArray<int>(4);
        Assert.Empty(array.ToArray());
    }

    [Fact]
    public void LockTransfer_hands_the_buffer_over() {
        var array = new PooledArray<int>(4);
        array.Add(7);
        array.Add(8);
        using var locked = array.LockTransfer();
        Assert.Equal(0, array.Length);
        Assert.Equal(2, locked.Length);
        Assert.Equal(7, locked[0]);
        Assert.Equal(8, locked.Last);
        Assert.Equal([7, 8], locked.Span.ToArray());
        Assert.Equal([8], locked.AsSpan(1).ToArray());
    }

    [Fact]
    public void Dispose_releases_the_buffer() {
        var array = new PooledArray<int>(4);
        array.Add(1);
        array.Dispose();
        Assert.Equal(0, array.Length);
        array.Dispose();
        Assert.Throws<NullReferenceException>(() => array.Add(1));
        Assert.Throws<IndexOutOfRangeException>(() => array[0]);
        Assert.Equal(0, array.Length);
        Assert.Equal(0, array.AsSpan(0).Length);
    }

    [Theory]
    [InlineData(10, 0, 10)]
    [InlineData(10, 5, 5)]
    [InlineData(10, 0, 1)]
    [InlineData(10, 9, 1)]
    [InlineData(10, 2, 3)]
    public void AsSpan_Slicing_MatchesExpectedRanges(int count, int start, int length) {
        using var list = new PooledArray<int>(count);
        for (int i = 0; i < count; i++)
            list.Add(i);

        Assert.Equal(count, list.Span.Length);

        var span1 = list.AsSpan(start);
        Assert.Equal(count - start, span1.Length);

        var span2 = list.AsSpan(start, length);
        Assert.Equal(length, span2.Length);
        Assert.Equal(start, span2[0]);
        var locked = list.LockTransfer();
        span1 = locked.AsSpan(start);
        Assert.Equal(count - start, span1.Length);

        span2 = locked.AsSpan(start, length);
        Assert.Equal(length, span2.Length);
        Assert.Equal(start, span2[0]);
    }
}
