using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Rinku;
using Xunit;

namespace RinkuLib.Tests.Documentation;

public class MarkdownExampleTests {
    static readonly Lazy<IReadOnlyDictionary<string, ExampleBlock>> Examples = new(LoadExamples);

    public static IEnumerable<object?[]> FencedExamples()
        => Examples.Value.Keys.Order(StringComparer.Ordinal)
            .Select(id => new object?[] { id });

    [Theory]
    [MemberData(nameof(FencedExamples))]
    public void Fenced_example_is_valid(string id) {
        ExampleBlock block = Examples.Value[id];

        try {
            ExampleVerifier.Verify(block);
        }
        catch (Exception error) {
            throw new InvalidDataException(
                $"{block.Id} at line {block.Line}: {error.Message}",
                error);
        }
    }

    static IReadOnlyDictionary<string, ExampleBlock> LoadExamples() {
        string docsRoot = Path.Combine(FindRepositoryRoot().FullName, "docs");
        IReadOnlyList<ExampleBlock> blocks = MarkdownExamples.Read(docsRoot);
        ExampleCompiler.Configure(blocks);
        return blocks.ToDictionary(block => block.Id, StringComparer.Ordinal);
    }

    static DirectoryInfo FindRepositoryRoot() {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (Directory.Exists(Path.Combine(folder.FullName, "docs", "articles")))
                return folder;
        throw new DirectoryNotFoundException("Could not find the documentation directory.");
    }
}

internal sealed record ExampleBlock(
    string Id,
    string RelativePath,
    int Ordinal,
    int Line,
    string Language,
    string Content,
    string Hash);

internal static class MarkdownExamples {
    static readonly HashSet<string> SupportedLanguages = new(
        ["bash", "csharp", "ini", "json", "sql", "text"],
        StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<ExampleBlock> Read(string docsRoot) {
        var result = new List<ExampleBlock>();

        foreach (string file in Directory.EnumerateFiles(
            docsRoot,
            "*.md",
            SearchOption.AllDirectories).Order()) {
            string relative = Path.GetRelativePath(docsRoot, file)
                .Replace('\\', '/');
            string[] lines = File.ReadAllLines(file);
            int ordinal = 0;

            for (int index = 0; index < lines.Length; index++) {
                if (!lines[index].StartsWith("```", StringComparison.Ordinal))
                    continue;

                string language = lines[index][3..].Trim().ToLowerInvariant();
                if (!SupportedLanguages.Contains(language))
                    language = language.Length == 0 ? "(none)" : language;

                int start = index + 2;
                var content = new StringBuilder();
                bool closed = false;

                for (index++; index < lines.Length; index++) {
                    if (lines[index].StartsWith("```", StringComparison.Ordinal)) {
                        closed = true;
                        break;
                    }

                    content.AppendLine(lines[index]);
                }

                if (!closed)
                    throw new InvalidDataException(
                        $"{relative}:{start - 1} contains an unclosed fence.");

                ordinal++;
                string value = content.ToString().TrimEnd('\r', '\n');
                string hash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
                string id = $"{relative}#{ordinal:D3}:{language}:{hash}";

                result.Add(new ExampleBlock(
                    id,
                    relative,
                    ordinal,
                    start,
                    language,
                    value,
                    hash));
            }
        }

        return result;
    }
}

internal static class ExampleVerifier {
    static readonly HashSet<string> ExpectedInvalidSql = [
        "E260385EB856",
        "BE11F74EED1D"
    ];
    static readonly CSharpParseOptions CSharpOptions = new(
        LanguageVersion.Preview,
        DocumentationMode.Diagnose,
        SourceCodeKind.Regular);

