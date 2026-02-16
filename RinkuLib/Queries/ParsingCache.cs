using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RinkuLib.Tools;

namespace RinkuLib.Queries;
/// <summary>
/// Represent an cached item used for parsing a <see cref="DbDataReader"/>
/// </summary>
public struct ParsingCacheItem {
    /// <summary>
    /// The actual parser func
    /// </summary>
    public object Parser;
    /// <summary>
    /// The indexes at which the condition muts be false
    /// </summary>
    public int[] FalseIndexes;
    /// <summary>
    /// The schema for which the <see cref="Parser"/> is for
    /// </summary>
    public ColumnInfo[] Schema;
    /// <summary>
    /// The default behavior of the reader
    /// </summary>
    public CommandBehavior CommandBehavior;
}
/// <summary>
/// Class that holds one or more <see cref="ISchemaParser"/>
/// </summary>
public abstract class ParsingCache {
    /// <summary>
    /// A lock shared to ensure thread safety across multiple <see cref="ISchemaParser"/> instances.
    /// </summary>
    public static readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        SharedLock = new();
    /// <summary>
    /// Determines the most appropriate cache depending on the number of conditional select columns in the query
    /// </summary>
    public static ParsingCache? New(int nbSelects) {
        if (nbSelects <= 0)
            return null;
        if (nbSelects <= 32)
            return new ParsingCache<Masker32, uint>();
        if (nbSelects <= 64)
            return new ParsingCache<Masker64, ulong>();
        if (nbSelects <= 128)
            return new ParsingCache<Masker128, Int128>();
        if (nbSelects <= 256)
            return new ParsingCache<Masker256, Int256>();
        return new ParsingCache<MaskerInfinite, ulong[]>();
    }
    /// <summary>
    /// The initial check if there is allready a <see cref="ISchemaParser"/> stored that can match <typeparamref name="T"/> 
    /// without <see cref="ISchemaParser{T}.Init(System.Data.Common.DbDataReader, System.Data.IDbCommand)"/>
    /// </summary>
    public abstract bool TryGetCache<T>(object?[] variables, out SchemaParser<T> cache);
    /// <summary>
    /// Used to retrieve the identifier to use with <see cref="UpdateCache{T}"/> to update the appropriate cache item.
    /// </summary>
    public abstract int GetActualCacheIndex<T>(object?[] variables);
    /// <summary>
    /// The initial check if there is allready a <see cref="ISchemaParser"/> stored that can match <typeparamref name="T"/> 
    /// without <see cref="ISchemaParser{T}.Init(System.Data.Common.DbDataReader, System.Data.IDbCommand)"/>
    /// </summary>
    public abstract bool TryGetCache<T>(Span<bool> usageMap, out SchemaParser<T> cache);
    /// <summary>
    /// Used to retrieve the identifier to use with <see cref="UpdateCache{T}"/> to update the appropriate cache item.
    /// </summary>
    public abstract int GetActualCacheIndex<T>(Span<bool> usageMap);
    /// <summary>
    /// Update the cache at the specified identifier
    /// </summary>
    public abstract bool UpdateCache<T>(int ind, SchemaParser<T> cache);
}
internal interface IKeyMasker<TMask> where TMask : notnull {
    public abstract static TMask ToMask(object?[] variables);
    public abstract static TMask ToMask(Span<bool> usageMap);
    public abstract static bool Equals(TMask k1, TMask k2);
}
internal sealed class ParsingCache<TMasker, TMask> : ParsingCache where TMasker : IKeyMasker<TMask> where TMask : notnull {
    private ISchemaParser[] Cache = [];
    private TMask[] Keys = [];
    public override bool TryGetCache<T>(object?[] variables, out SchemaParser<T> cache)
        => TryGetCache(TMasker.ToMask(variables), out cache);
    public override int GetActualCacheIndex<T>(object?[] variables)
        => GetActualCacheIndex<T>(TMasker.ToMask(variables));
    public override bool TryGetCache<T>(Span<bool> usageMap, out SchemaParser<T> cache)
        => TryGetCache(TMasker.ToMask(usageMap), out cache);
    public override int GetActualCacheIndex<T>(Span<bool> usageMap)
        => GetActualCacheIndex<T>(TMasker.ToMask(usageMap));
    private bool TryGetCache<T>(TMask mask, out SchemaParser<T> cache) {
        var keys = Keys;
        for (int i = 0; i < keys.Length; i++)
            if (TMasker.Equals(keys[i], mask)) {
                if (Cache[i] is SchemaParser<T> c) {
                    cache = c;
                    return true;
                }
                cache = new(_ => throw new Exception($"There allready has an item cached for a different type Current:{Cache[i].GetType()} Target:{typeof(SchemaParser<T>)}"), default);
                return false;
            }
        cache = default;
        return false;
    }


