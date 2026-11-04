using System.Runtime.CompilerServices;

namespace Rinku.More.Globalization;

/// <summary>
/// Interned ordered sets of culture names shared by words with the same layout.
/// </summary>
public sealed class CulturesKey
{
    private readonly int _twoLetterEnd;
    private readonly string[] _cultures;

    public static readonly CulturesKey Empty = new([]);

    private static readonly List<CulturesKey> Keys = [Empty];
    private static readonly Lock KeysLock = new();

    private CulturesKey(string[] cultures)
    {
        _cultures = cultures;

        var index = 0;
        while (index < cultures.Length && cultures[index].Length == 2)
            index++;
        _twoLetterEnd = index;

        while (index < cultures.Length && cultures[index].Length == 5)
            index++;
        CommonEnd = index;

        HasInvariant = cultures.Length > CommonEnd && cultures[CommonEnd].Length == 0;
    }

    public int CommonEnd { get; }
    public bool HasInvariant { get; }
    public int Count => _cultures.Length;
    public ReadOnlySpan<string> Cultures => _cultures;

    public unsafe int IndexOf(string culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var length = culture.Length;
        fixed (char* culturePointer = culture)
        {
            if (length == 2)
            {
                for (var i = 0; i < _twoLetterEnd; i++)
                {
                    fixed (char* candidatePointer = _cultures[i])
                    {
                        if (*(uint*)culturePointer == *(uint*)candidatePointer)
                            return i;
                    }
                }

                return -1;
            }

            if (length == 5)
            {
                for (var i = _twoLetterEnd; i < CommonEnd; i++)
                {
                    fixed (char* candidatePointer = _cultures[i])
                    {
                        if (*(ulong*)culturePointer == *(ulong*)candidatePointer &&
                            culturePointer[4] == candidatePointer[4])
                            return i;
                    }
                }

                return -1;
            }

            for (var i = CommonEnd; i < _cultures.Length; i++)
            {
                if (_cultures[i].Length == length && _cultures[i].CultureEquals(culturePointer))
                    return i;
            }

            return -1;
        }
    }

    internal static CulturesKey Add(CulturesKey key, string culture, out int index)
    {
        lock (KeysLock)
        {
            index = 0;
            while (index < key._cultures.Length && GoesBefore(key._cultures[index], culture))
                index++;

            foreach (var candidate in Keys)
            {
                if (candidate.MatchesWithAddedCulture(key, culture, index))
                    return candidate;
            }

            var cultures = new string[key._cultures.Length + 1];
            Array.Copy(key._cultures, 0, cultures, 0, index);
            cultures[index] = culture;
            Array.Copy(key._cultures, index, cultures, index + 1, key._cultures.Length - index);
            return AddNewKey(cultures);
        }
    }

    internal static CulturesKey Remove(CulturesKey key, int index)
    {
        lock (KeysLock)
        {
            foreach (var candidate in Keys)
            {
                if (candidate.MatchesWithRemovedCulture(key, index))
                    return candidate;
            }

            var cultures = new string[key._cultures.Length - 1];
            Array.Copy(key._cultures, 0, cultures, 0, index);
            Array.Copy(key._cultures, index + 1, cultures, index, cultures.Length - index);
            return AddNewKey(cultures);
        }
    }

    internal static CulturesKey GetCulturesKey(string[] orderedCultures)
    {
        lock (KeysLock)
            return Intern(orderedCultures);
    }

    internal static bool GoesBefore(string left, string right)
    {
        var leftPriority = Prioritize(left.Length);
        var rightPriority = Prioritize(right.Length);
        return leftPriority < rightPriority ||
            (leftPriority == rightPriority && IsLess(left, right));
    }

    private static CulturesKey Intern(string[] cultures)
    {
        for (var i = 1; i < cultures.Length; i++)
        {
            if (!GoesBefore(cultures[i - 1], cultures[i]))
                throw new ArgumentException("Cultures must be ordered and unique.", nameof(cultures));
        }

        foreach (var key in Keys)
        {
            if (key._cultures.Length != cultures.Length)
                continue;

            var matches = true;
            for (var i = 0; i < cultures.Length; i++)
            {
                if (!key._cultures[i].CultureEquals(cultures[i]))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return key;
        }

        var newKey = new CulturesKey(cultures);
        Keys.Add(newKey);
        return newKey;
    }

    private static CulturesKey AddNewKey(string[] cultures)
    {
        var newKey = new CulturesKey(cultures);
        Keys.Add(newKey);
        return newKey;
    }

    private bool MatchesWithAddedCulture(CulturesKey source, string culture, int insertionIndex)
    {
        if (_cultures.Length != source._cultures.Length + 1)
            return false;

        for (var i = 0; i < insertionIndex; i++)
        {
            if (!_cultures[i].CultureEquals(source._cultures[i]))
                return false;
        }

        if (!_cultures[insertionIndex].CultureEquals(culture))
            return false;

        for (var i = insertionIndex; i < source._cultures.Length; i++)
        {
            if (!_cultures[i + 1].CultureEquals(source._cultures[i]))
                return false;
        }

        return true;
    }

    private bool MatchesWithRemovedCulture(CulturesKey source, int removedIndex)
    {
        if (_cultures.Length != source._cultures.Length - 1)
            return false;

        for (var i = 0; i < removedIndex; i++)
        {
            if (!_cultures[i].CultureEquals(source._cultures[i]))
                return false;
        }

        for (var i = removedIndex; i < _cultures.Length; i++)
        {
            if (!_cultures[i].CultureEquals(source._cultures[i + 1]))
                return false;
        }

        return true;
    }

    private static bool IsLess(string left, string right)
    {
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] < right[i])
                return true;
            if (left[i] > right[i])
                return false;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Prioritize(int length) => length == 2 ? -2 : length == 5 ? -1 : length;
}