    public static void Verify(ExampleBlock block) {
        if (string.IsNullOrWhiteSpace(block.Content))
            throw new InvalidDataException("The example is empty.");

        switch (block.Language) {
            case "csharp":
                VerifyCSharpSyntax(block.Content);
                ExampleCompiler.Verify(block);
                break;
            case "json":
                JsonDocument.Parse(block.Content).Dispose();
                break;
            case "ini":
                VerifyIni(block.Content);
                break;
            case "bash":
                VerifyBash(block.Content);
                break;
            case "sql":
                bool expectedInvalid = ExpectedInvalidSql.Contains(block.Hash);
                try {
                    VerifySql(block.Content);
                }
                catch (InvalidDataException) when (expectedInvalid) {
                    // These examples intentionally demonstrate malformed SQL.
                    break;
                }
                if (expectedInvalid)
                    throw new InvalidDataException(
                        "The error example unexpectedly became balanced SQL.");
                break;
            case "text":
                break;
            default:
                throw new InvalidDataException(
                    $"No verifier is registered for language '{block.Language}'.");
        }
    }

    static void VerifyCSharpSyntax(string code) {
        string normalized = code.Replace(
            "static readonly ",
            "",
            StringComparison.Ordinal);
        string reordered = ReorderTopLevelStatements(normalized);
        string scoped = ScopeTopLevelMembers(normalized);

        string[] candidates = [
            code,
            $"class Example {{\n{code}\n}}",
            $"class Example {{\nvoid Run() {{\n{code}\n}}\n}}",
            normalized,
            reordered,
            scoped,
            $"class Example {{\nvoid Run() {{\n{normalized}\n}}\n}}",
            $"class Example {{\nvoid Run() {{\ntry {{ }}\n{code}\n}}\n}}"
        ];

        Diagnostic[] best = candidates
            .Select(candidate => CSharpSyntaxTree
                .ParseText(candidate, CSharpOptions)
                .GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray())
            .OrderBy(diagnostics => diagnostics.Length)
            .First();

        if (best.Length == 0)
            return;

        string message = string.Join(
            " | ",
            best.Take(3).Select(diagnostic => diagnostic.GetMessage()));
        throw new InvalidDataException($"C# syntax is not valid: {message}");
    }

    static string ReorderTopLevelStatements(string code) {
        CompilationUnitSyntax root = CSharpSyntaxTree
            .ParseText(code, CSharpOptions)
            .GetCompilationUnitRoot();
        MemberDeclarationSyntax[] statements = root.Members
            .Where(member => member is GlobalStatementSyntax)
            .ToArray();
        MemberDeclarationSyntax[] declarations = root.Members
            .Where(member => member is not GlobalStatementSyntax)
            .ToArray();

        return root.WithMembers([
            .. statements,
            .. declarations
        ]).ToFullString();
    }

    static string ScopeTopLevelMembers(string code) {
        CompilationUnitSyntax root = CSharpSyntaxTree
            .ParseText(code, CSharpOptions)
            .GetCompilationUnitRoot();
        string declarations = string.Join(
            Environment.NewLine,
            root.Members
                .Where(member => member is not GlobalStatementSyntax)
                .Select(member => member.ToFullString()));
        string statements = string.Join(
            Environment.NewLine,
            root.Members
                .OfType<GlobalStatementSyntax>()
                .Select(statement => statement.Statement.ToFullString()));

        return $$"""
            class Example
            {
                {{declarations}}

                void Run()
                {
                    {{statements}}
                }
            }
            """;
    }

    static void VerifyIni(string content) {
        int lineNumber = 0;

        foreach (string sourceLine in content.Split('\n')) {
            lineNumber++;
            string line = sourceLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']') && line.Length > 2)
                continue;

