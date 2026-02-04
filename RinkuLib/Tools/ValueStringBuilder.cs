using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RinkuLib.Tools;
public ref partial struct ValueStringBuilder {
    private char[]? _arrayToReturnToPool;
    private Span<char> _chars;
    private int _pos;

    public ValueStringBuilder(Span<char> initialBuffer) {
        _arrayToReturnToPool = null;
        _chars = initialBuffer;
        _pos = 0;
    }

    public ValueStringBuilder(int initialCapacity) {
        _arrayToReturnToPool = ArrayPool<char>.Shared.Rent(initialCapacity);
        _chars = _arrayToReturnToPool;
        _pos = 0;
    }

    public int Length {
        readonly get => _pos;
        set {
            Debug.Assert(value >= 0);
            Debug.Assert(value <= _chars.Length);
            _pos = value;
        }
    }

    public readonly int Capacity => _chars.Length;

    public void EnsureCapacity(int capacity) {
        // This is not expected to be called this with negative capacity
        Debug.Assert(capacity >= 0);

        // If the caller has a bug and calls this with negative capacity, make sure to call Grow to throw an exception.
        if ((uint)capacity > (uint)_chars.Length)
            Grow(capacity - _pos);
    }

    /// <summary>
    /// Get a pinnable reference to the builder.
    /// Does not ensure there is a null char after <see cref="Length"/>
    /// This overload is pattern matched in the C# 7.3+ compiler so you can omit
    /// the explicit method call, and write eg "fixed (char* c = builder)"
    /// </summary>
    public readonly ref char GetPinnableReference() {
        return ref MemoryMarshal.GetReference(_chars);
    }

    /// <summary>
    /// Get a pinnable reference to the builder.
    /// </summary>
    /// <param name="terminate">Ensures that the builder has a null char after <see cref="Length"/></param>
    public ref char GetPinnableReference(bool terminate) {
        if (terminate) {
            EnsureCapacity(Length + 1);
            _chars[Length] = '\0';
        }
        return ref MemoryMarshal.GetReference(_chars);
    }

    public ref char this[int index] {
        get {
            Debug.Assert(index < _pos);
            Debug.Assert(index >= 0);
            return ref _chars[index];
        }
    }

    public string ToStringAndDispose() {
        string s = _chars[.._pos].ToString();
        Dispose();
        return s;
    }

    /// <summary>Returns the underlying storage of the builder.</summary>
    public readonly Span<char> RawChars => _chars;

    /// <summary>
    /// Returns a span around the contents of the builder.
    /// </summary>
    /// <param name="terminate">Ensures that the builder has a null char after <see cref="Length"/></param>
    public ReadOnlySpan<char> AsSpan(bool terminate) {
        if (terminate) {
            EnsureCapacity(Length + 1);
            _chars[Length] = '\0';
        }
        return _chars[.._pos];
    }

    public readonly ReadOnlySpan<char> AsSpan() => _chars[.._pos];
    public readonly ReadOnlySpan<char> AsSpan(int start) => _chars[start.._pos];
    public readonly ReadOnlySpan<char> AsSpan(int start, int length) => _chars.Slice(start, length);

    public bool TryCopyTo(Span<char> destination, out int charsWritten) {
        if (_chars[.._pos].TryCopyTo(destination)) {
            charsWritten = _pos;
            Dispose();
            return true;
        }
        else {
            charsWritten = 0;
            Dispose();
            return false;
        }
    }

    public void Insert(int index, char value, int count) {
        if (_pos > _chars.Length - count) {
            Grow(count);
        }

        int remaining = _pos - index;
        _chars.Slice(index, remaining).CopyTo(_chars[(index + count)..]);
        _chars.Slice(index, count).Fill(value);
        _pos += count;
    }

    public void Insert(int index, string? s) {
        if (s == null) {
            return;
        }

        int count = s.Length;

        if (_pos > (_chars.Length - count)) {
            Grow(count);
        }

        int remaining = _pos - index;
        _chars.Slice(index, remaining).CopyTo(_chars[(index + count)..]);
        s
#if !NET6_0_OR_GREATER
            .AsSpan()
#endif
            .CopyTo(_chars[index..]);
        _pos += count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(char c) {
        int pos = _pos;
        if ((uint)pos < (uint)_chars.Length) {
            _chars[pos] = c;
            _pos = pos + 1;
        }
        else {
            GrowAndAppend(c);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(string? s) {
        if (s == null) {
            return;
        }

        int pos = _pos;
        if (s.Length == 1 && (uint)pos < (uint)_chars.Length) // very common case, e.g. appending strings from NumberFormatInfo like separators, percent symbols, etc.
        {
            _chars[pos] = s[0];
            _pos = pos + 1;
        }
        else {
            AppendSlow(s);
        }
    }

    private void AppendSlow(string s) {
        int pos = _pos;
        if (pos > _chars.Length - s.Length) {
            Grow(s.Length);
        }

        s
#if !NET6_0_OR_GREATER
            .AsSpan()
#endif
            .CopyTo(_chars[pos..]);
        _pos += s.Length;
    }

    public void Append(char c, int count) {
        if (_pos > _chars.Length - count) {
            Grow(count);
        }

        Span<char> dst = _chars.Slice(_pos, count);
        for (int i = 0; i < dst.Length; i++) {
            dst[i] = c;
        }
        _pos += count;
    }

    public unsafe void Append(char* value, int length) {
        int pos = _pos;
        if (pos > _chars.Length - length) {
            Grow(length);
        }

        Span<char> dst = _chars.Slice(_pos, length);
        for (int i = 0; i < dst.Length; i++) {
            dst[i] = *value++;
        }
        _pos += length;
    }

    public void Append(ReadOnlySpan<char> value) {
        int pos = _pos;
        if (pos > _chars.Length - value.Length) {
            Grow(value.Length);
        }

        value.CopyTo(_chars[_pos..]);
        _pos += value.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<char> AppendSpan(int length) {
        int origPos = _pos;
        if (origPos > _chars.Length - length) {
            Grow(length);
        }

        _pos = origPos + length;
        return _chars.Slice(origPos, length);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowAndAppend(char c) {
        Grow(1);
        Append(c);
    }

    /// <summary>
    /// Resize the internal buffer either by doubling current buffer size or
    /// by adding <paramref name="additionalCapacityBeyondPos"/> to
    /// <see cref="_pos"/> whichever is greater.
    /// </summary>
    /// <param name="additionalCapacityBeyondPos">
    /// Number of chars requested beyond current position.
    /// </param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int additionalCapacityBeyondPos) {
        Debug.Assert(additionalCapacityBeyondPos > 0);
        Debug.Assert(_pos > _chars.Length - additionalCapacityBeyondPos, "Grow called incorrectly, no resize is needed.");

        // Make sure to let Rent throw an exception if the caller has a bug and the desired capacity is negative
        char[] poolArray = ArrayPool<char>.Shared.Rent((int)Math.Max((uint)(_pos + additionalCapacityBeyondPos), (uint)_chars.Length * 2));

        _chars[.._pos].CopyTo(poolArray);

        char[]? toReturn = _arrayToReturnToPool;
        _chars = _arrayToReturnToPool = poolArray;
        if (toReturn != null) {
            ArrayPool<char>.Shared.Return(toReturn);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() {
        char[]? toReturn = _arrayToReturnToPool;
        this = default; // for safety, to avoid using pooled array if this instance is erroneously appended to again
        if (toReturn != null) {
            ArrayPool<char>.Shared.Return(toReturn);
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(int i) {
        if (i < 0) {
            if (i == int.MinValue) {
                Append("-2147483648");
                return;
            }
            i = -i;
            Append('-');
        }
        int pos = _pos;
        var digits = DigitCount(i);
        if (pos > _chars.Length - digits)
            Grow(digits);
        if (i == 0) {
            _chars[_pos] = '0';
            _pos++;
            return;
        }
        pos += digits - 1;
        while (i != 0) {
            _chars[pos--] = (char)('0' + (i % 10));
            i /= 10;
        }
        _pos += digits;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int DigitCount(int v) {
        if (v < 10)
            return 1;
        if (v < 100)
            return 2;
        if (v < 1_000)
            return 3;
        if (v < 10_000)
            return 4;
        if (v < 100_000)
            return 5;
        if (v < 1_000_000)
            return 6;
        if (v < 10_000_000)
            return 7;
        if (v < 100_000_000)
            return 8;
        if (v < 1_000_000_000)
            return 9;
        return 10;
    }
}
