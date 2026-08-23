using System.Text.RegularExpressions;
using Xunit;

namespace RinkuLib.Tests.Documentation;

public class MarkdownDocumentationStructureTests {
    static readonly Regex MarkdownLink = new(@"\[[^\]]+\]\(([^)]+)\)");
    static readonly Regex FrontMatter = new(@"\A---\r?\n(?<body>.*?)\r?\n---(?:\r?\n|\z)", RegexOptions.Singleline);
    static readonly Regex Word = new(@"[\p{L}\p{N}][\p{L}\p{N}'’-]*");

    [Fact]
    public void Pages_and_navigation_reference_existing_files() {
        string docsRoot = FindDocsRoot();
        var failures = new List<string>();

        foreach (string file in Directory.EnumerateFiles(docsRoot, "*.md", SearchOption.AllDirectories)) {
            string content = File.ReadAllText(file);
            string relative = Path.GetRelativePath(docsRoot, file);
            if (TryGetRedirectUrl(content, out string? redirectUrl)) {
                string path = redirectUrl.Split('#', 2)[0];
                if (!Regex.IsMatch(path, @"^https?://")) {
                    if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                        path = Path.ChangeExtension(path, ".md");
                    else if (path.EndsWith('/'))
                        path += "index.md";
                    string target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, path));
                    if (!File.Exists(target) && !Directory.Exists(target))
                        failures.Add($"{relative} redirects to missing target {redirectUrl}.");
                }
            }
            else {
                int titles = Regex.Matches(content, @"(?m)^# (?!#).+$").Count;
                if (titles != 1)
                    failures.Add($"{relative} must contain exactly one level-one heading; found {titles}.");
            }

            foreach (Match match in MarkdownLink.Matches(content)) {
                string link = match.Groups[1].Value.Trim();
                if (Regex.IsMatch(link, @"^(https?://|mailto:|xref:|#)"))
                    continue;
                string path = link.Split('#', 2)[0];
                if (path.Length == 0)
                    continue;
                string target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, path));
                if (!File.Exists(target) && !Directory.Exists(target))
                    failures.Add($"{relative} links to missing target {link}.");
            }
        }

        foreach (string toc in Directory.EnumerateFiles(docsRoot, "toc.yml", SearchOption.AllDirectories)) {
            foreach (string line in File.ReadLines(toc)) {
                Match match = Regex.Match(line, @"^\s*href:\s*(.+?)\s*$");
                if (!match.Success)
                    continue;
                string href = match.Groups[1].Value.Trim('"', '\'');
                if (Regex.IsMatch(href, @"^(https?://|#)"))
                    continue;
                string path = href.Split('#', 2)[0];
                string target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(toc)!, path));
                if (!File.Exists(target) && !Directory.Exists(target))
                    failures.Add($"{Path.GetRelativePath(docsRoot, toc)} links to missing target {href}.");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Prose_uses_readable_paragraph_lengths() {
        string docsRoot = FindDocsRoot();
        var failures = new List<string>();

        foreach (string file in Directory.EnumerateFiles(docsRoot, "*.md", SearchOption.AllDirectories)) {
            if (TryGetRedirectUrl(File.ReadAllText(file), out _))
                continue;
            string[] lines = File.ReadAllLines(file);
            var paragraph = new List<string>();
            bool inFence = false;
            int start = 0;

            void CheckParagraph() {
                if (paragraph.Count == 0)
                    return;
                string text = string.Join(' ', paragraph).Trim();
                if (IsLinkOnlyParagraph(text)) {
                    paragraph.Clear();
                    return;
                }
                int words = Word.Matches(text).Count;
                if (words <= 3 || words > 65)
                    failures.Add($"{Path.GetRelativePath(docsRoot, file)}:{start} [{words} words] {text}");
                paragraph.Clear();
            }

            for (int index = 0; index < lines.Length; index++) {
                string line = lines[index];
                if (line.StartsWith("```", StringComparison.Ordinal)) {
                    CheckParagraph();
                    inFence = !inFence;
                    continue;
                }
                if (inFence)
                    continue;
                if (string.IsNullOrWhiteSpace(line) || Regex.IsMatch(line, @"^\s*(#|[-*+] |\d+\. |\||<)")) {
                    CheckParagraph();
                    continue;
                }
                if (paragraph.Count == 0)
                    start = index + 1;
                paragraph.Add(line.Trim());
            }
            CheckParagraph();
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    static bool IsLinkOnlyParagraph(string text) {
        string remaining = text.Trim();
        if (remaining.StartsWith("See ", StringComparison.OrdinalIgnoreCase))
            remaining = remaining[4..].Trim();
        remaining = remaining.TrimEnd('.');
        return remaining.Split(" · ", StringSplitOptions.RemoveEmptyEntries)
            .All(part => part.StartsWith("[") && part.Contains("](", StringComparison.Ordinal) && part.EndsWith(")"));
    }

    [Fact]
    public void Reference_pages_cover_the_supported_surface() {
        string docsRoot = FindDocsRoot();
        string allMarkdown = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(docsRoot, "*.md", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        Assert.DoesNotContain("ResetParamCache", allMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("TypeParsingInfo.DefaultFactory", allMarkdown, StringComparison.Ordinal);

        string errors = File.ReadAllText(Path.Combine(docsRoot, "articles", "reference", "errors.md"));
        string[] requiredCodes = [
            "RINKU1001", "RINKU1002", "RINKU1003", "RINKU1004", "RINKU1005", "RINKU1006", "RINKU1007", "RINKU1008",
            "RINKU2001", "RINKU2002", "RINKU2003", "RINKU2004", "RINKU2005", "RINKU2006",
            "RINKU3001", "RINKU3002", "RINKU3003", "RINKU3004",
            "RINKU4001", "RINKU4002", "RINKU4003", "RINKU4004", "RINKU4005",
            "RINKU5001", "RINKU5002", "RINKU5003", "RINKU5004", "RINKU5005", "RINKU5006", "RINKU5007",
            "RINKU6001", "RINKU6002", "RINKU6003", "RINKU6004", "RINKU9001"
        ];
        foreach (string code in requiredCodes)
            Assert.Contains(code, errors, StringComparison.Ordinal);
    }

    static string FindDocsRoot() {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent) {
            string path = Path.Combine(folder.FullName, "docs", "articles");
            if (Directory.Exists(path))
                return Path.Combine(folder.FullName, "docs");
        }
        throw new DirectoryNotFoundException("Could not find the documentation directory.");
    }

    static bool TryGetRedirectUrl(string content, out string redirectUrl) {
        Match frontMatter = FrontMatter.Match(content);
        if (frontMatter.Success) {
            Match redirect = Regex.Match(frontMatter.Groups["body"].Value, @"(?m)^redirect_url:\s*(.+?)\s*$");
            if (redirect.Success) {
                redirectUrl = redirect.Groups[1].Value.Trim('"', '\'');
                return true;
            }
        }
        redirectUrl = "";
        return false;
    }
}
