using System.Globalization;

namespace Rinku.More.Globalization;

public interface IWordEditor
{
    IEnumerable<CultureInfo> Cultures
    {
        get; set;
    }
    bool AllowAddCultures
    {
        get; set;
    }
    int MinLines
    {
        get; set;
    }
    int MaxLines
    {
        get; set;
    }
}

public static class WordEditorDefaults
{
    public const int MinLines = 1;
    public const int MaxLines = 3;
}

public sealed class WordEditValue
{
    private readonly Word? _original;
    public WordEditValue(Word? value, IEnumerable<CultureInfo>? cultures = null)
    {
        _original = value;
        if (value is not null)
        {
            foreach (var translation in value)
            {
                Translations.Add(new(translation.Key, translation.Value));
            }
        }
        IncludeCultures(cultures ?? []);
    }
    public List<WordTranslation> Translations { get; } = [];
    public void IncludeCultures(IEnumerable<CultureInfo> cultures)
    {
        foreach (var culture in cultures)
        {
            IncludeCulture(culture);
        }
    }
    public void IncludeCulture(CultureInfo culture)
    {
        if (!Translations.Any(row => row.Culture.Equals(culture)))
        {
            Translations.Add(new(culture));
        }
    }
    public Word? Update(WordTranslation row, string value)
    {
        row.Value = value;
        return BuildValue();
    }
    public Word? BuildValue()
    {
        var result = _original?.Copy() ?? new Word();
        foreach (var row in Translations)
        {
            if (row.Value is not null)
            {
                result.Add(row.Culture, row.Value);
            }
        }
        if (_original is null && result.Count == 0)
        {
            return null;
        }
        if (_original?.Equals(result) == true)
        {
            return _original;
        }
        return result;
    }
}

public sealed class WordTranslation(CultureInfo culture, string? value = null)
{
    public CultureInfo Culture { get; } = culture;
    public string? Value { get; set; } = value;
}

public static class WordEditText
{
    private static readonly WordDict Texts = WordDict.FromLocalizationFile(Path.Combine(AppContext.BaseDirectory, "Localization", "WordEdit.resx"));
    public static Word EditTranslations => Texts[nameof(EditTranslations)];
    public static Word AddLanguage => Texts[nameof(AddLanguage)];
    public static Word Language => Texts[nameof(Language)];
    public static Word Invariant => Texts[nameof(Invariant)];
}
