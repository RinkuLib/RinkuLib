using Xunit;

namespace RinkuLib.Tests.Documentation;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class DocumentationExampleAttribute(string page, string id) : Attribute {
    public string Page { get; } = page;
    public string Id { get; } = id;
}

public class CustomizationDocumentationStyleTests {
    private static readonly char[] FancyPunctuation = [';', ':', '\u2014', '\u2013', '\u201C', '\u201D'];
    private static readonly string[] FancyWords = ["lifecycle", "seam", "dispatch", "retained", "compose"];

    [Fact]
    public void Customization_prose_stays_short_and_plain() {
        foreach (string file in GetCustomizationPages()) {
            bool inCode = false;
            var paragraph = new List<string>();
            foreach (string line in File.ReadLines(file)) {
                if (line.StartsWith("```", StringComparison.Ordinal)) {
                    CheckParagraph(file, paragraph);
                    paragraph.Clear();
                    inCode = !inCode;
                    continue;
                }
                if (inCode)
                    continue;

                foreach (char character in FancyPunctuation)
                    Assert.False(line.Contains(character), $"{file} uses '{character}' in prose\n{line}");
                foreach (string word in FancyWords)
                    Assert.False(line.Contains(word, StringComparison.OrdinalIgnoreCase),
                        $"{file} uses '{word}' in prose\n{line}");

                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith("- ")) {
                    CheckParagraph(file, paragraph);
                    paragraph.Clear();
                }
                else {
                    paragraph.Add(line.Trim());
                }
            }
            CheckParagraph(file, paragraph);
        }
    }

    private static void CheckParagraph(string file, List<string> lines) {
        string paragraph = string.Join(' ', lines);
        Assert.True(paragraph.Length <= 300, $"{file} has a prose paragraph longer than 300 characters\n{paragraph}");
    }

    private static string FindCustomizationFolder()
        => Path.Combine(FindRepositoryRoot().FullName, "docs", "articles", "customization");

    private static string[] GetCustomizationPages() {
        string folder = FindCustomizationFolder();
        return File.ReadLines(Path.Combine(folder, "toc.yml"))
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("href: ", StringComparison.Ordinal))
            .Select(line => line[6..].Split('#')[0])
            .Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFullPath(Path.Combine(folder, path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DirectoryInfo FindRepositoryRoot() {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (Directory.Exists(Path.Combine(folder.FullName, "docs", "articles", "customization")))
                return folder;
        throw new DirectoryNotFoundException("Could not find docs/articles/customization.");
    }
}
