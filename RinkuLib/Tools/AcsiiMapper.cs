using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if NET8_0_OR_GREATER
using System.Runtime.Intrinsics;
#endif

namespace RinkuLib.Tools;
#if NET7_0_OR_GREATER
public sealed unsafe class AsciiMapper<T> : Mapper where T : ICaseComparer {
#else
public sealed unsafe class AsciiMapper : Mapper {
#endif
    private uint* _steps;
    private int _lengthMask;
    private readonly int _maxSteps;

    public AsciiMapper(string[] keys, int lengthMask, uint[] steps, int maxSteps) : base(keys) {
        _lengthMask = lengthMask;
        _maxSteps = maxSteps;

        nuint stepsSize = (nuint)(steps.Length * sizeof(uint));
#if NET6_0_OR_GREATER
        _steps = (uint*)NativeMemory.AlignedAlloc(stepsSize, 64);
        fixed (uint* p = steps) {
            NativeMemory.Copy(p, _steps, stepsSize);
        }
#else
        // Standard 2.0 uses Marshal + Buffer.MemoryCopy
        _steps = (uint*)Marshal.AllocHGlobal((int)stepsSize);
        fixed (uint* p = steps) {
            Buffer.MemoryCopy(p, _steps, stepsSize, stepsSize);
        }
#endif
    }

    public override int GetIndex(string key) {
        if (key == null)
            return -1;
        int len = key.Length;
        fixed (char* keyPtr = key) {
            uint step = Navigate(keyPtr, len);
            if (step >= _keys.Length)
                return -1;

            string candidate = Unsafe.Add(ref KeysStartPtr, (nint)step);

            if (ReferenceEquals(candidate, key))
                return (int)step;

            if (candidate.Length == len &&
#if NET7_0_OR_GREATER
               T.Equals(keyPtr, candidate, len)
#else
        MemoryExtensions.Equals(
            new ReadOnlySpan<char>(keyPtr, len), 
            candidate.AsSpan(), 
            StringComparison.OrdinalIgnoreCase)
#endif
                )
                return (int)step;
            return -1;
        }
    }
    public override int GetIndex(ReadOnlySpan<char> key) {
        int len = key.Length;
        fixed (char* keyPtr = key) {
            uint step = Navigate(keyPtr, len);
            if (step >= _keys.Length)
                return -1;
            string candidate = Unsafe.Add(ref KeysStartPtr, (nint)step);

            if (candidate.Length == len &&
#if NET7_0_OR_GREATER
               T.Equals(keyPtr, candidate, len)
#else
        MemoryExtensions.Equals(
            new ReadOnlySpan<char>(keyPtr, len), 
            candidate.AsSpan(), 
            StringComparison.OrdinalIgnoreCase)
#endif
                )
                return (int)step;
            return -1;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint Navigate(char* keyPtr, int len) {
        uint* sPtr = _steps;
        uint step = sPtr[len & _lengthMask];
        if (step < MapperHelper.RESERVED)
            return step;
        step = sPtr[MapperHelper.GetIndexFromStep(step, keyPtr)];
        if (step < MapperHelper.RESERVED)
            return step;
        step = sPtr[MapperHelper.GetIndexFromStep(step, keyPtr)];
        if (step < MapperHelper.RESERVED)
            return step;
        step = sPtr[MapperHelper.GetIndexFromStep(step, keyPtr)];
        if (step < MapperHelper.RESERVED)
            return step;
        step = sPtr[MapperHelper.GetIndexFromStep(step, keyPtr)];
        if (step < MapperHelper.RESERVED)
            return step;
        step = sPtr[MapperHelper.GetIndexFromStep(step, keyPtr)];
        if (step < MapperHelper.RESERVED)
            return step;
        int remaining = _maxSteps - 5;
        while (step >= MapperHelper.RESERVED && --remaining >= 0)
            step = sPtr[MapperHelper.GetIndexFromStep(step, keyPtr)];
        return step;
    }
    protected override void DisposeUnmanaged() {
        uint* ptr = _steps;
        _steps = null;
        _lengthMask = 0;
        if (ptr is not null) {
#if NET6_0_OR_GREATER
            NativeMemory.AlignedFree(ptr);
#else
        System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)ptr);
#endif
        }
    }
}
#if NET8_0_OR_GREATER
public unsafe interface ICaseComparer {
    static abstract bool Equals(char* keyPtr, string candidate, int len);
}
public unsafe struct AsciiStrategy : ICaseComparer {

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool Equals(char* keyPtr, string candidate, int len) {
        fixed (char* cPtr = candidate) {
            int i = 0;

            // --- SIMD Path ---
            if (Vector128.IsHardwareAccelerated && len >= 8) {
                Vector128<ushort> caseBit = Vector128.Create((ushort)0x20);
                // We use lowercase range for normalization check
                Vector128<ushort> lowA = Vector128.Create((ushort)'a');
                Vector128<ushort> lowZ = Vector128.Create((ushort)'z');

                for (; i <= len - 8; i += 8) {
                    var v1 = Vector128.Load((ushort*)(keyPtr + i));
                    var v2 = Vector128.Load((ushort*)(cPtr + i));

                    if (v1 == v2)
                        continue;

                    // Normalize BOTH to lowercase
                    // (c | 0x20) only if it's a letter
                    var v1L = v1 | caseBit;
                    var v2L = v2 | caseBit;

                    // Safety: Ensure it's actually a letter
                    var isLetter = Vector128.GreaterThanOrEqual(v1L, lowA) &
                                   Vector128.LessThanOrEqual(v1L, lowZ);

                    // If it's a letter, compare normalized. If not, compare original.
                    var finalV1 = Vector128.ConditionalSelect(isLetter, v1L, v1);
                    var finalV2 = Vector128.ConditionalSelect(isLetter, v2L, v2);

                    if (finalV1 != finalV2)
                        return false;
                }
            }

            // --- Scalar Path ---
            for (; i < len; i++) {
                uint c1 = keyPtr[i];
                uint c2 = cPtr[i];
                if (c1 == c2)
                    continue;

                if ((c1 ^ c2) == 0x20) {
                    uint normalized = c1 | 0x20;
                    if (normalized >= 'a' && normalized <= 'z')
                        continue;
                }
                return false;
            }
            return true;
        }
    }
}

public unsafe struct UnicodeStrategy : ICaseComparer {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool Equals(char* keyPtr, string candidate, int len) {
        return new ReadOnlySpan<char>(keyPtr, len)
            .Equals(candidate, StringComparison.OrdinalIgnoreCase);
    }
}
#endif