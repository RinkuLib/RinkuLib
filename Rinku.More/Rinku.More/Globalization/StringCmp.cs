namespace Rinku.More.Globalization;

internal static class StringCmp
{
    public static unsafe bool CultureEquals(this string value, string other)
    {
        if (value.Length != other.Length)
            return false;

        fixed (char* valuePointer = value)
        {
            return ReferenceEquals(value, other) || other.CultureEquals(valuePointer);
        }
    }

    public static unsafe bool CultureEquals(this string value, char* other)
    {
        var length = value.Length;
        fixed (char* valuePointer = value)
        {
            switch (length)
            {
                case 0:
                    return true;
                case 1:
                    return other[0] == valuePointer[0];
                case 2:
                    return *(uint*)other == *(uint*)valuePointer;
                case 3:
                    return *(uint*)other == *(uint*)valuePointer && other[2] == valuePointer[2];
                case 4:
                    return *(ulong*)other == *(ulong*)valuePointer;
                case 5:
                    return *(ulong*)other == *(ulong*)valuePointer && other[4] == valuePointer[4];
                case 6:
                    return *(ulong*)other == *(ulong*)valuePointer && *(uint*)(other + 4) == *(uint*)(valuePointer + 4);
                case 7:
                    return *(ulong*)other == *(ulong*)valuePointer && *(uint*)(other + 4) == *(uint*)(valuePointer + 4) && other[6] == valuePointer[6];
                case 8:
                    return *(ulong*)other == *(ulong*)valuePointer;
                case 9:
                    return *(ulong*)other == *(ulong*)valuePointer && other[8] == valuePointer[8];
                case 10:
                    return *(ulong*)other == *(ulong*)valuePointer && *(uint*)(other + 8) == *(uint*)(valuePointer + 8);
            }

            var first = (ulong*)other;
            var second = (ulong*)valuePointer;
            var fullBlocks = length / 4;

            for (var i = 0; i < fullBlocks; i++)
            {
                if (*first != *second)
                    return false;
                first++;
                second++;
            }

            var firstCharacters = (char*)first;
            var secondCharacters = (char*)second;
            for (var i = 0; i < length % 4; i++)
            {
                if (firstCharacters[i] != secondCharacters[i])
                    return false;
            }

            return true;
        }
    }
}
