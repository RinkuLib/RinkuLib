using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace RinkuLib.Tools;
internal static class MapperHelper {

    public const uint RESERVED = 0x00010000;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal unsafe static uint GetIndexFromStep(uint step, char* keyPtr) {
        uint charIdx = step >> 24;
        uint mask = (step >> 16) & 0xFF;
        uint offset = step & 0xFFFF;
        return (*(keyPtr + charIdx) & mask) + offset;
    }
}
internal struct AsciiMapperBuilder {
    internal string[] UsedKeys;
    internal uint[] Steps;
    internal int MaxReserved;
    internal readonly int LengthMask;
    internal int MaxDepth;
    private readonly MaskedCharComparer CharComparer;
    public readonly static BitCounter NoCounter = new();
    private static readonly Comparer<string>[] LengthComparers = [
        null!, null!,
        Comparer<string>.Create((a, b) => MakeKey(a, 3) - MakeKey(b, 3)),
        Comparer<string>.Create((a, b) => MakeKey(a, 7) - MakeKey(b, 7)),
        Comparer<string>.Create((a, b) => MakeKey(a, 15) - MakeKey(b, 15)),
        Comparer<string>.Create((a, b) => MakeKey(a, 31) - MakeKey(b, 31)),
        Comparer<string>.Create((a, b) => MakeKey(a, 63) - MakeKey(b, 63)),
        Comparer<string>.Create((a, b) => MakeKey(a, 127) - MakeKey(b, 127)),
        Comparer<string>.Create((a, b) => MakeKey(a, 255) - MakeKey(b, 255))
    ];
    private static int MakeKey(string s, int mask) {
        int len = s.Length;
        return ((len & mask) << 16) | (len & 0xFFFF);
    }
    private class MaskedCharComparer : IComparer<string> {
        public int CharIndex;
        public int Mask;
        public void Update(int CharIndex, int Mask) {
            this.CharIndex = CharIndex;
            this.Mask = Mask;
        }
        public int Compare(string? a, string? b) 
            => (ToLower(a![CharIndex]) & Mask) - (ToLower(b![CharIndex]) & Mask);
    }
    private static MaskedCharComparer? SharedComparer;
    internal AsciiMapperBuilder(Span<string> Keys) {
        var length = Keys.Length;
        if (length <= 1)
            throw new Exception();
        MaxDepth = -1;
        Steps = [];
        UsedKeys = ArrayPool<string>.Shared.Rent(length);
        LengthMask = StartSteps(Keys);
        CharComparer = Interlocked.Exchange(ref SharedComparer, null) ?? new MaskedCharComparer();
        var res = FillMapping(int.MaxValue, length, Encode(255, LengthMask, ushort.MaxValue));
        Interlocked.CompareExchange(ref SharedComparer, CharComparer, null);
        ArrayPool<string>.Shared.Return(UsedKeys);
        UsedKeys = null!;
        if (Steps.Length == 0)
            return;
        uint[] newArr = new uint[MaxReserved];
        Array.Copy(Steps, newArr, MaxReserved);
        ArrayPool<uint>.Shared.Return(Steps);
        Steps = newArr;
        if (!res)
            return;
        UsedKeys = CompleteSteps(Keys);
    }
    private int StartSteps(Span<string> Keys) {
        var bit256 = new BitCounter256();
        var multi = new MultiBitCounter();
        for (int i = 0; i < Keys.Length; i++) {
            var k = Keys[i] ?? throw new NullReferenceException("A key in the set was null");
            UsedKeys[i] = k;
            var l = k.Length;
            bit256.Set(l & 255);
            multi.Set(l);
        }
        var best = multi.Best(false);
        var need256 = best.Count() < bit256.Count();
        var mask = best.Length - 1;
        if (need256)
            mask = 255;
        var StepsLength = mask + 1;
        this.Steps = ArrayPool<uint>.Shared.Rent(StepsLength);
        Array.Clear(Steps, 0, Steps.Length);

        int log2;
        uint val = (uint)StepsLength;
#if NETCOREAPP3_0_OR_GREATER
        log2 = BitOperations.Log2(val);
#else
        log2 = 0;
        if (val >= 1 << 16) { val >>= 16; log2 |= 16; }
        if (val >= 1 << 8)  { val >>= 8;  log2 |= 8;  }
        if (val >= 1 << 4)  { val >>= 4;  log2 |= 4;  }
        if (val >= 1 << 2)  { val >>= 2;  log2 |= 2;  }
        if (val >= 1 << 1)  { log2 |= 1; }
#endif

        Array.Sort(UsedKeys, 0, Keys.Length, LengthComparers[log2]);
        if (need256) {
            Reserve(new(128, bit256.A, bit256.B), 0);
            Reserve(new(128, bit256.C, bit256.D), 128);
        }
        else
            Reserve(best, 0);
        return mask;
    }
    private readonly unsafe int GetTerminalStepIndex(string key, out bool hasAlt, out int depth) {
        hasAlt = false;
        var len = key.Length;
        fixed (char* p = key) {
            char* keyPtr = p;
            uint step = Steps[len & LengthMask];
            uint lastStep = 0;
            depth = 1;
            if (step <= MapperHelper.RESERVED)
                return len & LengthMask;
            while (step > MapperHelper.RESERVED) {
                lastStep = step;
                step = Steps[MapperHelper.GetIndexFromStep(step, keyPtr)];
                depth++;
            }
            var stepInd = (int)MapperHelper.GetIndexFromStep(lastStep, keyPtr);
            if ((lastStep >> 16 & 0xFF) > 32) {
                var charInd = lastStep >> 24;
                hasAlt = true;
                if ((uint)(*(p + charInd) - 'A') < 26)
                    stepInd += 32;
                else if ((uint)(*(p + charInd) - 'a') >= 26)
                    hasAlt = false;
            }
            return stepInd;
        }
    }
    private string[] CompleteSteps(Span<string> Keys) {
        var newKeys = ArrayPool<string>.Shared.Rent(Keys.Length);
        var newKeyIndex = 0U;
        for (var i = 0; i < Keys.Length; i++) {
            var key = Keys[i];
            var ind = GetTerminalStepIndex(key, out var hasInd, out var depth);
            if (Steps[ind] != MapperHelper.RESERVED)
                continue;
            if (depth > MaxDepth)
                MaxDepth = depth;
            if (hasInd)
                Steps[ind - 32] = newKeyIndex;
            Steps[ind] = newKeyIndex;
            newKeys[newKeyIndex++] = key;
        }
        var res = new string[newKeyIndex];
        Array.Copy(newKeys, 0, res, 0, (int)newKeyIndex);
        ArrayPool<string>.Shared.Return(newKeys);
        return res;
    }
    private static (int charInd, int mask, int decal) Decode(uint step)
        => ((int)(step >> 24), (int)((step >> 16) & 0xFF), (ushort)step);
#if !NETCOREAPP3_0_OR_GREATER
// This table must be present in the class for the legacy path
private static readonly byte[] DeBruijnLookup64 = [
    0,  1,  48, 2,  57, 49, 28, 3,  61, 58, 50, 42, 38, 29, 17, 4,
    62, 54, 59, 36, 51, 47, 43, 25, 41, 39, 33, 30, 24, 18, 12, 5,
    63, 47, 55, 44, 37, 26, 34, 13, 46, 45, 35, 14, 32, 23, 11, 6,
    60, 27, 21, 20, 15, 10, 9,  7,  31, 22, 19, 16, 8,  40, 53, 56,
    52
];
#endif
    private unsafe void Reserve(BitCounter cnt, int startInd) {
        var bits = cnt.Low;
        var start = startInd;
        if (startInd + cnt.Length > MaxReserved)
            MaxReserved = startInd + cnt.Length;
        while (true) {
            int b;
#if NETCOREAPP3_0_OR_GREATER
            b = System.Numerics.BitOperations.TrailingZeroCount(bits);
#else
        if (bits == 0) {
            b = 64;
        } else {
            b = DeBruijnLookup64[((bits & ~(bits - 1)) * 0x03F79D71B4CB0A89UL) >> 58];
        }
#endif
            Steps[start + b] = MapperHelper.RESERVED;
            bits &= bits - 1;
            if (bits <= 0) {
                if (cnt.Length > 64 && start == startInd && cnt.High > 0) {
                    bits = cnt.High;
                    start += 64;
                    continue;
                }
                break;
            }
        }
    }
    private static int ToLower(int c) {
        if (char.ToLowerInvariant((char)c) - c == 32)
            return c + 32;
        return c;
    }
    public bool FillMapping(int keysStart, int keysEnd, uint step) {
        var (charInd, mask, start) = Decode(step);
        var isLen = charInd == 255 && start == ushort.MaxValue && keysStart == int.MaxValue;
        if (isLen) 
            start = keysStart = 0;
        for (int i = 0; i <= mask; i++) {
            var nb = 0;
            for (int t = keysStart; t < keysEnd; t++) {
                if (((isLen ? UsedKeys[t].Length : ToLower(UsedKeys[t][charInd])) & mask) != i)
                    break;
                nb++;
            }
            if (nb == 0)
                continue;
            if (nb == 1) {
                keysStart++;
                if (keysStart == keysEnd)
                    return true;
                continue;
            }
            uint subStep = MakeStep(keysStart, nb);
            if (subStep == MapperHelper.RESERVED) {
                keysStart += nb;
                if (keysStart == keysEnd)
                    return true;
                continue;
            }
            if (subStep == 0)
                return false;
            Steps[start + i] = subStep;
            if (!FillMapping(keysStart, keysStart + nb, subStep))
                return false;
            keysStart += nb;
            if (keysStart == keysEnd)
                return true;
        }
        return false;
    }
    private uint MakeStep(int keysStart, int nb) {
        var (counter, charIndex) = GetBestBitCounter(keysStart, nb);
        if (counter.Score() <= 1)
            return AllEqualIgnoreCase(UsedKeys.AsSpan(keysStart, nb)) ? MapperHelper.RESERVED : 0;
        var mask = counter.Length - 1;
        CharComparer.Update(charIndex, mask);
        Array.Sort(UsedKeys, keysStart, nb, CharComparer);
        if (!counter.SetUppercase(GetLettersMask(keysStart, keysStart + nb, charIndex)))
            return 0;
        var start = GetStart(counter);
        Reserve(counter, start);
        var step = Encode(charIndex, mask, start);
        return step;
    }
    private static bool AllEqualIgnoreCase(ReadOnlySpan<string> span) {
        if (span.Length < 2)
            return true;
        string first = span[0];
        int len = first.Length;
        for (int i = 1; i < span.Length; i++) {
            string s = span[i];
            if (ReferenceEquals(s, first))
                continue;
            if (s == null || s.Length != len)
                return false;
            var a = first.AsSpan();
            var b = s.AsSpan();
            for (int j = 0; j < len; j++) {
                char c1 = a[j];
                char c2 = b[j];
                if (c1 != c2 && ToLower(c1) != ToLower(c2))
                    return false;
            }
        }
        return true;
    }
    private static uint Encode(int charInd, int mask, int start) {
        return ((uint)charInd << 24)
                          | (((uint)mask) << 16)
                          | (ushort)start;
    }
    private readonly uint GetLettersMask(int keysStart, int keysEnd, int charInd) {
        var lowerMask = 0U;
        for (var i = keysStart; i < keysEnd; i++) {
            var c = UsedKeys[i][charInd];
            if ((uint)(c - 'a') < 26 || (uint)(c - 'A') < 26)
                lowerMask |= 1U << (c & ~32);
        }
        return lowerMask;
    }
    private readonly (BitCounter, int) GetBestBitCounter(int keysStart, int nb) {
        var keyLengths = UsedKeys[keysStart].Length & 255;
        var end = keysStart + nb;
        var multi = new MultiBitCounter();
        BitCounter best = NoCounter;
        var bestInd = -1;
        for (int ind = 0; ind < keyLengths; ind++) {
            for (int j = keysStart; j < end; j++) {
                var c = UsedKeys[j][ind];
                var lowerC = char.ToLowerInvariant(c);
                var upperC = char.ToUpperInvariant(c);
                if (upperC != lowerC && lowerC - upperC != 32) {
                    multi.Reset();
                    break;
                }
                multi.Set(lowerC);
            }
            var b = multi.Best();
            var cnt = b.Score();
            if (cnt > best.Score() || (cnt == best.Score() && b.Length < best.Length)) {
                best = b;
                bestInd = ind;
            }
            multi.Reset();
        }
        return (best, bestInd);
    }
    private int GetStart(BitCounter counter) {
        int len = Steps.Length;
        for (int start = 0; ; start++) {
            int required = start + counter.Length;
            if (required > len) {
                int newLen = len * 2;
                if (newLen < required)
                    newLen = required;
                ResizeRented(ref Steps, newLen);
                len = Steps.Length;
            }
            var localStart = start;
            ulong bits = counter.Low;
            while (true) {
                int b;
#if NETCOREAPP3_0_OR_GREATER
                b = System.Numerics.BitOperations.TrailingZeroCount(bits);
#else
                if (bits == 0) {
                    b = 64;
                } else {
                    b = DeBruijnLookup64[((bits & ~(bits - 1)) * 0x03F79D71B4CB0A89UL) >> 58];
                }
#endif
                if (Steps[localStart + b] >= MapperHelper.RESERVED)
                    goto NEXT;
                bits &= bits - 1;
                if (bits <= 0) {
                    if (counter.High != 0 && localStart == start) {
                        bits = counter.High;
                        localStart += 64;
                        continue;
                    }
                    break;
                }
            }

            return start;

        NEXT:
            continue;
        }
    }
    private static void ResizeRented<T>(ref T[] arr, int newLen) {
        var oldLen = arr.Length;
        var newArr = ArrayPool<T>.Shared.Rent(newLen);
        arr.AsSpan().CopyTo(newArr);
        if (newLen > oldLen)
            newArr.AsSpan(oldLen).Clear();
        ArrayPool<T>.Shared.Return(arr);
        arr = newArr;
    }
}
