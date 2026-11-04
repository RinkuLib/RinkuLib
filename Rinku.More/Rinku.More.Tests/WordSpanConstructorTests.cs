using System.Globalization;
using Rinku.More.Globalization;

namespace Rinku.More.Tests;

/// <summary>Tests construction of words from culture-text spans.</summary>
public sealed class WordSpanConstructorTests
{
    /// <summary>Verifies the span constructor stores and orders translations.</summary>
    [Fact]
    public void Params_span_constructor_stores_and_orders_translations()
    {
        var word = new Word(new(CultureInfo.GetCultureInfo("fr"), "Bonjour"), new(CultureInfo.GetCultureInfo("en"), "Hello"));

        Assert.Equal(2, word.Count);
        Assert.Equal("Hello", word.GetTranslation(CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("Bonjour", word.GetTranslation(CultureInfo.GetCultureInfo("fr-CA")));
    }

    /// <summary>Verifies duplicate cultures are rejected by the span constructor.</summary>
    [Fact]
    public void Params_span_constructor_rejects_duplicate_cultures()
    {
        Assert.Throws<ArgumentException>(() => new Word(new(CultureInfo.GetCultureInfo("en"), "Hello"), new(CultureInfo.GetCultureInfo("en"), "Hi")));
    }
}
