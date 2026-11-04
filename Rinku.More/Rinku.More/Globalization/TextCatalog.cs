using System.Globalization;

namespace Rinku.More.Globalization;

public interface ILocalizedMessage
{
    Word MessageText { get; }
}

public static class TextCatalog
{
    public static string Translate(this IReadOnlyDictionary<string, Word> catalog, Word value, CultureInfo culture)
    {
        if (value.Key is null || !catalog.TryGetValue(value.Key, out var stored) || stored.Count == 0)
        {
            return value[culture];
        }
        for (var candidate = culture; candidate.Name.Length > 0; candidate = candidate.Parent)
        {
            if (stored.TryGetValue(candidate, out var translated))
            {
                return translated;
            }
            if (value.TryGetValue(candidate, out var fallback))
            {
                return fallback;
            }
        }
        return stored[culture];
    }

    public static Word Resolve(this IReadOnlyDictionary<string, Word> catalog, Word value) => Resolve(catalog, value, null);

    public static Word Resolve(this IReadOnlyDictionary<string, Word> catalog, Word value, string? preferredKey) {
        if (preferredKey is not null && catalog.TryGetValue(preferredKey, out var preferred) && preferred.Count != 0)
            return preferred;
        return value.Key is not null && catalog.TryGetValue(value.Key, out var translated) && translated.Count != 0 ? translated : value;
    }
}
