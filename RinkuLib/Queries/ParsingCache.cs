using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RinkuLib.Queries; 
public abstract class ParsingCache {
    public static readonly Lock SharedLock = new();
    public static ParsingCache? New(int nbSelects) {
        if (nbSelects <= 0)
            return null;
        if (nbSelects <= 32)
            return new DynamicQueryCache<Masker32, uint>();
        if (nbSelects <= 64)
            return new DynamicQueryCache<Masker64, ulong>();
        if (nbSelects <= 128)
            return new DynamicQueryCache<Masker128, Int128>();
        if (nbSelects <= 256)
            return new DynamicQueryCache<Masker256, Int256>();
        return new DynamicQueryCache<MaskerInfinite, ulong[]>();
    }
    public abstract void GetCacheAndParser<T>(object?[] variables, out Func<DbDataReader, T>? parser, out CommandBehavior behavior, out IParserCache? cache);
}
internal sealed class SingleItemCache : ParsingCache, IParserCache {
    public object? MethodFunc;
    public CommandBehavior DefaultBehavior;
    public override void GetCacheAndParser<T>(object?[] variables, out Func<DbDataReader, T>? parser, out CommandBehavior behavior, out IParserCache? cache) {
        if (MethodFunc is null) {
            behavior = default;
            cache = this;
            parser = null;
            return;
        }
        parser = MethodFunc as Func<DbDataReader, T>;
        behavior = parser is null ? default : DefaultBehavior;
        cache = null;
    }
    public void UpdateCache<T>(DbDataReader reader, IDbCommand cmd, Func<DbDataReader, T>? parsingFunc, CommandBehavior behavior) {
        if (MethodFunc is not null)
            return;
        lock (SharedLock) {
            if (MethodFunc is null) {
                MethodFunc = parsingFunc;
                DefaultBehavior = behavior;
            }
        }
    }
}
public interface IKeyMasker<TMask> {
    public abstract static TMask ToMask(object?[] variables);
    public abstract static bool Equals(TMask k1, TMask k2);
}
internal sealed class DynamicQueryCache<TMasker, TMask> : ParsingCache where TMasker : IKeyMasker<TMask> {
    private SingleItemCache[] Cache = [];
    private TMask[] Keys = [];
    public override void GetCacheAndParser<T>(object?[] variables, out Func<DbDataReader, T>? parser, out CommandBehavior behavior, out IParserCache? cache) {
        var mask = TMasker.ToMask(variables);
        var keys = Keys;
        for (int i = 0; i < keys.Length; i++)
            if (TMasker.Equals(keys[i], mask)) {
                Cache[i].GetCacheAndParser<T>(variables, out parser, out behavior, out cache);
                return;
            }
        lock (SharedLock) {
            if (Cache.Length > 0 || Cache[^1].MethodFunc is null) {
                GetCacheAndParser(variables, out parser, out behavior, out cache);
                return;
            }
            behavior = default;
            parser = null;
            cache = new SingleItemCache();
            Cache = [.. Cache, (SingleItemCache)cache];
            Keys = [.. Keys, mask];
        }
    }
}
public class Masker32 : IKeyMasker<uint> {
    public static uint ToMask(object?[] variables) {
        uint mask = 0U;
        ref object? pVar = ref MemoryMarshal.GetArrayDataReference(variables);
        for (int i = 0; i < variables.Length; i++)
            if (Unsafe.Add(ref pVar, i) is not null)
                mask |= 1U << i;
        return mask;
    }
    public static bool Equals(uint k1, uint k2) => k1 == k2;
}
public class Masker64 : IKeyMasker<ulong> {
    public static ulong ToMask(object?[] variables) {
        ulong mask = 0UL;
        ref object? pVar = ref MemoryMarshal.GetArrayDataReference(variables);
        for (int i = 0; i < variables.Length; i++)
            if (Unsafe.Add(ref pVar, i) is not null)
                mask |= 1UL << i;
        return mask;
    }
    public static bool Equals(ulong k1, ulong k2) => k1 == k2;
}
public class Masker128 : IKeyMasker<Int128> {
    private static readonly Int128 One = (Int128)1;
    public static Int128 ToMask(object?[] variables) {
        Int128 mask = Int128.Zero;
        ref object? pVar = ref MemoryMarshal.GetArrayDataReference(variables);
        for (int i = 0; i < variables.Length; i++)
            if (Unsafe.Add(ref pVar, i) is not null)
                mask |= One << i;
        return mask;
    }
    public static bool Equals(Int128 k1, Int128 k2) => k1 == k2;
}
public readonly struct Int256(Int128 low, Int128 high) {
    public readonly Int128 Low = low;
    public readonly Int128 High = high;
}
public class Masker256 : IKeyMasker<Int256> {
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
    public static bool Equals(Int256 k1, Int256 k2)
        => k1.Low == k2.Low && k1.High == k2.High;
}
public unsafe class MaskerInfinite : IKeyMasker<ulong[]> {
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
}