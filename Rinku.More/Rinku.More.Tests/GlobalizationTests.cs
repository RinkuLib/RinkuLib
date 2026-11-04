using System.Globalization;
using System.Data;
using System.Text;
using Rinku.Mapping;
using Rinku.More.Globalization;

namespace Rinku.More.Tests;

/// <summary>Tests globalization, localization, and culture-aware word behavior.</summary>
public class GlobalizationTests
{
    /// <summary>Verifies neutral and culture-suffixed columns map to one word.</summary>
    [Fact]
    public void Rinku_maps_neutral_and_culture_suffixed_columns_to_one_word()
    {
        var table = new DataTable();
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("NameFr", typeof(string));
        table.Columns.Add("NameEn", typeof(string));
        table.Rows.Add("Default", "Bonjour", "Hello");

        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        using var parser = TypeParser.GetTypeParser<DatabaseRow>([
            new ColumnInfo("Name", typeof(string), false),
            new ColumnInfo("NameFr", typeof(string), false),
            new ColumnInfo("NameEn", typeof(string), false)
        ]);

        var row = parser.Parse(reader).Result;

        Assert.Equal("Default", row.Name.GetTranslation(C.DE));
        Assert.Equal("Bonjour", row.Name.GetTranslation(C.FR_CA));
        Assert.Equal("Hello", row.Name.GetTranslation(C.EN_US));
    }

    /// <summary>Verifies missing and null translations are allowed.</summary>
    [Fact]
    public void Rinku_word_mapping_allows_missing_and_null_translations()
    {
        var table = new DataTable();
        table.Columns.Add("NameFr", typeof(string));
        table.Columns.Add("NameEn", typeof(string));
        table.Rows.Add("Bonjour", DBNull.Value);

        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        using var parser = TypeParser.GetTypeParser<DatabaseRow>([
            new ColumnInfo("NameFr", typeof(string), true),
            new ColumnInfo("NameEn", typeof(string), true)
        ]);

        var row = parser.Parse(reader).Result;

        Assert.Equal("Bonjour", row.Name.GetTranslation(C.FR));
        Assert.False(row.Name.ContainsKey(C.EN));
    }

    /// <summary>Verifies alternative member names are honored by word mapping.</summary>
    [Fact]
    public void Rinku_word_mapping_honors_alternative_member_names()
    {
        var table = new DataTable();
        table.Columns.Add("Title", typeof(string));
        table.Columns.Add("TitleFr", typeof(string));
        table.Rows.Add("Default title", "Titre");

        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        using var parser = TypeParser.GetTypeParser<AliasedDatabaseRow>([
            new ColumnInfo("Title", typeof(string), false),
            new ColumnInfo("TitleFr", typeof(string), false)
        ]);

        var row = parser.Parse(reader).Result;

        Assert.Equal("Default title", row.Description.GetTranslation(C.DE));
        Assert.Equal("Titre", row.Description.GetTranslation(C.FR));
    }

    /// <summary>Verifies common cultures are available.</summary>
    [Fact]
    public void Common_cultures_are_available()
    {
        Assert.Equal("en-US", C.EN_US.Name);
        Assert.Equal(CultureInfo.InvariantCulture, C.INV);
        Assert.Same(C.FR, CultureInfo.GetCultureInfo("fr"));
    }

    /// <summary>Verifies translation lookup uses exact, parent, then default cultures.</summary>
    [Fact]
    public void Word_uses_the_exact_then_parent_then_default_translation()
    {
        var word = new Word
        {
            [C.INV] = "Hello",
            [C.FR] = "Bonjour"
        };

        Assert.Equal("Bonjour", word.GetTranslation(C.FR_CA));
        Assert.Equal("Hello", word.GetTranslation(C.DE_DE));
    }

    /// <summary>Verifies translation lookup walks multiple culture parent levels.</summary>
    [Fact]
    public void Word_walks_multiple_culture_parent_levels()
    {
        var word = new Word
        {
            [C.ZH] = "你好"
        };

        Assert.Equal("你好", word.GetTranslation(new CultureInfo("zh-Hant-TW")));
    }

    /// <summary>Verifies a word does not require an invariant translation.</summary>
    [Fact]
    public void Word_does_not_require_an_invariant_translation()
    {
        var word = new Word(new Dictionary<CultureInfo, string>
        {
            [C.FR] = "Bonjour"
        });

        Assert.Equal("Bonjour", word.GetTranslation(C.DE));
    }

    /// <summary>Verifies an empty word uses configurable default text.</summary>
    [Fact]
    public void Empty_word_uses_the_configurable_default_text()
    {
        var original = Word.NotSupported;
        try
        {
            Word.NotSupported = "[missing]";

            Assert.Equal("[missing]", new Word().GetTranslation(C.EN_US));
        }
        finally
        {
            Word.NotSupported = original;
        }
    }

