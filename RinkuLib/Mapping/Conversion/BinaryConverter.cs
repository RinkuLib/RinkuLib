namespace Rinku.Mapping.Conversion;

#if DEBUG
internal static class BinaryConverter {
    public static string ConvertBinary(this long bits) => ConvertBinary((ulong)bits);
    public static string ConvertBinary(this int bits) => ConvertBinary((uint)bits);
    public static string ConvertBinary(this short bits) => ConvertBinary((ushort)bits);
    public static string ConvertBinary(this char bits) => ConvertBinary((ushort)bits);
    public static string ConvertBinary(this sbyte bits) => ConvertBinary((byte)bits);
    public static string ConvertBinary(this ulong bits) {
        Span<char> buf = stackalloc char[71];
        int group = 8;
        for (int i = 70; ; i--) {
            buf[i] = (char)('0' + (bits & 1));
            bits >>= 1;
            if (--group == 0) {
                if (i == 0)
                    break;
                buf[--i] = ' ';
                group = 8;
            }
        }
        return new string(buf);
    }
    public static string ConvertBinary(this uint bits) {
        Span<char> buf = stackalloc char[35];
        int group = 8;
        for (int i = 34; ; i--) {
            buf[i] = (char)('0' + (bits & 1));
            bits >>= 1;
            if (--group == 0) {
                if (i == 0)
                    break;
                buf[--i] = ' ';
                group = 8;
            }
        }
        return new string(buf);
    }
    public static string ConvertBinary(this ushort bits) {
        Span<char> buf = stackalloc char[17];
        int group = 8;
        for (int i = 16; ; i--) {
            buf[i] = (char)('0' + (bits & 1));
            bits >>= 1;
            if (--group == 0) {
                if (i == 0)
                    break;
                buf[--i] = ' ';
                group = 8;
            }
        }
        return new string(buf);
    }
    public static string ConvertBinary(this byte bits)
        => $"{bits >> 7 & 1}{bits >> 6 & 1}{bits >> 5 & 1}{bits >> 4 & 1}{bits >> 3 & 1}{bits >> 2 & 1}{bits >> 1 & 1}{bits & 1}";
}
#endif
