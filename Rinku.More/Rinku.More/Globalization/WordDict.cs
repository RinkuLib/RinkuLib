using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Rinku.More.Globalization;

/// <summary>
/// A key-to-word localization catalog, including CSV and ResX loading.
/// The file format is a header of <c>key,culture,...</c> followed by translation rows.
/// </summary>
public sealed class WordDict : Dictionary<string, Word>
{
    /// <summary>
    /// Builds a catalog from a neutral ResX file and every culture-suffixed
    /// sibling, such as <c>Strings.resx</c>, <c>Strings.fr.resx</c>, and
    /// <c>Strings.fr-CA.resx</c>. Add an overlay file to the directory and it
    /// is included automatically.
    /// </summary>
    public static WordDict FromLocalizationFile(string neutralFilePath)
    {
        var catalog = new WordDict();
        catalog.LoadLocalizationSet(neutralFilePath);
        return catalog;
    }

    /// <summary>
    /// Builds a catalog from an explicit set of standard ResX localization files.
    /// Prefer <see cref="FromLocalizationFile(string)"/> for normal use.
    /// </summary>
    public static WordDict FromLocalizationFiles(params string[] filePaths) =>
        FromLocalizationFiles((IEnumerable<string>)filePaths);

    public static WordDict FromLocalizationFiles(IEnumerable<string> filePaths)
    {
        var catalog = new WordDict();
        catalog.LoadLocalizationFiles(filePaths);
        return catalog;
    }

    /// <summary>
    /// Loads a single localization file into this catalog. The file type is
    /// selected from its extension: <c>.resx</c> adds one culture overlay and
    /// <c>.csv</c> adds the cultures represented by its columns.
    /// </summary>
    public void LoadFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var extension = Path.GetExtension(filePath);
        if (string.Equals(extension, ".resx", StringComparison.OrdinalIgnoreCase))
        {
            LoadLocalizationFile(filePath);
            return;
        }

        if (string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
        {
            Merge(FromCsvFile(filePath));
            return;
        }

        throw new NotSupportedException($"Localization files with the '{extension}' extension are not supported.");
    }

    /// <summary>
    /// Loads one ResX file. It may be either a neutral resource or one culture
    /// overlay, allowing translations to be added as files become available.
    /// </summary>
    public void LoadLocalizationFile(string filePath)
    {
        var localizationFile = ReadLocalizationFile(filePath);
        Merge(localizationFile);
    }

    /// <summary>
    /// Loads an explicit sequence of ResX files. Files are parsed before the
    /// catalog is changed, so an invalid file cannot leave a partial load.
    /// Later files intentionally replace values for the same key and culture.
    /// </summary>
    public void LoadLocalizationFiles(IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var files = new List<LocalizationFileData>();
        foreach (var filePath in filePaths)
            files.Add(ReadLocalizationFile(filePath));

        foreach (var file in files)
            Merge(file);
    }

    /// <summary>
    /// Loads a neutral ResX file and every valid culture-suffixed sibling in
    /// its directory, such as <c>Strings.fr.resx</c>.
    /// </summary>
    public void LoadLocalizationSet(string neutralFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(neutralFilePath);

        var fullPath = Path.GetFullPath(neutralFilePath);
        if (!string.Equals(Path.GetExtension(fullPath), ".resx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"'{neutralFilePath}' is not a .resx localization file.");
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The neutral ResX localization file was not found.", fullPath);

        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"The resource path has no directory: {fullPath}");
        var baseName = Path.GetFileNameWithoutExtension(fullPath);
        if (TryGetCultureFromResourceName(baseName, out _))
            throw new InvalidDataException($"'{neutralFilePath}' is a culture overlay. Pass its neutral .resx file instead.");

        var prefix = baseName + ".";
        var localizedFiles = Directory.EnumerateFiles(directory, "*.resx")
            .Where(path => IsLocalizedVariant(path, prefix))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Prepend(fullPath);
        LoadLocalizationFiles(localizedFiles);
    }

    public static WordDict FromCsvFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var reader = new StreamReader(filePath);
        return FromCsv(reader);
    }

