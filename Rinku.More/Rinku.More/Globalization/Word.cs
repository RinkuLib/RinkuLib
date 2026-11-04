using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Rinku.Tracking;

namespace Rinku.More.Globalization;

/// <summary>
/// Culture-specific text stored as an interned culture layout plus a parallel translation array.
/// </summary>
public sealed class Word : IEnumerable<KeyValuePair<CultureInfo, string>>, ITrackingValue<Word>
{
    private static readonly string[] EmptyTranslations = [];
    private const int StackallocThreshold = 256;
    private static CultureInfo _defaultCulture = CultureInfo.InvariantCulture;
    private static string _notSupported = "NotSupported";

    private CulturesKey _culturesKey = CulturesKey.Empty;
    private string[] _translations = EmptyTranslations;

    /// <summary>Optional catalog identity. Ordinary and SQL-parsed words carry literal translations.</summary>
    public string? Key { get; private init; }
    public static Word FromKey(string key) => new(key) { Key = key };

    public Word() { }

    public Word(string translation)
    {
        ArgumentNullException.ThrowIfNull(translation);
        _culturesKey = CulturesKey.GetCulturesKey([CultureInfo.InvariantCulture.Name]);
        _translations = [translation];
    }

    public Word(IDictionary<CultureInfo, string> translations)
    {
        ArgumentNullException.ThrowIfNull(translations);

        if (translations.Count == 0)
            return;

        var entries = new Entry[translations.Count];
        var index = 0;
        foreach (var translation in translations)
        {
            ArgumentNullException.ThrowIfNull(translation.Key);
            ArgumentNullException.ThrowIfNull(translation.Value);
            entries[index++] = new Entry(translation.Key.Name, translation.Value);
        }

        SetEntries(entries, nameof(translations));
    }

    public Word(params ReadOnlySpan<KeyValuePair<CultureInfo, string>> translations)
    {
        if (translations.Length == 0)
            return;

        var entries = new Entry[translations.Length];
        for (var i = 0; i < translations.Length; i++)
        {
            KeyValuePair<CultureInfo, string> translation = translations[i];
            ArgumentNullException.ThrowIfNull(translation.Key);
            ArgumentNullException.ThrowIfNull(translation.Value);
            entries[i] = new Entry(translation.Key.Name, translation.Value);
        }

        SetEntries(entries, nameof(translations));
    }
    private Word(CulturesKey culturesKey, string[] translations)
    {
        _culturesKey = culturesKey;
        _translations = translations;
    }