    /// <summary>Verifies the default culture uses its parent chain.</summary>
    [Fact]
    public void Default_culture_uses_its_parent_chain()
    {
        var original = Word.DefaultCulture;
        try
        {
            Word.DefaultCulture = C.FR_CA;
            var word = new Word
            {
                [C.FR] = "Bonjour"
            };

            Assert.Equal("Bonjour", word.GetTranslation(C.DE_DE));
        }
        finally
        {
            Word.DefaultCulture = original;
        }
    }

    /// <summary>Verifies formatting uses each translation culture.</summary>
    [Fact]
    public void Formatting_uses_each_translation_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var word = new Word
            {
                [C.FR_FR] = "{0:N2}"
            };

            var formatted = Word.Format(word, 1234.5);

            Assert.Equal(string.Format(C.FR_FR, "{0:N2}", 1234.5), formatted[C.FR_FR]);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>Verifies format extensions count and validate parameters.</summary>
    [Fact]
    public void Format_extensions_count_and_validate_parameters()
    {
        Assert.Equal(2, "Value: {0}, {1}".GetNbFormatParams());
        Assert.True("Value: {0}, {1}".CheckValidFormat(2, perfectMatch: true));
        Assert.True("Value: {0,10} {1:yyyy-MM-dd}".CheckValidFormat(2, perfectMatch: true));
        Assert.False("Value: {0}".CheckValidFormat(2, perfectMatch: true));
        Assert.False("Value: {x}".CheckValidFormat());
        Assert.False("Value: {0:unterminated".CheckValidFormat());
    }

    /// <summary>Verifies equivalent cultures match without using culture names as keys.</summary>
    [Fact]
    public void Word_matches_equivalent_cultures_without_using_culture_names_as_keys()
    {
        var word = new Word
        {
            [C.FR] = "Bonjour"
        };

        Assert.True(word.TryGetValue(new CultureInfo("fr"), out var translation));
        Assert.Equal("Bonjour", translation);
    }

    /// <summary>Verifies extra translations are preserved and removable.</summary>
    [Fact]
    public void Word_keeps_extra_translations_and_supports_removal()
    {
        var word = new Word
        {
            [C.EN] = "Hello",
            [C.FR] = "Bonjour",
            [C.DE] = "Hallo"
        };

        Assert.Equal(3, word.Count);
        Assert.True(word.Remove(new CultureInfo("fr")));
        Assert.Equal(2, word.Count);
        Assert.False(word.TryGetValue(C.FR, out _));
        Assert.Equal("Hello", word.GetTranslation(C.EN));
        Assert.Equal("Hallo", word.GetTranslation(C.DE));
    }

    /// <summary>Verifies adding a translation replaces an existing culture translation.</summary>
    [Fact]
    public void Add_replaces_an_existing_culture_translation()
    {
        var word = new Word
        {
            { C.FR, "Bonjour" },
            { C.FR, "Salut" }
        };

        Assert.Equal(1, word.Count);
        Assert.Equal("Salut", word.GetTranslation(C.FR));
    }

    /// <summary>Verifies words with the same culture layout share one cultures key.</summary>
    [Fact]
    public void Words_with_the_same_culture_layout_share_one_cultures_key()
    {
        var first = new Word
        {
            [C.EN] = "Hello",
            [C.FR] = "Bonjour"
        };
        var second = new Word
        {
            [C.FR] = "Au revoir",
            [C.EN] = "Goodbye"
        };

        Assert.Same(first.CulturesKey, second.CulturesKey);
        Assert.Equal("Hello", first.GetTranslation(C.EN));
        Assert.Equal("Au revoir", second.GetTranslation(C.FR));
    }

    /// <summary>Verifies common two- and five-character cultures use exact translations.</summary>
    [Fact]
    public void Common_two_and_five_character_cultures_use_exact_translations()
    {
        var word = new Word
        {
            [C.EN] = "English",
            [C.EN_US] = "American English",
            [C.FR_CA] = "French Canadian"
        };

        Assert.Equal("English", word.GetTranslation(C.EN));
        Assert.Equal("American English", word.GetTranslation(C.EN_US));
        Assert.Equal("French Canadian", word.GetTranslation(C.FR_CA));
    }

