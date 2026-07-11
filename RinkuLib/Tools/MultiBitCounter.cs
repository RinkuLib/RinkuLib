namespace RinkuLib.Tools; 
internal struct MultiBitCounter {
    public byte Bit4;
    public byte Bit8;
    public ushort Bit16;
    public uint Bit32;
    public ulong Bit64;
    public ulong Bit128Low;
    public ulong Bit128High;
    public void Set(int ind) {
        Bit4 |= (byte)(1 << (ind & 3));
        Bit8 |= (byte)(1 << (ind & 7));
        Bit16 |= (ushort)(1 << (ind & 15));
        Bit32 |= 1U << (ind & 31);
        Bit64 |= 1UL << (ind & 63);
        if ((ind & 127) < 64)
            Bit128Low |= 1UL << (ind & 127);
        else
            Bit128High |= 1UL << ((ind - 64) & 127);
    }
    public void Reset() {
        Bit4 = 0;
        Bit8 = 0;
        Bit16 = 0;
        Bit32 = 0;
        Bit64 = 0;
        Bit128Low = 0;
        Bit128High = 0;
    }
    public readonly BitCounter Best(bool prioSmaller = true) {
        BitCounter best = new(4, Bit4);
        if (best.Score() < byte.PopCount(Bit8))
            best = new BitCounter(8, Bit8);
        if (best.Score() < ushort.PopCount(Bit16))
            best = new BitCounter(16, Bit16);
        if (best.Score() < uint.PopCount(Bit32))
            best = new BitCounter(32, Bit32);
        var current = (int)ulong.PopCount(Bit64);
        if (prioSmaller && current >= 3)
            current--;
        if (best.Score() < current) {
            best = new BitCounter(64, Bit64);
            if (!prioSmaller)
                prioSmaller = true;
        }
        current = (int)(ulong.PopCount(Bit128Low) + ulong.PopCount(Bit128High));
        if (prioSmaller && current >= 3)
            current--;
        if (best.Score() < current)
            best = new BitCounter(128, Bit128Low, Bit128High);
        return best;
    }
}
