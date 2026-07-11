using System;
using Xunit;
using RinkuLib.Tools;

namespace RinkuLib.Tests.Tools;

public class PooledArrayTests {
    #region Constructor & Initial State

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(128)]
    [InlineData(1024)]
    public void Constructor_InitializesCorrectly_WithVariousCapacities(int capacity) {
        using var array = new PooledArray<int>(capacity);

        Assert.Equal(0, array.Length);
        Assert.True(array.Capacity >= capacity);
        Assert.NotNull(array.RawArray);
    }

    [Fact]
    public void ParameterlessConstructor_UsesDefaultCapacityOfFour() {
        using var array = new PooledArray<int>();
        Assert.True(array.Capacity >= 4);
    }

    #endregion

    #region Add & Growth Logic (Good Paths)

    [Theory]
    [InlineData(0, 1)]      // Empty to 1
    [InlineData(1, 2)]      // Boundary growth
    [InlineData(2, 10)]     // Multiple jumps
    [InlineData(100, 1000)] // Large scale
    public void Add_GrowthDynamics_ShouldBeConsistent(int initialCapacity, int addCount) {
        using var list = new PooledArray<int>(initialCapacity);

        for (int i = 0; i < addCount; i++) {
            list.Add(i);
        }

        Assert.Equal(addCount, list.Length);
        for (int i = 0; i < addCount; i++) {
            Assert.Equal(i, list[i]);
        }
    }

    [Fact]
    public void Add_TriggersGrowth_WhenCapacityReached() {
        using var array = new PooledArray<int>(1);

        // Fill the array to its ACTUAL physical capacity
        int physicalCapacity = array.Capacity;
        for (int i = 0; i < physicalCapacity; i++) {
            array.Add(i);
        }

        // This is the guaranteed trigger point
        array.Add(999);

        Assert.True(array.Capacity > physicalCapacity);
        Assert.Equal(999, array[physicalCapacity]);
    }

    #endregion

    #region Set & Indexer (Good Paths)

    [Theory]
    [InlineData(10, 0, 999, 1)]   // Set first
    [InlineData(10, 5, 999, 6)]   // Set middle (updates length)
    [InlineData(10, 9, 999, 10)]  // Set last within capacity
    public void Set_StateTransitions_UpdatesLengthCorrectly(int initialCap, int index, int value, int expectedLength) {
        using var list = new PooledArray<int>(initialCap);

        list.Set(index, value);

        Assert.Equal(expectedLength, list.Length);
        Assert.Equal(value, list[index]);
    }

    [Fact]
    public void Set_DoesNotShrinkLength_WhenSettingLowerIndex() {
        using var list = new PooledArray<int>(10);
        list.Set(8, 80); // Length becomes 9
        list.Set(2, 20); // Length stays 9

        Assert.Equal(9, list.Length);
        Assert.Equal(80, list[8]);
        Assert.Equal(20, list[2]);
    }

    [Fact]
    public void Indexer_GetAndSet_ByRef_AllowsDirectModification() {
        using var array = new PooledArray<int>(4);
        array.Add(10);

        // Act
        ref int val = ref array[0];
        val += 90;

        // Assert
        Assert.Equal(100, array[0]);
    }

    #endregion

    #region Ownership & Locked Struct (Critical Integrity)

    [Fact]
    public void Lock_TransfersOwnership_AndInvalidatesOriginalToPreventDoubleReturn() {
        var array = new PooledArray<int>(4);
        array.Add(100);
        var internalPointer = array.RawArray;

        using var locked = array.LockTransfer();
        // Locked owns it now
        Assert.Equal(1, locked.Length);
        Assert.Same(internalPointer, locked.RawArray);
        Assert.Equal(100, locked[0]);

        Assert.Equal(0, array.Length);
        Assert.Null(array.RawArray);
    }

    [Fact]
    public void Locked_MaintainsIndependentLifecycle() {
        PooledArray<int>.Locked lockedInstance;

        using (var array = new PooledArray<int>(4)) {
            array.Add(7);
            lockedInstance = array.LockTransfer();
        } // array.Dispose() called here, but it's empty

        Assert.Equal(1, lockedInstance.Length);
        Assert.Equal(7, lockedInstance[0]);

        lockedInstance.Dispose();
        Assert.Null(lockedInstance.RawArray);
    }

    [Fact]
    public void Locked_Indexer_And_Last_Properties() {
        using var array = new PooledArray<int>(4);
        array.Add(10);
        array.Add(20);

        using var locked = array.LockTransfer();
        Assert.Equal(20, locked.Last);
        locked[1] = 30;
        Assert.Equal(30, locked[1]);
    }

    #endregion

    #region Slicing & Spans (Theories)

    [Theory]
    [InlineData(10, 0, 10)] // Full
    [InlineData(10, 5, 5)]  // Half
    [InlineData(10, 0, 1)]  // Single start
    [InlineData(10, 9, 1)]  // Single end
    [InlineData(10, 2, 3)]  // Middle
    public void AsSpan_Slicing_MatchesExpectedRanges(int count, int start, int length) {
        using var list = new PooledArray<int>(count);
        for (int i = 0; i < count; i++)
            list.Add(i);

        // Test Span property
        Assert.Equal(count, list.Span.Length);

        // Test AsSpan methods
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

    #endregion

    #region Memory Safety & Generic Types

    [Fact]
    public void ReferenceTypes_CorrectlyInvokeClearArray_ToPreventLeaking() {
        // Tests that T as a reference type works and doesn't crash growth logic
        using var array = new PooledArray<string>(1);
        array.Add("Data1");
        array.Add("Data2"); // Return original to pool with clearArray: true

        Assert.Equal(2, array.Length);
        Assert.Equal("Data1", array[0]);
        Assert.Equal("Data2", array[1]);
    }

    [Fact]
    public void Dispose_IsIdempotent_CanBeCalledMultipleTimes() {
        var array = new PooledArray<int>(4);
        array.Add(1);

        array.Dispose();
        var ex = Record.Exception(() => array.Dispose());

        Assert.Null(ex); // No exception on second call
        Assert.Null(array.RawArray);
        Assert.Equal(0, array.Length);
    }

    #endregion

    #region Failure Paths (Bad Inputs & Exceptions)

    [Theory]
    [InlineData(-1)]
    [InlineData(1)] // Count is 1, index 1 is OOB
    public void Indexer_OutOfBounds_Throws_IndexOutOfRangeException(int index) {
        using var array = new PooledArray<int>(4);
        array.Add(10);

        Assert.Throws<IndexOutOfRangeException>(() => array[index]);
    }

    [Theory]
    [InlineData(-1, 0)]   // Start is negative (Always fails)
    [InlineData(5, 1)]    // Start is OOB (Always fails)
    public void AsSpan_InvalidStart_Throws(int start, int length) {
        using var array = new PooledArray<int>(1);
        array.Add(1);

        // Both of these will throw because 'start' itself is illegal
        Assert.Throws<ArgumentOutOfRangeException>(() => array.AsSpan(start));
        Assert.Throws<ArgumentOutOfRangeException>(() => array.AsSpan(start, length));
    }

    [Theory]
    [InlineData(0, 100)]  // Start is 0 (Valid), but Length is 100 (OOB)
    public void AsSpan_InvalidLength_Throws(int start, int length) {
        using var array = new PooledArray<int>(1);
        array.Add(1);

        // This SUCCEEDS (it returns a span of length 1)
        var validSpan = array.AsSpan(start);
        Assert.Equal(1, validSpan.Length);

        // This THROWS (because 0 + 100 > 1)
        Assert.Throws<ArgumentOutOfRangeException>(() => array.AsSpan(start, length));
    }

    [Fact]
    public void Last_OnEmptyArray_Throws_IndexOutOfRangeException() {
        using var array = new PooledArray<int>(4);
        Assert.Throws<IndexOutOfRangeException>(() => array.Last);
    }

    [Fact]
    public void Access_After_Dispose_Throws_NullReferenceException() {
        var array = new PooledArray<int>(4);
        array.Dispose();

        Assert.Throws<NullReferenceException>(() => array.Add(1));
        Assert.Throws<IndexOutOfRangeException>(() => array[0]);
        Assert.Equal(0, array.Length);
        Assert.Equal(0, array.AsSpan(0).Length);
    }

    [Fact]
    public void Set_BeyondInternalArrayCapacity_Throws_IndexOutOfRangeException() {
        // This confirms 'Set' requires enough pre-rented capacity
        using var array = new PooledArray<int>(1);
        Assert.Throws<IndexOutOfRangeException>(() => array.Set(1000, 1));
    }

    #endregion
}