    /// <summary>Verifies a localization catalog builds words from a CSV file.</summary>
    [Fact]
    public void Localization_catalog_builds_words_from_a_csv_file()
    {
        const string csv = """
            key,,fr-CA,en,fr
            greeting,Default,"Bonjour, monde",Hello,Bonjour
            farewell,Default,Au revoir,Goodbye,Au revoir
            """;
        var filePath = Path.Combine(Path.GetTempPath(), $"rinku-more-{Guid.NewGuid():N}.csv");

        try
        {
            File.WriteAllText(filePath, csv, Encoding.UTF8);
            var catalog = WordDict.FromCsvFile(filePath);

            Assert.Equal("Bonjour, monde", catalog.Get("greeting", C.FR_CA));
            Assert.Equal("Bonjour", catalog.Get("greeting", C.FR_FR));
            Assert.Equal("Hello", catalog.Get("greeting", C.EN_US));
            Assert.Equal("Default", catalog.Get("greeting", C.DE_DE));
            Assert.Equal("Goodbye", catalog.Get("farewell", C.EN_GB));
            Assert.Equal("unknown.key", catalog.Get("unknown.key", C.EN));
            Assert.Same(catalog["greeting"].CulturesKey, catalog["farewell"].CulturesKey);
            Assert.Equal(["en", "fr", "fr-CA", ""], catalog.GetCulturesHeader());
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    /// <summary>Verifies a localization catalog rejects unterminated quoted values.</summary>
    [Fact]
    public void Localization_catalog_rejects_an_unterminated_quoted_value()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("key,en\nbroken,\"missing end"));

        Assert.Throws<InvalidDataException>(() => WordDict.FromCsv(stream));
    }

    /// <summary>Verifies a localization catalog rejects duplicate keys and malformed quotes.</summary>
    [Fact]
    public void Localization_catalog_rejects_duplicate_keys_and_malformed_quotes()
    {
        using var duplicateKeys = new MemoryStream(Encoding.UTF8.GetBytes("key,en\ngreeting,Hello\ngreeting,Hi"));
        using var malformedQuotes = new MemoryStream(Encoding.UTF8.GetBytes("key,en\ngreeting,\"Hello\" there"));

        Assert.Throws<InvalidDataException>(() => WordDict.FromCsv(duplicateKeys));
        Assert.Throws<InvalidDataException>(() => WordDict.FromCsv(malformedQuotes));
    }

    /// <summary>Verifies word transformations preserve cultures and translated formatting.</summary>
    [Fact]
    public void Word_transformations_preserve_cultures_and_format_with_translated_values()
    {
        var greeting = new Word
        {
            [C.EN] = "Hello, {0}!",
            [C.FR] = "Bonjour, {0}!"
        };
        var name = new Word
        {
            [C.EN] = "world",
            [C.FR] = "monde"
        };

        var formatted = Word.Format(greeting, name);
        var suffixed = name + "!";

        Assert.True(greeting.CheckValidFormat(1, perfectMatch: true));
        Assert.Equal("Hello, world!", formatted.GetTranslation(C.EN));
        Assert.Equal("Bonjour, monde!", formatted.GetTranslation(C.FR));
        Assert.Equal("world!", suffixed.GetTranslation(C.EN));
        Assert.Equal("monde!", suffixed.GetTranslation(C.FR));
        Assert.Same(greeting.CulturesKey, formatted.CulturesKey);
    }

    /// <summary>Verifies word composition and interpolation retain all cultures.</summary>
    [Fact]
    public void Word_composition_and_interpolation_keep_all_available_cultures()
    {
        Word template = "Welcome, {0}!";
        var name = new Word
        {
            [C.EN] = "world",
            [C.FR] = "monde"
        };

        var formatted = Word.Format(template, name);
        var interpolated = Word.Interpolate($"Welcome, {name}!");
        var parameter = ReadTranslatable($"Welcome, {name}!", C.FR_CA);
        var wordParameter = ReadTranslatable(name, C.FR_CA);
        var combined = (Word)"Hello, " + name;

        Assert.Equal("Welcome, world!", formatted.GetTranslation(C.EN_US));
        Assert.Equal("Welcome, monde!", formatted.GetTranslation(C.FR_CA));
        Assert.Equal("Welcome, monde!", interpolated.GetTranslation(C.FR_CA));
        Assert.Equal("Welcome, monde!", parameter);
        Assert.Equal("monde", wordParameter);
        Assert.Equal("Hello, monde", combined.GetTranslation(C.FR_CA));
        Assert.True(formatted.ContainsKey(C.EN));
        Assert.True(formatted.ContainsKey(C.FR));
    }