    public static WordDict FromCsv(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream, leaveOpen: true);
        return FromCsv(reader);
    }

    public static WordDict FromCsv(StreamReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var csv = new CsvReader(reader);
        if (!csv.ReadLine(out var header))
            return [];
        if (!header.ReadValue(out var keyHeader) || !keyHeader.SequenceEqual("key"))
            throw new InvalidDataException("The first localization column must be named 'key'.");

        var columns = ReadColumns(ref header);
        if (columns.Length == 0)
            throw new InvalidDataException("The localization file must contain at least one culture column.");
        var sourceToSorted = BuildSourceToSortedMap(columns);
        var cultures = new string[columns.Length];
        for (var i = 0; i < columns.Length; i++)
            cultures[i] = columns[i].Culture;
        var catalog = new WordDict();
        var buffer = new string?[columns.Length];
        Dictionary<ulong, CulturesKey>? savedKeys = columns.Length <= 64 ? [] : null;

        while (csv.ReadLine(out var line))
        {
            if (!line.ReadValue(out var keyValue))
                continue;
            if (keyValue.IsEmpty)
                continue;

            var key = keyValue.ToString();
            if (catalog.ContainsKey(key))
                throw new InvalidDataException($"The localization key '{key}' appears more than once.");

            Array.Clear(buffer, 0, buffer.Length);
            var sourceIndex = 0;
            while (line.ReadValue(out var value))
            {
                if (sourceIndex >= sourceToSorted.Length)
                    throw new InvalidDataException("A localization row has more columns than the header.");

                if (!value.IsEmpty)
                    buffer[sourceToSorted[sourceIndex]] = value.ToString();
                sourceIndex++;
            }

            var translationCount = 0;
            var cultureMask = 0UL;
            for (var i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] is not null)
                {
                    translationCount++;
                    cultureMask |= 1UL << i;
                }
            }

            var translations = new string[translationCount];
            var translationIndex = 0;
            string[]? rowCultures = savedKeys is null ? new string[translationCount] : null;
            for (var i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] is not { } translation)
                    continue;

                rowCultures?[translationIndex] = cultures[i];
                translations[translationIndex] = translation;
                translationIndex++;
            }

            if (savedKeys is null)
            {
                catalog.Add(key, Word.FromOrderedTranslations(rowCultures ?? throw new InvalidDataException("A new localization row has no culture values."), translations));
                continue;
            }

            if (!savedKeys.TryGetValue(cultureMask, out var culturesKey))
            {
                var keyCultures = new string[translationCount];
                var keyIndex = 0;
                for (var i = 0; i < buffer.Length; i++)
                {
                    if (buffer[i] is not null)
                        keyCultures[keyIndex++] = cultures[i];
                }

                culturesKey = CulturesKey.GetCulturesKey(keyCultures);
                savedKeys.Add(cultureMask, culturesKey);
            }

            catalog.Add(key, Word.FromCulturesKey(culturesKey, translations));
        }

        return catalog;
    }

    public string Get(string key) => Get(key, CultureInfo.CurrentUICulture);

    public string Get(string key, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(culture);

        return TryGetValue(key, out var word) ? word.GetTranslation(culture) : key;
    }

    public string[] GetCulturesHeader()
    {
        var frequencies = new Dictionary<string, int>();
        foreach (var word in Values)
        {
            foreach (var culture in word.CulturesKey.Cultures)
            {
                frequencies[culture] = frequencies.TryGetValue(culture, out var frequency)
                    ? frequency + 1
                    : 1;
            }
        }

        var header = frequencies.ToArray();
        Array.Sort(header, static (left, right) =>
        {
            var frequency = right.Value.CompareTo(left.Value);
            if (frequency != 0)
                return frequency;
            return CulturesKey.GoesBefore(left.Key, right.Key)
                ? -1
                : CulturesKey.GoesBefore(right.Key, left.Key) ? 1 : 0;
        });

        var cultures = new string[header.Length];
        for (var i = 0; i < header.Length; i++)
            cultures[i] = header[i].Key;
        return cultures;
    }

    private static Column[] ReadColumns(ref CsvReader.LineReader header)
    {
        var columns = new List<Column>();
        var sourceIndex = 0;
        while (header.ReadValue(out var cultureValue))
        {
            var culture = cultureValue.IsEmpty
                ? CultureInfo.InvariantCulture.Name
                : CultureInfo.GetCultureInfo(cultureValue.ToString()).Name;
            columns.Add(new Column(culture, sourceIndex++));
        }

        var result = columns.ToArray();
        Array.Sort(result, ColumnComparer.Instance);
        for (var i = 1; i < result.Length; i++)
        {
            if (result[i - 1].Culture.CultureEquals(result[i].Culture))
                throw new InvalidDataException($"The culture '{result[i].Culture}' appears more than once.");
        }

        return result;
    }

    private static LocalizationFileData ReadLocalizationFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var culture = GetCultureFromLocalizationFileName(filePath);
        using var stream = File.OpenRead(filePath);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        var document = XDocument.Load(reader, LoadOptions.None);
        var entries = new List<LocalizedEntry>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var data in document.Descendants().Where(static element => element.Name.LocalName == "data"))
        {
            var key = data.Attributes().FirstOrDefault(static attribute => attribute.Name.LocalName == "name")?.Value;
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidDataException($"A data entry in '{filePath}' has no name.");
            if (!seenKeys.Add(key))
                throw new InvalidDataException($"The resource '{key}' appears more than once in '{filePath}'.");

            var type = data.Attributes().FirstOrDefault(static attribute => attribute.Name.LocalName == "type")?.Value;
            var mimeType = data.Attributes().FirstOrDefault(static attribute => attribute.Name.LocalName == "mimetype")?.Value;
            if (type?.StartsWith("System.Resources.ResXNullRef", StringComparison.Ordinal) == true)
            {
                entries.Add(new LocalizedEntry(key, null));
                continue;
            }
            if (mimeType is not null || (type is not null && !type.StartsWith("System.String", StringComparison.Ordinal)))
                throw new InvalidDataException($"The resource '{key}' in '{filePath}' is not a string value.");

            var translation = data.Elements().FirstOrDefault(static element => element.Name.LocalName == "value")?.Value ?? string.Empty;
            entries.Add(new LocalizedEntry(key, translation));
        }

        return new LocalizationFileData(culture, entries);
    }

    private void Merge(LocalizationFileData file)
    {
        var culture = file.Culture.Length == 0
            ? CultureInfo.InvariantCulture
            : CultureInfo.GetCultureInfo(file.Culture);

        foreach (var entry in file.Entries)
        {
            if (entry.Translation is null)
            {
                if (TryGetValue(entry.Key, out var existing) && existing.Remove(culture) && existing.Count == 0)
                    Remove(entry.Key);
                continue;
            }

            if (!TryGetValue(entry.Key, out var word))
            {
                word = [];
                Add(entry.Key, word);
            }

            word.Add(culture, entry.Translation);
        }
    }

    private void Merge(WordDict source)
    {
        foreach (var (key, sourceWord) in source)
        {
            foreach (var translation in sourceWord)
            {
                if (!TryGetValue(key, out var targetWord))
                {
                    targetWord = [];
                    Add(key, targetWord);
                }

                targetWord.Add(translation.Key, translation.Value);
            }
        }
    }

    private static string GetCultureFromLocalizationFileName(string filePath)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".resx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"'{filePath}' is not a .resx localization file.");

        var name = Path.GetFileNameWithoutExtension(filePath);
        if (!TryGetCultureFromResourceName(name, out var culture))
            return CultureInfo.InvariantCulture.Name;

        return culture;
    }

    private static bool IsLocalizedVariant(string filePath, string prefix)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        return name.StartsWith(prefix, StringComparison.Ordinal) &&
            TryGetCultureName(name[prefix.Length..], out _);
    }

    private static bool TryGetCultureFromResourceName(string resourceName, out string culture)
    {
        var separator = resourceName.LastIndexOf('.');
        if (separator < 0)
        {
            culture = string.Empty;
            return false;
        }

        var cultureName = resourceName[(separator + 1)..];
        return TryGetCultureName(cultureName, out culture);
    }

    private static bool TryGetCultureName(string cultureName, out string culture)
    {
        try
        {
            culture = CultureInfo.GetCultureInfo(cultureName).Name;
            return true;
        }
        catch (CultureNotFoundException)
        {
            culture = string.Empty;
            return false;
        }
    }

    private static int[] BuildSourceToSortedMap(Column[] columns)
    {
        var map = new int[columns.Length];
        for (var sortedIndex = 0; sortedIndex < columns.Length; sortedIndex++)
            map[columns[sortedIndex].SourceIndex] = sortedIndex;
        return map;
    }

    private readonly record struct Column(string Culture, int SourceIndex);
    private readonly record struct LocalizationFileData(string Culture, List<LocalizedEntry> Entries);
    private readonly record struct LocalizedEntry(string Key, string? Translation);

    private sealed class ColumnComparer : IComparer<Column>
    {
        public static readonly ColumnComparer Instance = new();

        public int Compare(Column left, Column right) => CulturesKey.GoesBefore(left.Culture, right.Culture)
            ? -1
            : CulturesKey.GoesBefore(right.Culture, left.Culture) ? 1 : 0;
    }
}
