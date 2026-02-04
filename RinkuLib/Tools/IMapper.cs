using System.Buffers;
using System.Runtime.InteropServices;

namespace RinkuLib.Tools;
/// <summary>
/// A high-performance, immutable lookup table mapping case-insensitive string keys to fixed integer indices. 
/// Designed to serve as a reusable schema for external arrays, enabling the creation of lightweight, 
/// allocation-free dictionary structures.
/// </summary>
/// <remarks>
/// <para>
/// This class is strictly read-only and immutable. If the key set changes, a new instance must be constructed. 
/// It is optimized for "build-once, read-many" scenarios.
/// </para>
/// <para>
/// <b>Performance Tip:</b> In addition to index lookups, this class provides canonical string references. 
/// By retrieving these via <c>GetSameKey</c>, consumers can replace expensive character-by-character 
/// comparisons with O(1) reference equality checks in performance-critical loops.
/// </para>
/// </remarks>
public abstract unsafe class Mapper(string[] Keys) : IReadOnlyDictionary<string, int>, IDisposable {
    /// <summary>
    /// A sentinel array used to indicate the mapper has been disposed in a thread-safe manner.
    /// </summary>
    protected static readonly string[] DeadKeys = [null!];
    /// <summary>
    /// The internal storage of unique, deduplicated keys.
    /// </summary>
    protected string[] _keys = Keys;
    /// <summary>
    /// Gets a managed reference to the start of the key array. 
    /// Allows for high-performance pointer-style iteration while bypassing array bounds checks.
    /// </summary>
#if NET5_0_OR_GREATER
    public ref string KeysStartPtr => ref MemoryMarshal.GetArrayDataReference(_keys);
#else
    public ref string KeysStartPtr => ref _keys[0];
#endif
    /// <summary>
    /// Gets the unique keys held by this mapper as a <see cref="ReadOnlySpan{String}"/>.
    /// </summary>
    public ReadOnlySpan<string> Keys => _keys;
    /// <summary>
    /// Returns the stable index associated with the provided key.
    /// </summary>
    /// <param name="key">The case-insensitive key to locate.</param>
    /// <returns>The zero-based index of the key if it exists; otherwise, -1.</returns>
    public abstract int GetIndex(ReadOnlySpan<char> key);
    /// <summary>
    /// Returns the stable index associated with the provided key.
    /// </summary>
    /// <param name="key">The case-insensitive key to locate.</param>
    /// <returns>The zero-based index of the key if it exists; otherwise, -1.</returns>
    public abstract int GetIndex(string key);
    /// <summary>
    /// Gets the number of unique keys defined in this schema.
    /// </summary>
    public int Count => _keys.Length;

    /// <summary>
    /// Retrieves the canonical string reference stored within the mapper for a given input.
    /// </summary>
    /// <param name="ind">The index of the key to look up.</param>
    /// <returns>
    /// The internal <see cref="string"/> instance if a match is found; otherwise, <see langword="throw"/>.
    /// </returns>
    /// <remarks>
    /// This is a high-performance optimization tool. By retrieving the internal string reference, 
    /// subsequent comparisons can use **Reference Equality** (Object.ReferenceEquals) 
    /// instead of character-by-character string comparisons, significantly accelerating 
    /// downstream lookup logic.
    /// </remarks>
    public string GetKey(int ind) => _keys[ind];

    /// <summary>
    /// Retrieves the canonical string reference stored within the mapper for a given input.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <returns>
    /// The internal <see cref="string"/> instance if a match is found; otherwise, <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// This is a high-performance optimization tool. By retrieving the internal string reference, 
    /// subsequent comparisons can use **Reference Equality** (Object.ReferenceEquals) 
    /// instead of character-by-character string comparisons, significantly accelerating 
    /// downstream lookup logic.
    /// </remarks>
    public string GetSameKey(string key) {
        int i = GetIndex(key);
        return i >= 0 ? _keys[i] : null!;
    }
    /// <summary>
    /// Retrieves the canonical string reference stored within the mapper for a given input.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <returns>
    /// The internal <see cref="string"/> instance if a match is found; otherwise, <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// This is a high-performance optimization tool. By retrieving the internal string reference, 
    /// subsequent comparisons can use **Reference Equality** (Object.ReferenceEquals) 
    /// instead of character-by-character string comparisons, significantly accelerating 
    /// downstream lookup logic.
    /// </remarks>
    public string GetSameKey(ReadOnlySpan<char> key) {
        int i = GetIndex(key);
        return i >= 0 ? _keys[i] : null!;
    }

