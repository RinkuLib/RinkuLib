using System.Buffers;
using System.Runtime.CompilerServices;

namespace RinkuLib.Tools;
/// <summary>
/// A mutable, expandable buffer that rents its underlying storage from <see cref="ArrayPool{T}"/>.
/// </summary>
public struct PooledArray<T>(int initialCapacity = 4) : IDisposable {
    /// <summary>
    /// Transfers ownership of the underlying array to a <see cref="Locked"/> handle.
    /// </summary>
    /// <remarks>The pooled array (current instance) is render unusable after this operation.</remarks>
    /// <returns>A <see cref="Locked"/> handle responsible for returning the array to the pool.</returns>
    public Locked LockTransfer() {
        var res = new Locked(_array, _count);
        _array = null!;
        _count = 0;
        return res;
    }
    /// <summary>
    /// A fixed-size view of a pooled array that maintains disposal responsibility.
    /// </summary>
    public struct Locked : IDisposable {
        internal Locked(T[] Array, int Count) {
            _array = Array;
            _count = Count;
        }
        private T[] _array;
        private int _count;
        /// <summary>
        /// Gets the underlying array rented from the pool.
        /// </summary>
        public readonly T[] RawArray => _array;
        /// <summary>
        /// Gets a span representing the active portion of the buffer.
        /// </summary>
        public readonly Span<T> Span => _array.AsSpan(0, _count);
        /// <summary>
        /// Gets the number of items currently stored in the buffer.
        /// </summary>
        public readonly int Length => _count;
        /// <summary>
        /// Gets a reference to the item at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index of the element.</param>
        /// <exception cref="IndexOutOfRangeException">Thrown when index is out of bounds.</exception>
        public readonly ref T this[int index] => ref _array[index];
        /// <summary>Gets a reference to the last item in the buffer.</summary>
        public readonly ref T Last => ref _array[_count - 1];
        /// <summary>Returns a slice of the buffer within the current usage.</summary>
        public readonly Span<T> AsSpan(int start, int length) {
            if (start + length > _count)
                throw new ArgumentOutOfRangeException(nameof(length));
            return _array.AsSpan(start, length);
        }
        /// <summary>Returns a slice of the buffer within the current usage.</summary>
        public readonly Span<T> AsSpan(int start) => RawArray.AsSpan(start, Length - start);
        /// <summary>Returns the rented array</summary>
        public void Dispose() {
            if (_array != null) {
                ArrayPool<T>.Shared.Return(
                    _array,
                    clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>()
                );
                _array = null!;
                _count = 0;
            }
        }
    }
    private T[] _array = ArrayPool<T>.Shared.Rent(initialCapacity);
    private int _count = 0;
    /// <summary>
    /// Gets the underlying array rented from the pool.
    /// </summary>
    public readonly T[] RawArray => _array;
    /// <summary>
    /// Gets a span representing the active portion of the buffer.
    /// </summary>
    public readonly Span<T> Span => _array.AsSpan(0, _count);
    /// <summary>
    /// Gets the number of items currently stored in the buffer.
    /// </summary>
    public readonly int Length => _count;
    /// <summary>
    /// Gets the total capacity of the currently rented array.
    /// </summary>
    public readonly int Capacity => _array.Length;
    /// <summary>Default initialization that rent 4 elements as initial capacity</summary>
    public PooledArray() : this(4) {}
    /// <summary>
    /// Gets a reference to the item at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the element.</param>
    /// <exception cref="IndexOutOfRangeException">Thrown when index is out of bounds.</exception>
    public readonly ref T this[int index] {
        get {
            if ((uint)index >= (uint)_count)
                throw new IndexOutOfRangeException();
            return ref _array[index];
        }
    }
    /// <summary>Gets a reference to the last item in the buffer.</summary>
    public readonly ref T Last => ref _array[_count - 1]; 
    /// <summary>Returns a slice of the buffer within the current usage.</summary>
    public readonly Span<T> AsSpan(int start, int length) {
        if (start + length > _count)
            throw new ArgumentOutOfRangeException(nameof(length));
        return _array.AsSpan(start, length);
    }
    /// <summary>Returns a slice of the buffer within the current usage.</summary>
    public readonly Span<T> AsSpan(int start) => RawArray.AsSpan(start, Length - start);
    /// <summary>
    /// Appends an item to the buffer, growing the underlying storage if necessary.
    /// </summary>

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T value) {
        if (_count >= _array.Length)
            Grow();

        _array[_count++] = value;
    }
    /// <summary>
    /// Assigns a value at the specified index (within capacity) and updates the buffer usage to include it.
    /// </summary>
    /// <remarks>
    /// If the index is beyond the current usage, the length is extended so the assigned item becomes the last element. 
    /// This method does not increase <see cref="Capacity"/>.
    /// </remarks>
    /// <param name="index">The target index within the current <see cref="Capacity"/>.</param>
    /// <param name="value">The value to store.</param>
    /// <exception cref="IndexOutOfRangeException">Thrown if the index is outside the rented array bounds.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(int index, T value) {
        _array[index] = value;
        if (index >= _count)
            _count = index + 1;
    }

    private void Grow() {
        var old = _array;
        if (old.Length == 0) {
            _array = ArrayPool<T>.Shared.Rent(4);
            return;
        }
        var next = ArrayPool<T>.Shared.Rent(old.Length * 2);

        Array.Copy(old, next, _count);
        ArrayPool<T>.Shared.Return(old, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());

        _array = next;
    }
    /// <summary>Returns the rented array</summary>
    public void Dispose() {
        if (_array != null) {
            ArrayPool<T>.Shared.Return(
                _array,
                clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>()
            );
            _array = null!;
            _count = 0;
        }
    }
}