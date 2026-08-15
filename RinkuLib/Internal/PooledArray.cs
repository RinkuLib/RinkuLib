using System.Buffers;
using System.Runtime.CompilerServices;

namespace Rinku.Internal;
internal struct PooledArray<T>(int initialCapacity = 4) : IDisposable {
    public Locked LockTransfer() {
        var res = new Locked(_array, _count);
        _array = null!;
        _count = 0;
        return res;
    }
    public struct Locked : IDisposable {
        internal Locked(T[] Array, int Count) {
            _array = Array;
            _count = Count;
        }
        private T[] _array;
        private int _count;
        public readonly T[] RawArray => _array;
        public readonly Span<T> Span => _array.AsSpan(0, _count);
        public readonly int Length => _count;
        public readonly ref T this[int index] => ref _array[index];
        public readonly ref T Last => ref _array[_count - 1];
        public readonly Span<T> AsSpan(int start, int length) {
            if (start + length > _count)
                throw new ArgumentOutOfRangeException(nameof(length));
            return _array.AsSpan(start, length);
        }
        public readonly Span<T> AsSpan(int start) => RawArray.AsSpan(start, Length - start);
        public void Dispose() {
            if (_array is null)
                return;
            if (_array.Length > 0)
                ArrayPool<T>.Shared.Return(
                    _array,
                    clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>()
                );
            _array = null!;
            _count = 0;
        }
    }
    private T[] _array = initialCapacity == 0 ? [] : ArrayPool<T>.Shared.Rent(initialCapacity);
    private int _count = 0;
    public readonly T[] RawArray => _array;
    public readonly Span<T> Span => _array.AsSpan(0, _count);
    public readonly int Length => _count;
    public readonly int Capacity => _array.Length;
    public PooledArray() : this(4) {}
    public readonly ref T this[int index] {
        get {
            if ((uint)index >= (uint)_count)
                throw new IndexOutOfRangeException();
            return ref _array[index];
        }
    }
    public readonly ref T Last => ref _array[_count - 1]; 
    public readonly Span<T> AsSpan(int start, int length) {
        if (start + length > _count)
            throw new ArgumentOutOfRangeException(nameof(length));
        return _array.AsSpan(start, length);
    }
    public readonly Span<T> AsSpan(int start) => RawArray.AsSpan(start, Length - start);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T value) {
        if (_count >= _array.Length)
            Grow();

        _array[_count++] = value;
    }
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
    public void Dispose() {
        if (_array is null)
            return;
        if (_array.Length > 0)
            ArrayPool<T>.Shared.Return(
                _array,
                clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>()
            );
        _array = null!;
        _count = 0;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RemoveAt(int index) {
        int remaining = --_count - index;
        if (remaining > 0) {
            Span<T> arraySpan = _array.AsSpan();
            arraySpan.Slice(index + 1, remaining).CopyTo(arraySpan[index..]);
        }
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            _array[_count] = default!;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear() {
        _count = 0;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly T[] ToArray() {
        if (_count == 0)
            return [];
        return Span.ToArray();
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly List<T> ToList() {
        var list = new List<T>(_count);
        System.Runtime.InteropServices.CollectionsMarshal.SetCount(list, _count);
        Span.CopyTo(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list));
        return list;
    }
}