    /// <summary>Verifies invariant translations can be formatted and enumerated.</summary>
    [Fact]
    public void Invariant_translation_can_be_formatted_and_enumerated()
    {
        Word template = "Value: {0:N2}";

        var formatted = Word.Format(template, 1234.5);
        var translation = Assert.Single(formatted);

        Assert.Equal(CultureInfo.InvariantCulture, translation.Key);
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, "Value: {0:N2}", 1234.5), translation.Value);
    }

    /// <summary>Verifies a localization catalog merges standard culture-suffixed RESX files.</summary>
    [Fact]
    public void Localization_catalog_merges_standard_culture_suffixed_resx_files()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rinku-more-{Guid.NewGuid():N}");
        var neutralFile = Path.Combine(directory, "Strings.resx");
        var englishFile = Path.Combine(directory, "Strings.en.resx");
        var frenchFile = Path.Combine(directory, "Strings.fr.resx");
        var frenchCanadianFile = Path.Combine(directory, "Strings.fr-CA.resx");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(neutralFile, Resx(("greeting", "Default"), ("farewell", "Default farewell")), Encoding.UTF8);
            File.WriteAllText(englishFile, Resx(("greeting", "Hello"), ("farewell", "Goodbye")), Encoding.UTF8);
            File.WriteAllText(frenchFile, Resx(("greeting", "Bonjour"), ("farewell", "Au revoir"), ("thanks", "Merci")), Encoding.UTF8);
            File.WriteAllText(frenchCanadianFile, ResxWithNull("thanks", ("greeting", "Allo")), Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "Strings.backup.resx"), Resx(("greeting", "Ignored")), Encoding.UTF8);

            var catalog = WordDict.FromLocalizationFile(neutralFile);

            Assert.Equal("Hello", catalog.Get("greeting", C.EN_US));
            Assert.Equal("Allo", catalog.Get("greeting", C.FR_CA));
            Assert.Equal("Bonjour", catalog.Get("greeting", CultureInfo.GetCultureInfo("fr-BE")));
            Assert.Equal("Au revoir", catalog.Get("farewell", C.FR_FR));
            Assert.Equal("Default farewell", catalog.Get("farewell", C.DE_DE));
            Assert.Equal("Merci", catalog.Get("thanks", C.FR_CA));
            Assert.Equal(4, catalog["greeting"].Count);
            Assert.Equal(3, catalog["farewell"].Count);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies localization files can be loaded incrementally by extension.</summary>
    [Fact]
    public void Localization_files_can_be_loaded_incrementally_by_extension()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rinku-more-{Guid.NewGuid():N}");
        var neutralFile = Path.Combine(directory, "Messages.resx");
        var frenchFile = Path.Combine(directory, "Messages.fr.resx");
        var spanishFile = Path.Combine(directory, "Messages.es.resx");
        var csvFile = Path.Combine(directory, "extra.csv");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(neutralFile, Resx(("greeting", "Hello"), ("neutralOnly", "Neutral")), Encoding.UTF8);
            File.WriteAllText(frenchFile, Resx(("greeting", "Bonjour"), ("frenchOnly", "Français")), Encoding.UTF8);
            File.WriteAllText(spanishFile, Resx(("spanishOnly", "Español")), Encoding.UTF8);
            File.WriteAllText(csvFile, "key,en\nfromCsv,Loaded from CSV\n", Encoding.UTF8);

            var catalog = new WordDict();
            catalog.LoadFile(neutralFile);
            Assert.Equal("Hello", catalog.Get("greeting", C.FR_CA));

            catalog.LoadFile(frenchFile);
            catalog.LoadFile(spanishFile);
            catalog.LoadFile(csvFile);

            Assert.Equal("Bonjour", catalog.Get("greeting", C.FR_CA));
            Assert.Equal("Neutral", catalog.Get("neutralOnly", C.DE_DE));
            Assert.Equal("Français", catalog.Get("frenchOnly", C.FR_FR));
            Assert.Equal("Español", catalog.Get("spanishOnly", C.ES_MX));
            Assert.Equal("Loaded from CSV", catalog.Get("fromCsv", C.EN_US));

            File.WriteAllText(frenchFile, Resx(("greeting", "Salut")), Encoding.UTF8);
            catalog.LoadLocalizationFile(frenchFile);

            Assert.Equal("Salut", catalog.Get("greeting", C.FR_CA));
            Assert.Equal("Français", catalog.Get("frenchOnly", C.FR_FR));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static string Resx(params (string Key, string Value)[] entries)
    {
        var values = string.Join(Environment.NewLine, entries.Select(static entry =>
            $"  <data name=\"{entry.Key}\" xml:space=\"preserve\"><value>{entry.Value}</value></data>"));
        return $"<?xml version=\"1.0\" encoding=\"utf-8\"?>{Environment.NewLine}<root>{Environment.NewLine}{values}{Environment.NewLine}</root>";
    }

    private static string ResxWithNull(string key, params (string Key, string Value)[] entries) =>
        Resx(entries).Replace(
            "</root>",
            $"  <data name=\"{key}\" type=\"System.Resources.ResXNullRef, System.Windows.Forms\"><value /></data>{Environment.NewLine}</root>",
            StringComparison.Ordinal);

    private static string ReadTranslatable(TranslatableString value, CultureInfo culture) => value.GetTranslation(culture);

    private sealed record DatabaseRow(Word Name);
    private sealed record AliasedDatabaseRow([param: Alt("Title")] Word Description);
}