    private int GetActualCacheIndex<T>(TMask mask) {
        var keys = Keys;
        lock (SharedLock) {
            for (int i = 0; i < keys.Length; i++)
                if (TMasker.Equals(keys[i], mask)) {
                    if (Cache[i] is SchemaParser<T>)
                        return i;
                    return -1;
                }
            if (Cache.Length <= 0) {
                Cache = new ISchemaParser[1];
                Cache[0] = new SchemaParser<T>();
                return 0;
            }
            var len = Cache.Length;
            var c = new ISchemaParser[len + 1];
            Array.Copy(Cache, c, len);
            Cache = c;
            Cache[len] = new SchemaParser<T>();
            var k = new TMask[len + 1];
            Array.Copy(Keys, k, len);
            Keys = k;
            Keys[len] = mask;
            return len;
        }
    }
    public override bool UpdateCache<T>(int ind, SchemaParser<T> cache) {
        lock (SharedLock) {
            if (ind == -1)
                throw new Exception("The item was allready cached using a different type");
            if (((SchemaParser<T>)Cache[ind]).IsInit)
                return false;
            Cache[ind] = cache;
            return true;
        }
    }
}
internal class Masker32 : IKeyMasker<uint> {
    public static uint ToMask(object?[] variables) {
        uint mask = 0U;
        ref object? pVar = ref MemoryMarshal.GetArrayDataReference(variables);
        for (int i = 0; i < variables.Length; i++)
            if (Unsafe.Add(ref pVar, i) is not null)
                mask |= 1U << i;
        return mask;
    }
    public static uint ToMask(Span<bool> usageMap) {
        uint mask = 0U;
        for (int i = 0; i < usageMap.Length; i++)
            if (usageMap[i])
                mask |= 1U << i;
        return mask;
    }
    public static bool Equals(uint k1, uint k2) => k1 == k2;
}
internal class Masker64 : IKeyMasker<ulong> {
    public static ulong ToMask(object?[] variables) {
        ulong mask = 0UL;
        ref object? pVar = ref MemoryMarshal.GetArrayDataReference(variables);
        for (int i = 0; i < variables.Length; i++)
            if (Unsafe.Add(ref pVar, i) is not null)
                mask |= 1UL << i;
        return mask;
    }
    public static ulong ToMask(Span<bool> usageMap) {
        ulong mask = 0U;
        for (int i = 0; i < usageMap.Length; i++)
            if (usageMap[i])
                mask |= 1UL << i;
        return mask;
    }
    public static bool Equals(ulong k1, ulong k2) => k1 == k2;
}
internal class Masker128 : IKeyMasker<Int128> {
    private static readonly Int128 One = (Int128)1;
    public static Int128 ToMask(object?[] variables) {
        Int128 mask = Int128.Zero;
        ref object? pVar = ref MemoryMarshal.GetArrayDataReference(variables);
        for (int i = 0; i < variables.Length; i++)
            if (Unsafe.Add(ref pVar, i) is not null)
                mask |= One << i;
        return mask;
    }
    public static Int128 ToMask(Span<bool> usageMap) {
        Int128 mask = Int128.Zero;
        for (int i = 0; i < usageMap.Length; i++)
            if (usageMap[i])
                mask |= One << i;
        return mask;
    }
    public static bool Equals(Int128 k1, Int128 k2) => k1 == k2;
}
internal readonly struct Int256(Int128 low, Int128 high) {
    public readonly Int128 Low = low;
    public readonly Int128 High = high;
}
internal class Masker256 : IKeyMasker<Int256> {
    private static readonly Int128 One = (Int128)1;
    public static Int256 ToMask(object?[] variables) {
        Int128 mask = Int128.Zero;
        ref object? pVar = ref MemoryMarshal.GetArrayDataReference(variables);
        var len = variables.Length - 128;
        for (int i = 0; i < 128; i++)
            if (Unsafe.Add(ref pVar, i) is not null)
                mask |= One << i;
        pVar = Unsafe.Add(ref pVar, 128);
        Int128 mask2 = Int128.Zero;
        for (int i = 0; i < len; i++)
            if (Unsafe.Add(ref pVar, i) is not null)
                mask2 |= One << i;
        return new(mask, mask2);
    }
    public static Int256 ToMask(Span<bool> usageMap) {
        Int128 mask = Int128.Zero;
        for (int i = 0; i < 128; i++)
            if (usageMap[i])
                mask |= One << i;
        Int128 mask2 = Int128.Zero;
        for (int i = 128; i < usageMap.Length; i++)
            if (usageMap[i])
                mask2 |= One << i;
        return new(mask, mask2);
    }
    public static bool Equals(Int256 k1, Int256 k2)
        => k1.Low == k2.Low && k1.High == k2.High;
}
internal unsafe class MaskerInfinite : IKeyMasker<ulong[]> {
    public static bool Equals(ulong[] k1, ulong[] k2)
        => k1.AsSpan().SequenceEqual(k2.AsSpan());
    public static ulong[] ToMask(object?[] variables) {
        int arraySize = (variables.Length + 63) >> 6;
        ulong[] data = new ulong[arraySize];

        ref object? pVar = ref MemoryMarshal.GetArrayDataReference(variables);

        // i >> 6 is index, i & 63 is bit position
        for (int i = 0; i < variables.Length; i++)
            if (Unsafe.Add(ref pVar, i) is not null)
                data[i >> 6] |= 1UL << (i & 63);
        return data;
    }
    public static ulong[] ToMask(Span<bool> usageMap) {
        int arraySize = (usageMap.Length + 63) >> 6;
        ulong[] data = new ulong[arraySize];

        for (int i = 0; i < usageMap.Length; i++)
            if (usageMap[i])
                data[i >> 6] |= 1UL << (i & 63);
        return data;
    }
}