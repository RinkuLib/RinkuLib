namespace RinkuPowerTools.Compiler; 
public static class MergeXAMLEngine {
    public const string DataTemplate = "<DataTemplate";
    public const string DataTemplateEnd = "</DataTemplate>";

    public const string Token = "{{CONTENT}}";
    public static int MergeFiles(string inputPath, string outputPath, string pattern, string shell, string[] files) {
        var count = 0;
        var projectRoot = GetCommonRoot(inputPath, outputPath);
        if (!shell.StartsWith(DataTemplate))
            throw new XamlCompilerException("RXC001", $"Shell must start with {DataTemplate}");
        var indexOfToken = shell.IndexOf(Token);
        if (indexOfToken == -1)
            throw new XamlCompilerException("RXC002", $"Shell missing {Token} token");
        var closingTagIndex = shell.LastIndexOf(DataTemplateEnd);
        if (closingTagIndex == -1)
            throw new XamlCompilerException("RXC003", $"Shell file missing {DataTemplateEnd} tag");
        var (shellTagEnd, shellAttrs) = ParseAttributes(shell);
        var shellBeforeContent = shell.AsSpan(shellTagEnd, indexOfToken - shellTagEnd);
        var shellAfterContent = shell.AsSpan(indexOfToken + Token.Length);

        foreach (var file in files) {
            var feature = File.ReadAllText(file);
            if (!feature.StartsWith(DataTemplate))
                throw new XamlCompilerException("RXC001", $"The xml must start with {DataTemplate}", Path.GetFileName(file));
            var (featureTagEnd, featureAttrs) = ParseAttributes(feature);

            var fullOutput = Path.Combine(outputPath, GetLogicalName(projectRoot, file, pattern));
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);

            using (var writer = new StreamWriter(fullOutput)) {
                writer.Write(DataTemplate);
                foreach (var kv in shellAttrs)
                    writer.WriteAttribute(kv.Key, kv.Value);
                foreach (var kv in featureAttrs) {
                    if (!shellAttrs.TryGetValue(kv.Key, out var shellValue))
                        writer.WriteAttribute(kv.Key, kv.Value);
                    else if (kv.Value != shellValue)
                        throw new XamlCompilerException("RXC004", $"Attribute conflict: {kv.Key}", Path.GetFileName(file));
                }

                writer.Write('>');
                writer.Write(shellBeforeContent);
                closingTagIndex = feature.LastIndexOf(DataTemplateEnd);
                if (closingTagIndex == -1)
                    throw new XamlCompilerException("RXC003", $"Shell file missing {DataTemplateEnd} tag", Path.GetFileName(file));
                writer.Write(feature.AsSpan(featureTagEnd, closingTagIndex - featureTagEnd));
                writer.Write(shellAfterContent);
            }

            Console.WriteLine($"RinkuXamlCompiler: generated '{fullOutput}'");
            count++;
        }
        return count;
    }
    public static string GetLogicalName(string projectRoot, string file, string templateSuffix) {
        var relative = Path.GetRelativePath(projectRoot, file);
        int suffixIndex = relative.LastIndexOf(templateSuffix, StringComparison.Ordinal);
        ReadOnlySpan<char> span = suffixIndex >= 0
            ? relative.AsSpan(0, suffixIndex)
            : relative.AsSpan();
        int finalLength = span.Length + ".xaml".Length;
        return string.Create(finalLength, span, (dst, src) => {
            for (int i = 0; i < src.Length; i++) {
                char c = src[i];
                if (c == '\\' || c == '/') {
                    dst[i] = '.';
                    continue;
                }
                dst[i] = c;
            }
            ".xaml".CopyTo(dst[src.Length..]);
        });
    }
    public static void WriteAttribute(this StreamWriter writer, string key, string value) {
        writer.Write(' ');
        writer.Write(key);
        writer.Write("=\"");
        writer.Write(value);
        writer.Write('"');
    }
    public static (int, Dictionary<string, string>) ParseAttributes(string text) {
        var result = new Dictionary<string, string>();

        char quoteChar = '\0';

        int startIndex = DataTemplate.Length;
        string? currentName = null;
        int i = startIndex;
        for (; i < text.Length; i++) {
            char c = text[i];
            if (c == '"' || c == '\'') {
                if (quoteChar == c) {
                    quoteChar = '\0';
                    if (currentName is null)
                        throw new Exception("There is no name for the attribute value");
                    result.Add(currentName, text[startIndex..i]);
                }
                else {
                    quoteChar = c;
                }
                startIndex = i + 1;
                continue;
            }
            if (quoteChar != '\0')
                continue;
            if (c == '>')
                break;
            if (c != '=')
                continue;
            int nameEnd = i;
            while (nameEnd >= startIndex && char.IsWhiteSpace(text[nameEnd]))
                nameEnd--;
            while (startIndex <= nameEnd && char.IsWhiteSpace(text[startIndex]))
                startIndex++;
            if (nameEnd == startIndex)
                throw new Exception("There is no name for the attribute");
            currentName = text[startIndex..nameEnd];
            startIndex = i + 1;
        }
        if (i >= text.Length || text[i] != '>')
            throw new Exception("The tag must be closed");
        return (i + 1, result);
    }
    static string GetCommonRoot(string a, string b) {
        var aParts = Path.GetFullPath(a).Split(Path.DirectorySeparatorChar);
        var bParts = Path.GetFullPath(b).Split(Path.DirectorySeparatorChar);

        int len = Math.Min(aParts.Length, bParts.Length);
        int i = 0;

        for (; i < len; i++) {
            if (!string.Equals(aParts[i], bParts[i], StringComparison.OrdinalIgnoreCase))
                break;
        }

        return string.Join(Path.DirectorySeparatorChar, aParts.Take(i));
    }
}