            int equals = line.IndexOf('=');
            if (equals <= 0)
                throw new InvalidDataException(
                    $"INI line {lineNumber} is neither a section nor a key/value pair.");
        }
    }

    static void VerifyBash(string content) {
        foreach (string line in content.Split('\n')) {
            string command = line.Trim();
            if (command.Length == 0)
                continue;
            if (!command.StartsWith("dotnet ", StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Unsupported shell example '{command}'.");
        }
    }

    static void VerifySql(string content) {
        int parentheses = 0;
        bool singleQuote = false;
        bool doubleQuote = false;
        bool bracket = false;
        bool lineComment = false;
        bool blockComment = false;

        for (int index = 0; index < content.Length; index++) {
            char current = content[index];
            char next = index + 1 < content.Length ? content[index + 1] : '\0';

            if (lineComment) {
                if (current == '\n')
                    lineComment = false;
                continue;
            }
            if (blockComment) {
                if (current == '*' && next == '/') {
                    blockComment = false;
                    index++;
                }
                continue;
            }
            if (!singleQuote && !doubleQuote && !bracket) {
                if (current == '-' && next == '-') {
                    lineComment = true;
                    index++;
                    continue;
                }
                if (current == '/' && next == '*') {
                    blockComment = true;
                    index++;
                    continue;
                }
            }
            if (!doubleQuote && !bracket && current == '\'') {
                if (singleQuote && next == '\'') {
                    index++;
                    continue;
                }
                singleQuote = !singleQuote;
                continue;
            }
            if (!singleQuote && !bracket && current == '"') {
                if (doubleQuote && next == '"') {
                    index++;
                    continue;
                }
                doubleQuote = !doubleQuote;
                continue;
            }
            if (!singleQuote && !doubleQuote) {
                if (!bracket && current == '[') {
                    bracket = true;
                    continue;
                }
                if (bracket && current == ']') {
                    if (next == ']') {
                        index++;
                        continue;
                    }
                    bracket = false;
                    continue;
                }
            }
            if (singleQuote || doubleQuote || bracket)
                continue;
            if (current == '(')
                parentheses++;
            else if (current == ')' && --parentheses < 0)
                throw new InvalidDataException("SQL has an unmatched closing parenthesis.");
        }

        if (singleQuote || doubleQuote || bracket || blockComment || parentheses != 0)
            throw new InvalidDataException(
                "SQL has an unclosed quote, comment, bracket, or parenthesis.");
    }
}

internal static class ExampleCompiler {
    const string Usings = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.ComponentModel;
        using System.Data;
        using System.Data.Common;
        using System.Diagnostics;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Reflection;
        using System.Reflection.Emit;
        using System.Text;
        using System.Text.Json;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Data.SqlClient;
        using Rinku;
        using Rinku.Mapping;
        using Rinku.Mapping.Defaults;
        using Rinku.Mapping.Parsers;
        using Rinku.Mapping.Parsers.Defaults;
        using Rinku.Querying;
        using Rinku.Querying.Defaults;
        using Rinku.Querying.Parameters;
        using Rinku.Tracking;
        """;

    static readonly CSharpParseOptions ParseOptions = new(
        LanguageVersion.Preview,
        DocumentationMode.Diagnose,
        SourceCodeKind.Regular);
    static readonly MetadataReference[] References = CreateReferences();
    static readonly HashSet<string> StubMembers = [];
    static readonly List<PageDeclaration> PageDeclarations = [];
    static readonly List<PageField> PageFields = [];
    static readonly HashSet<string> ObjectMembers = new(StringComparer.Ordinal) {
        nameof(object.Equals),
        "Finalize",
        nameof(object.GetHashCode),
        nameof(object.GetType),
        "MemberwiseClone",
        nameof(object.ReferenceEquals),
        nameof(object.ToString)
    };
    static int assemblyIndex;

    public static void Configure(IEnumerable<ExampleBlock> blocks) {
        foreach (ExampleBlock block in blocks.Where(block => block.Language == "csharp")) {
            string normalized = block.Content.Replace(
                "static readonly ",
                "",
                StringComparison.Ordinal);
            SyntaxNode root = CSharpSyntaxTree.ParseText(normalized, ParseOptions).GetRoot();
            SyntaxNode declarationRoot = CSharpSyntaxTree
                .ParseText(block.Content, ParseOptions)
                .GetRoot();
            foreach (VariableDeclarationSyntax declaration in root.DescendantNodes()
                .OfType<VariableDeclarationSyntax>()) {
                if (declaration.Type.IsVar)
                    continue;

                foreach (VariableDeclaratorSyntax variable in declaration.Variables) {
                    PageFields.Add(new PageField(
                        block.RelativePath,
                        block.Ordinal,
                        variable.Identifier.ValueText,
                        declaration.Type.ToString()));
                }
            }

            if (declarationRoot is CompilationUnitSyntax compilationUnit) {
                MemberDeclarationSyntax[] declarations = compilationUnit.Members
                    .Where(member => member is not GlobalStatementSyntax)
                    .ToArray();
                foreach (BaseTypeDeclarationSyntax declaration in declarations
                    .SelectMany(member => member.DescendantNodesAndSelf())
                    .OfType<BaseTypeDeclarationSyntax>()) {
                    string declarationSource = declaration.ToFullString();
                    PageDeclarations.Add(new PageDeclaration(
                        block.RelativePath,
                        block.Ordinal,
                        declaration.Identifier.ValueText,
                        declarationSource));
                }
            }

            foreach (MemberAccessExpressionSyntax access in root.DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()) {
                string name = access.Name.Identifier.ValueText;
                if (SyntaxFacts.IsValidIdentifier(name) && !ObjectMembers.Contains(name))
                    StubMembers.Add(name);
            }

            foreach (AssignmentExpressionSyntax assignment in root.DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()) {
                if (assignment.Left is IdentifierNameSyntax identifier &&
                    SyntaxFacts.IsValidIdentifier(identifier.Identifier.ValueText)) {
                    StubMembers.Add(identifier.Identifier.ValueText);
                }
            }
        }
    }

    public static void Verify(ExampleBlock block) {
        var unknownFields = new Dictionary<string, string>(StringComparer.Ordinal);
        var unknownTypes = new Dictionary<string, int>(StringComparer.Ordinal);
        Diagnostic[] best = [];

        for (int attempt = 0; attempt < 10; attempt++) {
            string scoped = BuildScopedSource(block, unknownFields, unknownTypes);
            string raw = BuildRawSource(block, unknownTypes);
            var results = new List<Diagnostic[]> {
                Compile(scoped, OutputKind.DynamicallyLinkedLibrary)
            };
            string? fragment = BuildFragmentSource(block);
            if (fragment is not null)
                results.Add(Compile(fragment, OutputKind.DynamicallyLinkedLibrary));
            if (block.Content.Contains("static readonly ", StringComparison.Ordinal))
                results.Add(Compile(
                    $"{Usings}\nclass MemberExample {{\n{block.Content}\n}}",
                    OutputKind.DynamicallyLinkedLibrary));
            if (block.Content.Contains("namespace ", StringComparison.Ordinal) &&
                block.Content.Split('\n')[0].TrimEnd('\r').EndsWith(';')) {
                results.Add(Compile(raw, OutputKind.DynamicallyLinkedLibrary));
                results.Add(Compile(raw, OutputKind.ConsoleApplication));
                results.Add(Compile(
                    BuildBlockNamespaceSource(block),
                    OutputKind.DynamicallyLinkedLibrary));
            }
            best = results.OrderBy(diagnostics => diagnostics.Length).First();

            if (best.Length == 0)
                return;

            if (IsExpectedCompilerExample(block, best))
                return;

            bool added = AddMissingScaffolding(
                block,
                best,
                unknownFields,
                unknownTypes,
                scoped,
                raw);
            if (!added)
                break;
        }

        string message = string.Join(
            " | ",
            best.Take(4).Select(diagnostic =>
                $"{diagnostic.Id} {diagnostic.GetMessage()}"));
        throw new InvalidDataException($"C# does not compile: {message}");
    }

    static Diagnostic[] Compile(string source, OutputKind outputKind) {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        CSharpCompilation compilation = CSharpCompilation.Create(
            $"DocsExample{Interlocked.Increment(ref assemblyIndex)}",
            [tree],
            References,
            new CSharpCompilationOptions(
                outputKind,
                nullableContextOptions: NullableContextOptions.Enable,
                allowUnsafe: true));

        return compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
    }

    static string? BuildFragmentSource(ExampleBlock block) {
        if (block.Content.Contains(
            "public override void ResetCache(DbParamInfo inferred)",
            StringComparison.Ordinal)) {
            return $$"""
                {{Usings}}
                abstract class HandlerFragment : SpecialHandler
                {
                    DbParamInfo parameter = DbParameterDefaults.Current.Inferred;
                    DbParamInfo firstParameter = DbParameterDefaults.Current.Inferred;
                    DbParamInfo secondParameter = DbParameterDefaults.Current.Inferred;
                    {{block.Content}}
                }
                """;
        }

        if (block.Content.Contains(
            "public override bool CanParse(ColumnInfo[] schema)",
            StringComparison.Ordinal)) {
            return $$"""
                {{Usings}}
                abstract class ParserFragment : BaseTypeParser<object>
                {
                    ITypeParser<object> inner = null!;
                    {{block.Content}}
                }
                """;
        }

        if (block.Content.Contains("void OnParserDisposing(", StringComparison.Ordinal)) {
            return $$"""
                {{Usings}}
                class ParserListener
                {
                    ITypeParser<object>? retainedParser;

                    void Register()
                    {
                        {{block.Content}}
                    }
                }
                """;
        }

        return null;
    }

    static bool IsExpectedCompilerExample(
        ExampleBlock block,
        IReadOnlyCollection<Diagnostic> diagnostics)
        => block.RelativePath == "articles/codegen/analyzers.md" &&
           block.Content.Contains("=> Save;", StringComparison.Ordinal) &&
           diagnostics.All(diagnostic => diagnostic.Id == "CS0428");

    static string BuildScopedSource(
        ExampleBlock block,
        IReadOnlyDictionary<string, string> unknownFields,
        IReadOnlyDictionary<string, int> unknownTypes) {
        string normalized = block.Content.Replace(
            "static readonly ",
            "",
            StringComparison.Ordinal);
        CompilationUnitSyntax root = CSharpSyntaxTree
            .ParseText(normalized, ParseOptions)
            .GetCompilationUnitRoot();
        IEnumerable<MemberDeclarationSyntax> rootMembers = root.Members;

        if (root.Members.Count == 1 &&
            root.Members[0] is FileScopedNamespaceDeclarationSyntax fileNamespace) {
            rootMembers = fileNamespace.Members;
        }

        string declarations = string.Join(
            Environment.NewLine,
            rootMembers
                .Where(member => member is not GlobalStatementSyntax)
                .Select(member => member.ToFullString()));
        HashSet<string> currentTypeNames = rootMembers
            .Where(member => member is not GlobalStatementSyntax)
            .SelectMany(member => member.DescendantNodesAndSelf())
            .OfType<BaseTypeDeclarationSyntax>()
            .Select(declaration => declaration.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);
        PageDeclaration[] importedDeclarations = GetImportedDeclarations(block).ToArray();
        string pageDeclarations = string.Join(
            Environment.NewLine,
            PageDeclarations
                .Where(declaration =>
                    declaration.RelativePath == block.RelativePath &&
                    declaration.Ordinal < block.Ordinal &&
                    !currentTypeNames.Contains(declaration.Name) &&
                    !declaration.Source.Contains("this DbConnection", StringComparison.Ordinal))
                .GroupBy(declaration => declaration.Name, StringComparer.Ordinal)
                .Select(group => group.MaxBy(declaration => declaration.Ordinal)!.Source)
                .Concat(importedDeclarations
                    .Where(declaration => !currentTypeNames.Contains(declaration.Name))
                    .Select(declaration => declaration.Source)));
        HashSet<string> availableTypeNames = PageDeclarations
            .Where(declaration =>
                declaration.RelativePath == block.RelativePath &&
                declaration.Ordinal < block.Ordinal)
            .Select(declaration => declaration.Name)
            .Concat(importedDeclarations.Select(declaration => declaration.Name))
            .Concat(currentTypeNames)
            .ToHashSet(StringComparer.Ordinal);
        string statements = string.Join(
            Environment.NewLine,
            rootMembers
                .OfType<GlobalStatementSyntax>()
                .Select(statement => statement.Statement.ToFullString()));
        HashSet<string> currentVariableNames = root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Select(variable => variable.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> usedIdentifiers = root.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Select(identifier => identifier.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);
        IEnumerable<KeyValuePair<string, string>> precedingFields = PageFields
            .Where(field =>
                field.RelativePath == block.RelativePath &&
                field.Ordinal < block.Ordinal &&
                usedIdentifiers.Contains(field.Name) &&
                !currentVariableNames.Contains(field.Name) &&
                !unknownFields.ContainsKey(field.Name))
            .GroupBy(field => field.Name, StringComparer.Ordinal)
            .Select(group => group.MaxBy(field => field.Ordinal)!)
            .Select(field => new KeyValuePair<string, string>(
                field.Name,
                field.TypeSource));
        string fields = string.Join(
            Environment.NewLine,
            unknownFields.Concat(precedingFields).Select(pair =>
                $"protected static {pair.Value} {Escape(pair.Key)} = default!;"));
        string stubs = BuildTypeStubs(block.RelativePath, unknownTypes, availableTypeNames);
        string tryPrefix = statements.TrimStart().StartsWith("catch", StringComparison.Ordinal)
            ? "try { }"
            : "";

        return $$"""
            {{Usings}}

            namespace DocumentationExample
            {
                [AttributeUsage(AttributeTargets.Parameter)]
                public sealed class TrueNameAttribute(string name) : Attribute;

                public sealed record GetAlbumsByArtistResult(
                    int Id,
                    string Title,
                    int? ReleaseYear);

                public static class GeneratedCommandFixture
                {
                    public static DbCommand GetAlbumsByArtist(
                        this DbConnection connection,
                        int artistId) => connection.CreateCommand();
                }

                public abstract class DocumentationContext
                {
                    protected DbConnection cnn = null!;
                    protected DbTransaction transaction = null!;
                    protected dynamic command = null!;
                    protected CancellationToken cancellationToken;
                    protected ColumnInfo[] columns = [];
                    protected string connectionString = "";
                    protected dynamic request = null!;
                    protected dynamic db = null!;
                    protected dynamic parser = null!;
                    protected static DbConnection GetConnection() => null!;
                    protected static IDbConnection GetLegacyConnection() => null!;
                    protected static DbCommand CreateUnboundCommand() => null!;
                    protected static dynamic LoadPlaylists() => null!;
                    protected static void DeletePlaylists(object values) { }
                    protected static bool SupportsProviderMetadata(IDbCommand command) => true;
                    protected static void Show(params object?[] values) { }
                }

                public class Example : DocumentationContext
                {
                    {{fields}}
                    {{stubs}}
                    {{pageDeclarations}}
                    {{declarations}}

                    public async Task<dynamic> Run()
                    {
                        {{tryPrefix}}
                        {{statements}}
                        return null!;
                    }
                }
            }
            """;
    }

    static IEnumerable<PageDeclaration> GetImportedDeclarations(ExampleBlock block) {
        var requests = new List<(string Path, string Name)>();

        if (block.RelativePath is "articles/customization/index.md" or
            "articles/mapping/registration.md") {
            requests.Add(("articles/customization/type-registration.md", "LocalDate"));
            requests.Add(("articles/customization/type-registration.md", "LocalDateTypeParsingInfo"));
        }

        if (block.RelativePath == "articles/customization/index.md") {
            requests.Add(("articles/customization/parameters.md", "Names"));
            requests.Add(("articles/customization/parameters.md", "NamesParamInfo"));
            requests.Add(("articles/customization/conditional-sql.md", "SortDirectionHandler"));
            requests.Add(("articles/customization/conditional-sql.md", "SortDirection"));
        }

        foreach ((string path, string name) in requests) {
            PageDeclaration? declaration = PageDeclarations
                .Where(candidate =>
                    candidate.RelativePath == path &&
                    candidate.Name == name)
                .MinBy(candidate => candidate.Ordinal);
            if (declaration is not null)
                yield return declaration;
        }
    }

    static string BuildRawSource(
        ExampleBlock block,
        IReadOnlyDictionary<string, int> unknownTypes)
        => $"{Usings}\n{block.Content}\n{BuildGlobalTypeStubs(block.RelativePath, unknownTypes)}";

    static string BuildBlockNamespaceSource(ExampleBlock block) {
        string[] lines = block.Content.Split('\n');
        string namespaceName = lines[0].Trim()
            .Replace("namespace ", "", StringComparison.Ordinal)
            .TrimEnd(';');
        string body = string.Join('\n', lines.Skip(1));
        return $$"""
            {{Usings}}
            namespace {{namespaceName}}
            {
                [AttributeUsage(AttributeTargets.Parameter)]
                public sealed class TrueNameAttribute(string name) : Attribute;
                {{body}}
            }
            """;
    }

    static bool AddMissingScaffolding(
        ExampleBlock block,
        IEnumerable<Diagnostic> diagnostics,
        IDictionary<string, string> unknownFields,
        IDictionary<string, int> unknownTypes,
        string scoped,
        string raw) {
        bool added = false;

        foreach (Diagnostic diagnostic in diagnostics) {
            if (diagnostic.Id == "CS0103") {
                string? name = QuotedName(diagnostic.GetMessage());
                if (name is not null) {
                    if (HasEarlierPageField(block, name) ||
                        IsLikelyCommandName(name)) {
                        if (!unknownFields.ContainsKey(name)) {
                            unknownFields[name] = InferFieldType(block, name);
                            added = true;
                        }
                    }
                    else if (char.IsUpper(name[0]) &&
                        SyntaxFacts.IsValidIdentifier(name)) {
                        if (!unknownTypes.ContainsKey(name)) {
                            unknownTypes[name] = 0;
                            added = true;
                        }
                    }
                    else if (!unknownFields.ContainsKey(name)) {
                        unknownFields[name] = InferFieldType(block, name);
                        added = true;
                    }
                }
            }
            else if (diagnostic.Id == "CS0246") {
                string? name = QuotedName(diagnostic.GetMessage());
                if (name is null)
                    continue;

                string simpleName = name.Split('<', StringSplitOptions.TrimEntries)[0];
                int arity = name.Contains('<')
                    ? name.Count(character => character == ',') + 1
                    : 0;
                if (SyntaxFacts.IsValidIdentifier(simpleName) &&
                    !unknownTypes.ContainsKey(simpleName)) {
                    unknownTypes[simpleName] = arity;
                    added = true;
                }
            }
        }

        return added;
    }

    static bool HasEarlierPageField(ExampleBlock block, string name)
        => PageFields.Any(field =>
            field.RelativePath == block.RelativePath &&
            field.Ordinal < block.Ordinal &&
            field.Name == name);

    static bool IsLikelyCommandName(string name)
        => name.StartsWith("Get", StringComparison.Ordinal) ||
           name.StartsWith("Find", StringComparison.Ordinal) ||
           name.StartsWith("Search", StringComparison.Ordinal) ||
           name.StartsWith("Update", StringComparison.Ordinal) ||
           name.StartsWith("Insert", StringComparison.Ordinal) ||
           name.StartsWith("Read", StringComparison.Ordinal) ||
           name.StartsWith("Count", StringComparison.Ordinal) ||
           name.StartsWith("Clear", StringComparison.Ordinal) ||
           name.StartsWith("Rename", StringComparison.Ordinal) ||
           name.StartsWith("Save", StringComparison.Ordinal) ||
           name.StartsWith("Add", StringComparison.Ordinal) ||
           name.StartsWith("Delete", StringComparison.Ordinal);

    static string InferFieldType(ExampleBlock block, string name) {
        string? pageType = PageFields
            .Where(field =>
                field.RelativePath == block.RelativePath &&
                field.Ordinal < block.Ordinal &&
                field.Name == name)
            .MaxBy(field => field.Ordinal)?.TypeSource;
        if (pageType is not null)
            return pageType;

        if (name.Equals("cnn", StringComparison.OrdinalIgnoreCase))
            return "DbConnection";
        if (name.Equals("transaction", StringComparison.OrdinalIgnoreCase))
            return "DbTransaction";
        if (name.Equals("cancellationToken", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("ct", StringComparison.OrdinalIgnoreCase))
            return "CancellationToken";
        if (name.Equals("columns", StringComparison.OrdinalIgnoreCase))
            return "ColumnInfo[]";
        if (name.Equals("sql", StringComparison.OrdinalIgnoreCase))
            return "string";
        if (name.Length > 0 && char.IsLower(name[0]) &&
            name.EndsWith("Command", StringComparison.Ordinal))
            return "DbCommand";
        if (IsLikelyCommandName(name))
            return "QueryCommand";

        return "dynamic";
    }

    static string BuildTypeStubs(
        string relativePath,
        IReadOnlyDictionary<string, int> unknownTypes,
        IReadOnlySet<string> availableTypeNames)
        => string.Join(
            Environment.NewLine,
            unknownTypes
                .Where(pair => !availableTypeNames.Contains(pair.Key))
                .Select(pair => GetDeclarationOrStub(
                    relativePath,
                    pair.Key,
                    pair.Value,
                    nested: true))
                .Distinct(StringComparer.Ordinal));

    static string BuildGlobalTypeStubs(
        string relativePath,
        IReadOnlyDictionary<string, int> unknownTypes)
        => string.Join(
            Environment.NewLine,
            unknownTypes
                .Select(pair => GetDeclarationOrStub(
                    relativePath,
                    pair.Key,
                    pair.Value,
                    nested: false))
                .Distinct(StringComparer.Ordinal));

    static string GetDeclarationOrStub(
        string relativePath,
        string name,
        int arity,
        bool nested) {
        return BuildTypeStub(name, arity, nested);
    }

    static string BuildTypeStub(string name, int arity, bool nested) {
        string accessibility = nested ? "public" : "internal";
        string typeParameters = arity == 0
            ? ""
            : $"<{string.Join(", ", Enumerable.Range(1, arity).Select(index => $"T{index}"))}>";
        string members = string.Join(
            Environment.NewLine,
            StubMembers
                .Where(member => !member.Equals(name, StringComparison.Ordinal))
                .Select(member => $"public dynamic {Escape(member)} {{ get; set; }} = null!;"));

        return $$"""
            {{accessibility}} class {{Escape(name)}}{{typeParameters}}
            {
                public {{Escape(name)}}(params object?[] values) { }
                public dynamic this[object key] { get => null!; set { } }
                {{members}}
            }
            """;
    }

    static string Escape(string identifier)
        => SyntaxFacts.GetKeywordKind(identifier) == SyntaxKind.None
            ? identifier
            : $"@{identifier}";

    static string? QuotedName(string message) {
        int first = message.IndexOf('\'');
        if (first < 0)
            return null;
        int second = message.IndexOf('\'', first + 1);
        return second < 0 ? null : message[(first + 1)..second];
    }

    static MetadataReference[] CreateReferences() {
        string[] platformAssemblies = ((string?)AppContext.GetData(
            "TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator) ?? [];
        return platformAssemblies
            .Append(typeof(QueryCommand).Assembly.Location)
            .Append(typeof(Dapper.SqlMapper).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    sealed record PageDeclaration(
        string RelativePath,
        int Ordinal,
        string Name,
        string Source);

    sealed record PageField(
        string RelativePath,
        int Ordinal,
        string Name,
        string TypeSource);
}
