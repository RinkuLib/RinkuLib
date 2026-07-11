using System.Runtime.CompilerServices;

namespace RinkuLib.Tools;

internal struct BitCounter(byte Length, ulong Low, ulong High = 0) {
    public int Length = Length;
    public ulong Low = Low;
    public ulong High = High;
    public bool SetUppercase(uint letterMask) {
        if (Length <= 32)
            return true;
        ref ulong bits = ref (Length == 128 ? ref High : ref Low);
        var invUppers = ~(bits & 0x0000000007FFFFFE);
        if ((invUppers | letterMask) != invUppers)
            return false;
        bits |= letterMask;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int Score() {
        var cnt = (int)ulong.PopCount(Low);
        if (Length == 128) {
            cnt += (int)ulong.PopCount(High);
            if (cnt > 3)
                cnt -= 2;
            return cnt;
        }
        if (Length == 64 && cnt > 2)
            cnt--;
        return cnt;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int Count() {
        var cnt = (int)ulong.PopCount(Low);
        if (Length == 128)
            cnt += (int)ulong.PopCount(High);
        return cnt;
    }
}
internal struct BitCounter256 {
    public ulong A;
    public ulong B;
    public ulong C;
    public ulong D;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(int index) {
        if (index < 64) {
            A |= 1UL << index;
            return;
        }
        index -= 64;

        if (index < 64) {
            B |= 1UL << index;
            return;
        }
        index -= 64;

        if (index < 64) {
            C |= 1UL << index;
            return;
        }
        index -= 64;

        D |= 1UL << index;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int Count()
        => (int)(ulong.PopCount(A) + 
        ulong.PopCount(B) + 
        ulong.PopCount(C) + 
        ulong.PopCount(D));
}