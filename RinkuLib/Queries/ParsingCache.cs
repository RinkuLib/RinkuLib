#if NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#endif

namespace RinkuLib.Queries;
public abstract class ParsingCache {
    public static readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        SharedLock = new();
    public static ParsingCache? New(int nbSelects) {
        if (nbSelects <= 0)
            return null;
#if NET8_0_OR_GREATER
        if (nbSelects <= 32)
            return new ParsingCache<Masker32, uint>();
        if (nbSelects <= 64)
            return new ParsingCache<Masker64, ulong>();
        if (nbSelects <= 128)
            return new ParsingCache<Masker128, Int128>();
        if (nbSelects <= 256)
            return new ParsingCache<Masker256, Int256>();
        return new ParsingCache<MaskerInfinite, ulong[]>();
#else
        if (nbSelects <= 32)
            return new ParsingCache32();
        return new ParsingCacheInfinite();
#endif
    }
    public abstract bool TryGetCache<T>(object?[] variables, out ParsingCache<T> cache);
    public abstract int GetActualGetCacheIndex<T>(object?[] variables);
    public abstract bool UpdateCache<T>(int index, ParsingCache<T> cache);
}
#if NET8_0_OR_GREATER
public interface IKeyMasker<TMask> {
    public abstract static TMask ToMask(object?[] variables);
    public abstract static bool Equals(TMask k1, TMask k2);
}
public sealed class ParsingCache<TMasker, TMask> : ParsingCache where TMasker : IKeyMasker<TMask> {
    private IParsingCache[] Cache = [];
    private TMask[] Keys = [];
    public override bool TryGetCache<T>(object?[] variables, out ParsingCache<T> cache) {
        var mask = TMasker.ToMask(variables);
        var keys = Keys;
        for (int i = 0; i < keys.Length; i++)
            if (TMasker.Equals(keys[i], mask)) {
                if (Cache[i] is ParsingCache<T> c) {
                    cache = c;
                    return true;
                }
                cache = new(_ => throw new Exception($"There allready has an item cached for a different type Current:{Cache[i].GetType()} Target:{typeof(ParsingCache<T>)}"), default);
                return false;
            }
        cache = default; 
        return false;
    }
    public override int GetActualGetCacheIndex<T>(object?[] variables) {
        var mask = TMasker.ToMask(variables);
        var keys = Keys;
        lock (SharedLock) {
            for (int i = 0; i < keys.Length; i++)
                if (TMasker.Equals(keys[i], mask)) {
                    if (Cache[i] is ParsingCache<T>)
                        return i;
                    return -1;
                }
            if (Cache.Length <= 0) {
                Cache = new IParsingCache[1];
                Cache[0] = new ParsingCache<T>();
                return 0;
            }
            var len = Cache.Length;
            var c = new IParsingCache[len + 1];
            Array.Copy(Cache, c, len);
            Cache = c;
            Cache[len] = new ParsingCache<T>();
            var k = new TMask[len + 1];
            Array.Copy(Keys, k, len);
            Keys = k;
            Keys[len] = mask;
            return len;
        }
    }

    public override bool UpdateCache<T>(int index, ParsingCache<T> cache) {
        lock (SharedLock) {
            if (index == -1)
                throw new Exception("The item was allready cached using a different type");
            if (((ParsingCache<T>)Cache[index]).IsValid)
                return false;
            Cache[index] = cache;
            return true;
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
#else
public sealed class ParsingCache32 : ParsingCache {
    private IParsingCache[] Cache = [];
    private uint[] Keys = [];

    private uint ToMask(object?[] variables) {
        uint mask = 0;
        for (int i = 0; i < variables.Length; i++)
            if (variables[i] != null) mask |= 1U << i;
        return mask;
    }

    public override bool TryGetCache<T>(object?[] variables, out ParsingCache<T> cache) {
        uint mask = ToMask(variables);
        for (int i = 0; i < Keys.Length; i++) {
            if (Keys[i] == mask && Cache[i] is ParsingCache<T> c) {
                cache = c; return true;
            }
        }
        cache = default; return false;
    }

    public override int GetActualGetCacheIndex<T>(object?[] variables) {
        uint mask = ToMask(variables);
        lock (SharedLock) {
            for (int i = 0; i < Keys.Length; i++)
                if (Keys[i] == mask) return (Cache[i] is ParsingCache<T>) ? i : -1;

            int len = Cache.Length;
            Array.Resize(ref Cache, len + 1);
            Array.Resize(ref Keys, len + 1);
            Cache[len] = new ParsingCache<T>();
            Keys[len] = mask;
            return len;
        }
    }

    public override bool UpdateCache<T>(int index, ParsingCache<T> cache) {
        lock (SharedLock) {
            Cache[index] = cache;
            return true;
        }
    }
}

// Specific implementation for Infinite (ulong[])
public sealed class ParsingCacheInfinite : ParsingCache {
    private IParsingCache[] Cache = [];
    private ulong[][] Keys = new ulong[0][];

    private ulong[] ToMask(object?[] variables) {
        ulong[] data = new ulong[(variables.Length + 63) >> 6];
        for (int i = 0; i < variables.Length; i++)
            if (variables[i] != null) data[i >> 6] |= 1UL << (i & 63);
        return data;
    }

    public override bool TryGetCache<T>(object?[] variables, out ParsingCache<T> cache) {
        ulong[] mask = ToMask(variables);
        for (int i = 0; i < Keys.Length; i++) {
            if (Keys[i].SequenceEqual(mask) && Cache[i] is ParsingCache<T> c) {
                cache = c; return true;
            }
        }
        cache = default; return false;
    }

    public override int GetActualGetCacheIndex<T>(object?[] variables) {
        ulong[] mask = ToMask(variables);
        lock (SharedLock) {
            for (int i = 0; i < Keys.Length; i++)
                if (Keys[i].SequenceEqual(mask)) return (Cache[i] is ParsingCache<T>) ? i : -1;

            int len = Cache.Length;
            Array.Resize(ref Cache, len + 1);
            Array.Resize(ref Keys, len + 1);
            Cache[len] = new ParsingCache<T>();
            Keys[len] = mask;
            return len;
        }
    }

    public override bool UpdateCache<T>(int index, ParsingCache<T> cache) {
        lock (SharedLock) {
            Cache[index] = cache;
            return true;
        }
    }
}
#endif