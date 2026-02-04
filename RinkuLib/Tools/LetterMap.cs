using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace RinkuLib.Tools;
/// <summary>
/// A memory-efficient dictionary optimized for mapping Latin letters to values.
/// </summary>
/// <remarks>
/// <para>
/// <b>Case Insensitivity:</b> This map treats 'A'-'Z' and 'a'-'z' as identical keys.
/// </para>
/// <para>
/// <b>Key Constraints:</b> Only standard Latin alphabet characters are supported. 
/// Using any other character (numbers, symbols, or extended characters) will result in an error.
/// </para>
/// <para>
/// <b>Iteration:</b> When iterating or accessing keys/values, the data is always 
/// returned in alphabetical order, regardless of the order in which items were added.
/// </para>
/// </remarks>
public class LetterMap<T> : IDictionary<char, T> {
    uint _mask;
    T[] _values = [];
    /// <summary>
    /// Gets a bitmask representing the current set of letters present in the map.
    /// Bit 0 corresponds to 'a', bit 25 to 'z'.
    /// </summary>
    public uint PresenceMap => _mask;
    public LetterMap() {}
    /// <summary>
    /// Initializes the map with a collection of character-value pairs.
    /// </summary>
    /// <param name="items">The initial items to populate the map.</param>
    public LetterMap(
#if NET8_0_OR_GREATER
        params
#endif
        ReadOnlySpan<ValueTuple<char, T>> items)
        => ResetWith(items);
    /// <summary>
    /// Clears the map and populates it with the provided items.
    /// </summary>
    /// <param name="items">A span of character-value pairs to initialize the map.</param>
    /// <remarks>
    /// If duplicate letters are provided in the input, the last occurrence 
    /// will determine the final value for that letter.
    /// </remarks>
    public void ResetWith(params ReadOnlySpan<ValueTuple<char, T>> items) {
        _mask = 0;
        int len = items.Length;
        if (len == 0) {
            _values = [];
            return;
        }
        _values = new T[len];
        for (int i = 0; i < len; i++)
            _mask |= 1U << Idx(items[i].Item1);
        for (int i = 0; i < len; i++)
            _values[Rank(_mask, Idx(items[i].Item1))] = items[i].Item2;
        if (Count < len)
            Array.Resize(ref _values, Count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Idx(char c) {
        int i = (c | (char)0x20) - 'a';
        if ((uint)i >= 26)
            throw new ArgumentOutOfRangeException(nameof(c));
        return i;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Rank(uint mask, int idx) {
        // mask & ((1u << idx) - 1) zeroes out everything above the index
        uint target = mask & ((1u << idx) - 1);

#if NETCOREAPP3_0_OR_GREATER
        return System.Numerics.BitOperations.PopCount(target);
#else
    // Software fallback: SWAR (SIMD Within A Register) algorithm
    // This is the fastest way to count bits without hardware intrinsics
    target = target - ((target >> 1) & 0x55555555);
    target = (target & 0x33333333) + ((target >> 2) & 0x33333333);
    return (int)((((target + (target >> 4)) & 0x0F0F0F0F) * 0x01010101) >> 24);
#endif
    }
    /// <summary>
    /// Gets or sets the value associated with the specified letter.
    /// </summary>
    /// <param name="key">The letter (A-Z or a-z).</param>
    /// <exception cref="KeyNotFoundException">Thrown on retrieval if the letter is missing.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the character is not a Latin letter.</exception>
    public T this[char key] {
        get {
            if (!TryGetValue(key, out var v))
                throw new KeyNotFoundException();
            return v;
        }
        set => Set(key, value);
    }

    public ICollection<char> Keys => GetKeysArray();

    private char[] GetKeysArray() {
        int count = Count;
        if (count == 0)
            return [];
        var keys = new char[count];
        int k = 0;
        for (int i = 0; i < 26; i++)
            if ((_mask & (1u << i)) != 0)
                keys[k++] = (char)('a' + i);
        return keys;
    }

    public ICollection<T> Values => _values;
    /// <summary>
    /// Gets the number of unique letters currently mapped.
    /// </summary>
    public int Count {
        get {
#if NETCOREAPP3_0_OR_GREATER
            return System.Numerics.BitOperations.PopCount(_mask);
#else
            return SoftwarePopCount(_mask);
#endif
        }
    }

#if !NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SoftwarePopCount(uint i) {
        i = i - ((i >> 1) & 0x55555555);
        i = (i & 0x33333333) + ((i >> 2) & 0x33333333);
        return (int)((((i + (i >> 4)) & 0x0F0F0F0F) * 0x01010101) >> 24);
    }
#endif
    public bool IsReadOnly => false;
    public void Add(char key, T value) {
        if (ContainsKey(key))
            throw new ArgumentException("Key already exists");
        Set(key, value);
    }
    public void Add(KeyValuePair<char, T> item)
        => Add(item.Key, item.Value);
    /// <summary>
    /// Fast check to see if a specific letter is present in the map.
    /// </summary>
    public bool ContainsKey(char key)
        => (_mask & (1u << Idx(key))) != 0;
    /// <summary>
    /// Attempts to retrieve a value. Does not throw if the key is missing or invalid.
    /// </summary>
    public bool TryGetValue(char key, out T value) {
        int i = Idx(key);
        uint bit = 1u << i;
        if ((_mask & bit) == 0) {
            value = default!;
            return false;
        }
        value = _values[Rank(_mask, i)];
        return true;
    }
    /// <summary>
    /// Removes a letter from the map.
    /// </summary>
    /// <returns>True if the letter was found and removed; otherwise, false.</returns>
    public bool Remove(char key) {
        int i = Idx(key);
        uint bit = 1u << i;

        if ((_mask & bit) == 0)
            return false;

        int r = Rank(_mask, i);
        var arr = _values;

        if (arr.Length == 1) {
            _values = [];
            _mask = 0;
            return true;
        }

        var newArr = new T[arr.Length - 1];
        Array.Copy(arr, 0, newArr, 0, r);
        Array.Copy(arr, r + 1, newArr, r, arr.Length - r - 1);

        _values = newArr;
        _mask &= ~bit;
        return true;
    }

    public bool Remove(KeyValuePair<char, T> item)
        => TryGetValue(item.Key, out var v)
           && EqualityComparer<T>.Default.Equals(v, item.Value)
           && Remove(item.Key);

    public void Clear() {
        _mask = 0;
        _values = [];
    }

    public bool Contains(KeyValuePair<char, T> item)
        => TryGetValue(item.Key, out var v)
           && EqualityComparer<T>.Default.Equals(v, item.Value);

    public void CopyTo(KeyValuePair<char, T>[] array, int arrayIndex) {
        if (_values.Length == 0)
            return;
        int v = 0;
        for (int i = 0; i < 26; i++) {
            if ((_mask & (1u << i)) == 0)
                continue;
            array[v] = new KeyValuePair<char, T>((char)('a' + i), _values[v]);
            v++;
        }
    }

    public IEnumerator<KeyValuePair<char, T>> GetEnumerator() {
        if (_values.Length == 0)
            yield break;
        int v = 0;
        for (int i = 0; i < 26; i++) {
            if ((_mask & (1u << i)) == 0)
                continue;
            yield return new KeyValuePair<char, T>((char)('a' + i), _values[v++]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /* ================= Core ================= */

    void Set(char key, T value) {
        int i = Idx(key);
        uint bit = 1u << i;
        int r = Rank(_mask, i);

        if ((_mask & bit) != 0) {
            _values[r] = value;
            return;
        }

        var arr = _values;
        var newArr = new T[arr == null ? 1 : arr.Length + 1];

        if (arr != null) {
            Array.Copy(arr, 0, newArr, 0, r);
            Array.Copy(arr, r, newArr, r + 1, arr.Length - r);
        }

        newArr[r] = value;
        _values = newArr;
        _mask |= bit;
    }
}