    /// <summary>
    /// Returns the stable index associated with the provided key.
    /// </summary>
    /// <param name="key">The case-insensitive key to locate.</param>
    /// <returns>The zero-based index of the key if it exists; otherwise, -1.</returns>
    public int this[string key] => GetIndex(key);

    /// <summary>
    /// Returns the stable index associated with the provided key.
    /// </summary>
    /// <param name="key">The case-insensitive key to locate.</param>
    /// <returns>The zero-based index of the key if it exists; otherwise, -1.</returns>
    public int this[ReadOnlySpan<char> key] => GetIndex(key);
    public bool ContainsKey(string key) => GetIndex(key) >= 0;
    public bool ContainsKey(ReadOnlySpan<char> key) => GetIndex(key) >= 0;
    public bool TryGetValue(string key, out int value) => (value = GetIndex(key)) >= 0;
    public bool TryGetValue(ReadOnlySpan<char> key, out int value) => (value = GetIndex(key)) >= 0;
    IEnumerable<string> IReadOnlyDictionary<string, int>.Keys => _keys;
    public IEnumerable<int> Values => Enumerable.Range(0, Count);
    public IEnumerator<KeyValuePair<string, int>> GetEnumerator() {
        var keys = _keys;
        for (int i = 0; i < keys.Length; i++)
            yield return new(keys[i], i);
    }
    /// <summary>
    /// Disposes the mapper and triggers unmanaged cleanup. 
    /// This operation is thread-safe and ensures <see cref="DisposeUnmanaged"/> is called exactly once.
    /// </summary>
    public void Dispose() {
        string[] originalKeys = Interlocked.Exchange(ref _keys, DeadKeys);
        if (!ReferenceEquals(originalKeys, DeadKeys)) {
            DisposeUnmanaged();
            GC.SuppressFinalize(this);
        }
    }
    /// <summary>
    /// When overridden in a derived class, performs specialized cleanup of unmanaged memory 
    /// or pooled resources used by the specific lookup strategy.
    /// </summary>
    protected abstract void DisposeUnmanaged();
    ~Mapper() => Dispose();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Factory method to build a schema from an existing collection of keys.
    /// </summary>
    /// <param name="keys">The collection of keys to index.</param>
    /// <returns>A specialized <see cref="Mapper"/> instance.</returns>
    /// <remarks>
    /// Input order is preserved. If duplicate keys (case-insensitive) are present, 
    /// only the first occurrence is stored, and its position becomes the fixed index for that key.
    /// </remarks>
    public static Mapper GetMapper(IEnumerable<string> keys) {
        string[]? pooledArr = null;
        Span<string> k;
        if (keys is string[] arr)
            k = arr;
#if NET5_0_OR_GREATER
        else if (keys is List<string> list)
            k = CollectionsMarshal.AsSpan(list);
#endif
        else if (keys is IReadOnlyList<string> ro) {
            pooledArr = ArrayPool<string>.Shared.Rent(ro.Count);
            for (int i = 0; i < ro.Count; i++)
                pooledArr[i] = ro[i];
            k = pooledArr.AsSpan(0, ro.Count);
        }
        else {
            var pool = new PooledArray<string>();
            foreach (string key in keys)
                pool.Add(key);
            pooledArr = pool.RawArray;
            k = pool.Span;
        }
        var mapper = GetMapper(k);
        if (pooledArr is not null)
            ArrayPool<string>.Shared.Return(pooledArr);
        return mapper;
    }
    /// <summary>
    /// Factory method to build a schema from a span of keys.
    /// </summary>
    /// <param name="keys">A span of keys used to define the schema.</param>
    /// <returns>A specialized <see cref="Mapper"/> instance.</returns>
    /// <remarks>
    /// Input order is preserved. If duplicate keys (case-insensitive) are present, 
    /// only the first occurrence is stored, and its position becomes the fixed index for that key.
    /// </remarks>
    public static Mapper GetMapper(
#if NET8_0_OR_GREATER
        params
#endif
        Span<string> keys) {
        if (keys.Length == 0)
            return EmptyMapper;
        if (keys.Length == 1)
            return GetOneKeyMapper(keys[0]);
        if (keys.Length == 2)
            return GetTwoKeyMapper(keys[0], keys[1]);
        var builder = new AsciiMapperBuilder(keys);
        if (builder.MaxDepth >= 0 || builder.UsedKeys is not null) {
            var usedKeys = builder.UsedKeys;
            if (usedKeys.Length == 0)
                return EmptyMapper;
            if (usedKeys.Length == 1)
                return GetOneKeyMapper(usedKeys[0]);
            if (usedKeys.Length == 2)
                return GetTwoKeyMapper(usedKeys[0], usedKeys[1]);
#if NET8_0_OR_GREATER
            return IsAllAscii(builder.UsedKeys) 
                ? new AsciiMapper<AsciiStrategy>(builder.UsedKeys, builder.LengthMask, builder.Steps, builder.MaxDepth)
                : new AsciiMapper<UnicodeStrategy>(builder.UsedKeys, builder.LengthMask, builder.Steps, builder.MaxDepth);
#else
            return new AsciiMapper(builder.UsedKeys, builder.LengthMask, builder.Steps, builder.MaxDepth);
#endif
        }
        var dict = new DictMapper(keys);
        var uKeys = dict.Keys;
        if (uKeys.Length == 0)
            return EmptyMapper;
        if (uKeys.Length == 1)
            return GetOneKeyMapper(uKeys[0]);
        if (uKeys.Length == 2)
            return GetTwoKeyMapper(uKeys[0], uKeys[1]);
        return dict;
    }
#if NET8_0_OR_GREATER
    /// <summary>
    /// Checks if all strings in the array contain only ASCII characters.
    /// </summary>
    public static bool IsAllAscii(string[] keys) {
        foreach (var key in keys)
            if (!System.Text.Ascii.IsValid(key))
                return false;
        return true;
    }
#endif
    public static readonly Mapper EmptyMapper = new Empty();
    /// <summary>
    /// Provides a shared schema for instances requiring zero keys.
    /// </summary>
    /// <returns>A singleton <see cref="Mapper"/> containing no keys.</returns>
    public static Mapper GetEmptyMapper() => EmptyMapper;
    /// <summary>
    /// Factory method specifically for a single-key schema.
    /// </summary>
    /// <param name="key">The sole key in the schema.</param>
    /// <returns>An optimized <see cref="Mapper"/> containing exactly one key at index 0.</returns>
    public static Mapper GetOneKeyMapper(string key) {
        if (key is null)
            throw new NullReferenceException("A key in the set was null");
        return new One(key);
    }
    /// <summary>
    /// Factory method specifically for a two-key schema.
    /// </summary>
    /// <param name="key1">The key to be assigned to index 0.</param>
    /// <param name="key2">The key to be assigned to index 1.</param>
    /// <returns>A specialized <see cref="Mapper"/> instance.</returns>
    /// <remarks>
    /// If the keys are identical (case-insensitive), this returns a single-key mapper 
    /// to ensure the index remains stable at 0.
    /// </remarks>
    public static Mapper GetTwoKeyMapper(string key1, string key2) {
        if (key1 is null || key2 is null)
            throw new NullReferenceException("A key in the set was null");
        if (string.Equals(key1, key2, StringComparison.OrdinalIgnoreCase))
            return GetOneKeyMapper(key1);
        return new Two(key1, key2);
    }
    public sealed class Empty() : Mapper([]) {
        protected override void DisposeUnmanaged() { }
        public override int GetIndex(string _) => -1;
        public override int GetIndex(ReadOnlySpan<char> _) => -1;
    }
    public sealed class One(string Key) : Mapper([Key]) {
        public readonly string Key = Key;
        protected override void DisposeUnmanaged() { }
        public override int GetIndex(string key)
            => string.Equals(Key, key, StringComparison.OrdinalIgnoreCase) ? 0 : -1;
        public override int GetIndex(ReadOnlySpan<char> key)
            => key.Equals(Key, StringComparison.OrdinalIgnoreCase) ? 0 : -1;
    }
    public sealed class Two(string Key1, string Key2) : Mapper([Key1, Key2]) {
        public readonly string Key1 = Key1;
        public readonly string Key2 = Key2;
        protected override void DisposeUnmanaged() { }
        public override int GetIndex(string key)
            => string.Equals(Key1, key, StringComparison.OrdinalIgnoreCase) ? 0
            : string.Equals(Key2, key, StringComparison.OrdinalIgnoreCase) ? 1 : -1;
        public override int GetIndex(ReadOnlySpan<char> key)
            => key.Equals(Key1, StringComparison.OrdinalIgnoreCase) ? 0
            : key.Equals(Key2, StringComparison.OrdinalIgnoreCase) ? 0 : -1;
    }/*
    public unsafe sealed class DictMapper : Mapper {
        private readonly Dictionary<string, int> Dict;
        private readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> SpanDict;
        public DictMapper(Span<string> keys) : base(Init(keys, out var dict)) {
            Dict = dict;
            SpanDict = dict.GetAlternateLookup<ReadOnlySpan<char>>();
        }
        private static string[] Init(Span<string> keys, out Dictionary<string, int> dict) {
            int i = 0;
            dict = new Dictionary<string, int>(keys.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys) {
                if (key is null)
                    throw new NullReferenceException("A key in the set was null");
                if (dict.TryAdd(key, i))
                    i++;
            }
            var keysArr = new string[i];
            foreach (var kvp in dict)
                keysArr[kvp.Value] = kvp.Key;
            return keysArr;
        }
        protected override void DisposeUnmanaged() { }
        public override int GetIndex(string key)
            => Dict.TryGetValue(key, out var ind) ? ind : -1;
        public override int GetIndex(ReadOnlySpan<char> key)
            => SpanDict.TryGetValue(key, out var ind) ? ind : -1;
    }*/
    public unsafe sealed class DictMapper : Mapper {
        private readonly Dictionary<string, int> _stringDict;

#if NET9_0_OR_GREATER
        private readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _spanDict;
#else
    // For Legacy, we store the data in a way that we can manually probe
    private readonly int[] _buckets;
    private readonly Entry[] _entries;
    private readonly int _mask;