    public Word Copy() => new(_culturesKey, [.. _translations]) { Key = Key };
    public bool Equals(Word? other) => ReferenceEquals(this, other) || other is not null && Key == other.Key && ReferenceEquals(_culturesKey, other._culturesKey) && _translations.AsSpan().SequenceEqual(other._translations);
    public override bool Equals(object? other) => other is Word word && Equals(word);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Key, StringComparer.Ordinal);
        hash.Add(_culturesKey);
        foreach (string translation in _translations)
        {
            hash.Add(translation, StringComparer.Ordinal);
        }
        return hash.ToHashCode();
    }

    public static string CurrentCulture => CultureInfo.CurrentCulture.Name;

    public static CultureInfo DefaultCulture
    {
        get => _defaultCulture;
        set => _defaultCulture = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Text returned only when a word has no translations at all.
    /// </summary>
    public static string NotSupported
    {
        get => _notSupported;
        set => _notSupported = value ?? throw new ArgumentNullException(nameof(value));
    }

    public int Count => _translations.Length;
    public CulturesKey CulturesKey => _culturesKey;
    public ReadOnlySpan<string> Translations => _translations;

    public bool IsValid() => _translations.Length != 0;

    public string this[CultureInfo culture]
    {
        get => GetTranslation(culture);
        set => Add(culture, value);
    }

    public string this[string culture]
    {
        get => GetTranslation(ToCultureInfo(culture));
        set => Add(ToCultureInfo(culture), value);
    }

    public static implicit operator Word(string translation) => new(translation);

    [return: NotNullIfNotNull(nameof(word))]
    public static implicit operator string?(Word? word) => word?.ToString();

    public static Word New(IDictionary<CultureInfo, string> translations) => new(translations);

    internal static Word FromOrderedTranslations(string[] cultures, string[] translations)
    {
        ArgumentNullException.ThrowIfNull(cultures);
        ArgumentNullException.ThrowIfNull(translations);
        if (cultures.Length != translations.Length)
            throw new ArgumentException("Cultures and translations must have the same length.");

        return cultures.Length == 0
            ? []
            : new Word(CulturesKey.GetCulturesKey(cultures), translations);
    }

    internal static Word FromUnorderedTranslations(string[] cultures, string[] translations)
    {
        ArgumentNullException.ThrowIfNull(cultures);
        ArgumentNullException.ThrowIfNull(translations);
        if (cultures.Length != translations.Length)
            throw new ArgumentException("Cultures and translations must have the same length.");

        var entries = new Entry[cultures.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(cultures[i]);
            ArgumentNullException.ThrowIfNull(translations[i]);
            entries[i] = new Entry(cultures[i], translations[i]);
        }

        var word = new Word();
        word.SetEntries(entries, nameof(cultures));
        return word;
    }

    private void SetEntries(Entry[] entries, string parameterName)
    {
        if (entries.Length == 0) return;
        Array.Sort(entries, EntryComparer.Instance);
        for (var i = 1; i < entries.Length; i++)
            if (entries[i - 1].Culture.CultureEquals(entries[i].Culture))
                throw new ArgumentException("A translation already exists for this culture.", parameterName);

        var cultures = new string[entries.Length];
        _translations = new string[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            cultures[i] = entries[i].Culture;
            _translations[i] = entries[i].Translation;
        }
        _culturesKey = CulturesKey.GetCulturesKey(cultures);
    }

    internal static Word FromCulturesKey(CulturesKey culturesKey, string[] translations)
    {
        ArgumentNullException.ThrowIfNull(culturesKey);
        ArgumentNullException.ThrowIfNull(translations);
        if (culturesKey.Count != translations.Length)
            throw new ArgumentException("Cultures and translations must have the same length.", nameof(translations));

        return translations.Length == 0 ? [] : new Word(culturesKey, translations);
    }

    public void Add(CultureInfo culture, string translation)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(translation);

        var index = _culturesKey.IndexOf(culture.Name);
        if (index >= 0)
        {
            _translations[index] = translation;
            return;
        }

        AddCore(culture.Name, translation);
    }

    /// <summary>
    /// Adds or replaces a translation using an IETF culture name. An empty name is the invariant culture.
    /// </summary>
    public void Add(string culture, string translation)
    {
        ArgumentNullException.ThrowIfNull(culture);
        Add(ToCultureInfo(culture), translation);
    }

    public bool ContainsKey(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return _culturesKey.IndexOf(culture.Name) >= 0;
    }

    public bool TryGetValue(CultureInfo culture, [NotNullWhen(true)] out string? translation)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var index = _culturesKey.IndexOf(culture.Name);
        if (index < 0)
        {
            translation = null;
            return false;
        }

        translation = _translations[index];
        return true;
    }

    public bool Remove(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var index = _culturesKey.IndexOf(culture.Name);
        if (index < 0)
            return false;

        var translations = new string[_translations.Length - 1];
        Array.Copy(_translations, 0, translations, 0, index);
        Array.Copy(_translations, index + 1, translations, index, translations.Length - index);
        _culturesKey = CulturesKey.Remove(_culturesKey, index);
        _translations = translations.Length == 0 ? EmptyTranslations : translations;
        return true;
    }

    public void Clear()
    {
        _culturesKey = CulturesKey.Empty;
        _translations = EmptyTranslations;
    }

    public override string ToString() => GetTranslation(CultureInfo.CurrentCulture);

    public string GetTranslation(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        if (TryGetTranslationInHierarchy(culture, out var translation))
            return translation;

        var defaultCulture = DefaultCulture;
        if (!defaultCulture.Equals(CultureInfo.InvariantCulture) &&
            TryGetTranslationInHierarchy(defaultCulture, out translation))
            return translation;

        return _translations.Length == 0 ? NotSupported : _translations[0];
    }

    public bool CheckValidFormat(int expectedCount, bool perfectMatch = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedCount);

        Span<bool> paramsFound = expectedCount <= StackallocThreshold
            ? stackalloc bool[expectedCount]
            : new bool[expectedCount];
        foreach (var translation in _translations)
        {
            if (!translation.CheckValidFormat(paramsFound, perfectMatch))
                return false;
        }

        return true;
    }

    public static Word operator +(Word word, string suffix)
    {
        ArgumentNullException.ThrowIfNull(word);
        ArgumentNullException.ThrowIfNull(suffix);

        var translations = new string[word._translations.Length];
        for (var i = 0; i < translations.Length; i++)
            translations[i] = word._translations[i] + suffix;
        return new Word(word._culturesKey, translations);
    }

    public static Word operator +(string prefix, Word word)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(word);

        var translations = new string[word._translations.Length];
        for (var i = 0; i < translations.Length; i++)
            translations[i] = prefix + word._translations[i];
        return new Word(word._culturesKey, translations);
    }

    public static Word operator +(Word word1, Word word2)
    {
        ArgumentNullException.ThrowIfNull(word1);
        ArgumentNullException.ThrowIfNull(word2);

        return Combine(word1, word2, static (left, right, _) => left + right);
    }

    public static Word Format(Word word, params object?[] values)
    {
        ArgumentNullException.ThrowIfNull(word);
        ArgumentNullException.ThrowIfNull(values);

        return FormatCore(word, values);
    }

    /// <summary>
    /// Formats an interpolated string while preserving the translations of any
    /// <see cref="Word"/> values used as interpolation arguments.
    /// </summary>
    public static Word Interpolate(FormattableString format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return FormatCore(new Word(format.Format), format.GetArguments());
    }

    public static Word Format(Word word1, Word word2)
    {
        ArgumentNullException.ThrowIfNull(word1);
        ArgumentNullException.ThrowIfNull(word2);

        return Combine(word1, word2, static (format, value, culture) => string.Format(culture, format, value));
    }

    public Enumerator GetEnumerator() => new(_culturesKey, _translations);

    IEnumerator<KeyValuePair<CultureInfo, string>> IEnumerable<KeyValuePair<CultureInfo, string>>.GetEnumerator() =>
        new Enumerator(_culturesKey, _translations);

    IEnumerator IEnumerable.GetEnumerator() => new Enumerator(_culturesKey, _translations);

    private void AddCore(string culture, string translation)
    {
        var newKey = CulturesKey.Add(_culturesKey, culture, out var index);
        var translations = new string[_translations.Length + 1];
        Array.Copy(_translations, 0, translations, 0, index);
        translations[index] = translation;
        Array.Copy(_translations, index, translations, index + 1, _translations.Length - index);
        _culturesKey = newKey;
        _translations = translations;
    }

    private bool TryGetExactTranslation(string culture, [NotNullWhen(true)] out string? translation)
    {
        var index = _culturesKey.IndexOf(culture);
        if (index < 0)
        {
            translation = null;
            return false;
        }

        translation = _translations[index];
        return true;
    }

    private bool TryGetTranslationInHierarchy(CultureInfo culture, [NotNullWhen(true)] out string? translation)
    {
        for (var candidate = culture; ; candidate = candidate.Parent)
        {
            if (TryGetExactTranslation(candidate.Name, out translation))
                return true;
            if (candidate.Equals(CultureInfo.InvariantCulture))
                break;
        }

        translation = null;
        return false;
    }

    private static Word FormatCore(Word word, object?[] values)
    {
        var hasLocalizedValue = false;
        foreach (var value in values)
        {
            if (value is Word)
            {
                hasLocalizedValue = true;
                break;
            }
        }

        if (!hasLocalizedValue)
            return FormatWithoutLocalizedValues(word, values);

        var cultures = word._culturesKey.Cultures.ToArray();
        foreach (var value in values)
        {
            if (value is Word localizedValue)
                cultures = MergeCultures(cultures, localizedValue._culturesKey.Cultures);
        }

        var localizedValues = new object?[values.Length];
        Array.Copy(values, localizedValues, values.Length);

        var translations = new string[cultures.Length];
        for (var cultureIndex = 0; cultureIndex < cultures.Length; cultureIndex++)
        {
            var culture = ToCultureInfo(cultures[cultureIndex]);
            for (var valueIndex = 0; valueIndex < localizedValues.Length; valueIndex++)
            {
                if (values[valueIndex] is Word value)
                    localizedValues[valueIndex] = value.GetTranslation(culture);
            }

            translations[cultureIndex] = string.Format(culture, word.GetTranslation(culture), localizedValues);
        }

        return new Word(CulturesKey.GetCulturesKey(cultures), translations);
    }

    private static Word Combine(Word word1, Word word2, Func<string, string, CultureInfo, string> operation)
    {
        if (ReferenceEquals(word1._culturesKey, word2._culturesKey))
        {
            var sameTranslations = new string[word1._translations.Length];
            var sameCultures = word1._culturesKey.Cultures;
            for (var i = 0; i < sameTranslations.Length; i++)
            {
                var culture = ToCultureInfo(sameCultures[i]);
                sameTranslations[i] = operation(word1._translations[i], word2._translations[i], culture);
            }

            return new Word(word1._culturesKey, sameTranslations);
        }

        var cultures = MergeCultures(word1._culturesKey.Cultures, word2._culturesKey.Cultures);

        var translations = new string[cultures.Length];
        for (var i = 0; i < cultures.Length; i++)
        {
            var culture = ToCultureInfo(cultures[i]);
            translations[i] = operation(word1.GetTranslation(culture), word2.GetTranslation(culture), culture);
        }

        return new Word(CulturesKey.GetCulturesKey(cultures), translations);
    }

    private static Word FormatWithoutLocalizedValues(Word word, object?[] values)
    {
        var translations = new string[word._translations.Length];
        var cultures = word._culturesKey.Cultures;
        for (var i = 0; i < translations.Length; i++)
            translations[i] = string.Format(ToCultureInfo(cultures[i]), word._translations[i], values);
        return new Word(word._culturesKey, translations);
    }

    private static string[] MergeCultures(ReadOnlySpan<string> left, ReadOnlySpan<string> right)
    {
        if (left.Length == 0)
            return right.ToArray();
        if (right.Length == 0)
            return left.ToArray();

        var result = new string[left.Length + right.Length];
        var leftIndex = 0;
        var rightIndex = 0;
        var resultIndex = 0;
        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            if (left[leftIndex].CultureEquals(right[rightIndex]))
            {
                result[resultIndex++] = left[leftIndex++];
                rightIndex++;
            }
            else if (CulturesKey.GoesBefore(left[leftIndex], right[rightIndex]))
            {
                result[resultIndex++] = left[leftIndex++];
            }
            else
            {
                result[resultIndex++] = right[rightIndex++];
            }
        }

        while (leftIndex < left.Length)
            result[resultIndex++] = left[leftIndex++];
        while (rightIndex < right.Length)
            result[resultIndex++] = right[rightIndex++];

        return resultIndex == result.Length ? result : result[..resultIndex];
    }

    private static CultureInfo ToCultureInfo(string culture) => culture.Length == 0
        ? CultureInfo.InvariantCulture
        : CultureInfo.GetCultureInfo(culture);

    private readonly record struct Entry(string Culture, string Translation);

    private sealed class EntryComparer : IComparer<Entry>
    {
        public static readonly EntryComparer Instance = new();

        public int Compare(Entry left, Entry right) => CulturesKey.GoesBefore(left.Culture, right.Culture)
            ? -1
            : CulturesKey.GoesBefore(right.Culture, left.Culture) ? 1 : 0;
    }

    public struct Enumerator : IEnumerator<KeyValuePair<CultureInfo, string>>
    {
        private readonly CulturesKey _culturesKey;
        private readonly string[] _translations;
        private int _index;

        internal Enumerator(CulturesKey culturesKey, string[] translations)
        {
            _culturesKey = culturesKey;
            _translations = translations;
            _index = -1;
        }

        public readonly KeyValuePair<CultureInfo, string> Current
        {
            get
            {
                if (_index < 0 || _index >= _translations.Length)
                    throw new InvalidOperationException();

                return new KeyValuePair<CultureInfo, string>(
                    ToCultureInfo(_culturesKey.Cultures[_index]),
                    _translations[_index]);
            }
        }

        readonly object IEnumerator.Current => Current;

        public bool MoveNext() => ++_index < _translations.Length;

        public void Reset() => _index = -1;

        public readonly void Dispose() { }
    }
}
