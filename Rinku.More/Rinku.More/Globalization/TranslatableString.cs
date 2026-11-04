using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Rinku.More.Globalization;

/// <summary>
/// A culture-aware interpolated-string handler suitable for API parameters.
/// Methods can accept this type and callers can pass either a <see cref="Word"/>
/// or an interpolated string containing <see cref="Word"/> values.
/// </summary>
[InterpolatedStringHandler]
public ref struct TranslatableString
{
    private Word? _word;
    private readonly StringBuilder? _format;
    private readonly object?[]? _arguments;
    private int _argumentCount;

    public TranslatableString(int literalLength, int formattedCount)
    {
        _format = new StringBuilder(literalLength + formattedCount * 3);
        _arguments = formattedCount == 0 ? [] : new object?[formattedCount];
        _argumentCount = 0;
    }

    private TranslatableString(Word word)
    {
        _word = word;
        _format = null;
        _arguments = null;
        _argumentCount = 0;
    }

    public Word Word => ToWord();

    public string GetTranslation(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return ToWord().GetTranslation(culture);
    }

    public readonly void AppendLiteral(string value)
        => (_format ?? throw new InvalidOperationException("The Word-backed handler cannot append text.")).Append(value);

    public void AppendFormatted<T>(T value) => AppendFormatted(value, 0, null);

    public void AppendFormatted<T>(T value, string? format) => AppendFormatted(value, 0, format);

    public void AppendFormatted<T>(T value, int alignment) => AppendFormatted(value, alignment, null);

    public void AppendFormatted<T>(T value, int alignment, string? format)
    {
        var argumentIndex = _argumentCount++;
        (_arguments ?? throw new InvalidOperationException("The Word-backed handler cannot append formatted values."))[argumentIndex] = value;
        (_format ?? throw new InvalidOperationException("The Word-backed handler cannot append formatted values.")).Append('{').Append(argumentIndex);
        if (alignment != 0)
            _format.Append(',').Append(alignment);
        if (format is not null)
            _format.Append(':').Append(format);
        _format.Append('}');
    }

    public Word ToWord()
    {
        if (_word is not null)
            return _word;

        _word = Word.Interpolate(FormattableStringFactory.Create(
            (_format ?? throw new InvalidOperationException("The handler has no format.")).ToString(),
            _arguments ?? throw new InvalidOperationException("The handler has no arguments.")));
        return _word;
    }

    public override string ToString() => ToWord().ToString();

    public static implicit operator TranslatableString(Word word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return new TranslatableString(word);
    }

    public static implicit operator Word(TranslatableString text) => text.ToWord();
}