    private struct Entry {
        public string Key;
        public int Value;
        public int Next;
        public int Hash;
    }
#endif

        public DictMapper(Span<string> keys) : base(Init(keys, out var dict)) {
            _stringDict = dict;
#if NET9_0_OR_GREATER
            _spanDict = dict.GetAlternateLookup<ReadOnlySpan<char>>();
#else
        // Build a minimal internal hash table for Span lookups
        int size = HashHelper.GetPrime(dict.Count);
        _buckets = new int[size];
        for (int i = 0; i < size; i++) _buckets[i] = -1;
        _entries = new Entry[dict.Count];
        _mask = size; // Used for modulo

        int index = 0;
        foreach (var kvp in dict) {
            int h = GetOrdinalIgnoreCaseHash(kvp.Key.AsSpan());
            int bucket = (h & 0x7FFFFFFF) % size;
            _entries[index] = new Entry { 
                Key = kvp.Key, 
                Value = kvp.Value, 
                Hash = h, 
                Next = _buckets[bucket] 
            };
            _buckets[bucket] = index;
            index++;
        }
#endif
        }

        public override int GetIndex(ReadOnlySpan<char> key) {
#if NET9_0_OR_GREATER
            return _spanDict.TryGetValue(key, out var ind) ? ind : -1;
#else
        if (_buckets == null) return -1;
        int h = GetOrdinalIgnoreCaseHash(key);
        int bucket = (h & 0x7FFFFFFF) % _mask;
        for (int i = _buckets[bucket]; i >= 0; i = _entries[i].Next) {
            if (_entries[i].Hash == h && MemoryExtensions.Equals(key, _entries[i].Key.AsSpan(), StringComparison.OrdinalIgnoreCase))
                return _entries[i].Value;
        }
        return -1;
#endif
        }

#if !NET9_0_OR_GREATER
    // Manual OrdinalIgnoreCase Hash for netstandard2.0
    private static int GetOrdinalIgnoreCaseHash(ReadOnlySpan<char> span) {
        unchecked {
            int hash = 5381;
            for (int i = 0; i < span.Length; i++) {
                char c = span[i];
                if (c >= 'a' && c <= 'z') c = (char)(c - 32);
                hash = ((hash << 5) + hash) ^ c;
            }
            return hash;
        }
    }
#endif

        public override int GetIndex(string key) => _stringDict.TryGetValue(key, out var ind) ? ind : -1;
        protected override void DisposeUnmanaged() { }

        private static string[] Init(Span<string> keys, out Dictionary<string, int> dict) {
            dict = new Dictionary<string, int>(keys.Length, StringComparer.OrdinalIgnoreCase);
            int i = 0;
            foreach (var k in keys) {
                if (k == null)
                    throw new NullReferenceException();
#if NET8_0_OR_GREATER
                if (dict.TryAdd(k, i)) { i++; }
#else
                if (!dict.ContainsKey(k)) { dict.Add(k, i); i++; }
#endif
            }
            var arr = new string[dict.Count];
            foreach (var kvp in dict)
                arr[kvp.Value] = kvp.Key;
            return arr;
        }
    }
}
#if !NET9_0_OR_GREATER
internal static class HashHelper {
    // A small subset of primes used by the .NET Dictionary
    private static readonly int[] Primes = [
        3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919,
        1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103
    ];

    public static int GetPrime(int min) {
        for (int i = 0; i < Primes.Length; i++) {
            if (Primes[i] >= min)
                return Primes[i];
        }
        // Fallback for extremely large tables (rare for ORM mappers)
        return min | 1;
    }
}
#endif