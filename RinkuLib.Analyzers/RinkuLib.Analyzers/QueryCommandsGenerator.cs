using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace RinkuLib.Analyzers;

[Generator(LanguageNames.CSharp)]
public sealed class QueryCommandsGenerator : IIncrementalGenerator {
    internal const string AttributeName = "Rinku.QueryCommandsAttribute";
    internal static readonly DiagnosticDescriptor Invalid = new("RK0200", "Invalid SQL command source", "{0}", "Rinku", DiagnosticSeverity.Error, true);
    internal static readonly DiagnosticDescriptor Collision = new("RK0201", "Conflicting SQL command source", "{0}", "Rinku", DiagnosticSeverity.Error, true);

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var files = context.AdditionalTextsProvider.Where(static file => file.Path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) || file.Path.EndsWith(".rnksql", StringComparison.OrdinalIgnoreCase));
        var declarations = context.SyntaxProvider.ForAttributeWithMetadataName(AttributeName, static (node, _) => node is ClassDeclarationSyntax, static (syntax, _) => new Declaration((INamedTypeSymbol)syntax.TargetSymbol, syntax.Attributes[0])).Collect();
        context.RegisterSourceOutput(files.Collect().Combine(context.AnalyzerConfigOptionsProvider).Combine(declarations).Combine(context.CompilationProvider), static (output, input) => Generate(output, input.Left.Left.Left, input.Left.Left.Right, input.Left.Right, input.Right));
    }

    private static void Generate(SourceProductionContext output, ImmutableArray<AdditionalText> files, AnalyzerConfigOptionsProvider options, ImmutableArray<Declaration> declarations, Compilation compilation) {
        string project = Get(options.GlobalOptions, "build_property.MSBuildProjectDirectory", "");
        if (string.IsNullOrEmpty(project)) {
            if (files.Length != 0)
                output.ReportDiagnostic(Diagnostic.Create(Invalid, Location.None, "SQL generation requires MSBuildProjectDirectory. Import Rinku.targets."));
            return;
        }
        foreach (var declaration in declarations) {
            if (string.IsNullOrWhiteSpace(declaration.Path) || declaration.Type.ContainingType is not null || !ValidDeclaration(declaration.Type)) {
                output.ReportDiagnostic(Diagnostic.Create(Invalid, declaration.Type.Locations.FirstOrDefault(), "QueryCommands requires a nonempty path and a top-level, non-generic static partial class."));
                return;
            }
            try { Matches("", declaration.Pattern); }
            catch (ArgumentException error) {
                output.ReportDiagnostic(Diagnostic.Create(Invalid, declaration.Type.Locations.FirstOrDefault(), error.Message));
                return;
            }
        }
        var commands = new List<Command>();
        var paths = new HashSet<string>(Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var file in files.OrderBy(static item => item.Path, StringComparer.Ordinal)) {
            output.CancellationToken.ThrowIfCancellationRequested();
            var metadata = options.GetOptions(file);
            if (!file.Path.EndsWith(".rnksql", StringComparison.OrdinalIgnoreCase) && Get(metadata, "build_metadata.AdditionalFiles.RinkuSql", "") != "true")
                continue;
            try {
                string full = Path.GetFullPath(file.Path);
                if (!paths.Add(full))
                    continue;
                string root = Resolve(project, Setting(metadata, options.GlobalOptions, "Root", project));
                string relative = Relative(root, full);
                string container = Setting(metadata, options.GlobalOptions, "Class", "Queries");
                string ns = Setting(metadata, options.GlobalOptions, "Namespace", Get(options.GlobalOptions, "build_property.RootNamespace", compilation.AssemblyName ?? ""));
                string pattern = Get(metadata, "build_metadata.AdditionalFiles.Pattern", "");
                Declaration? owner = null;
                bool claimed = false;
                foreach (var declaration in declarations) {
                    string source = Resolve(project, declaration.Path);
                    bool isFile = source.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) || source.EndsWith(".rnksql", StringComparison.OrdinalIgnoreCase);
                    string path = isFile ? (SamePath(source, full) ? Path.GetFileName(full) : "../") : Relative(source, full);
                    if (Outside(path))
                        continue;
                    claimed = true;
                    if (!declaration.Recursive && path.IndexOf('/') >= 0)
                        continue;
                    if (!Matches(path, declaration.Pattern))
                        continue;
                    if (owner is not null)
                        throw new ArgumentException($"'{file.Path}' belongs to more than one QueryCommands declaration.");
                    owner = declaration;
                    relative = path;
                    container = declaration.Type.Name;
                    ns = declaration.Type.ContainingNamespace.IsGlobalNamespace ? "" : declaration.Type.ContainingNamespace.ToDisplayString();
                    pattern = "";
                }
                if (claimed && owner is null)
                    continue;
                if (Outside(relative))
                    throw new ArgumentException($"'{file.Path}' is outside its SQL root. Set Root explicitly for external files.");
                if (!Matches(relative, pattern))
                    continue;
                if (owner is not null && (owner.Type.ContainingType is not null || !ValidDeclaration(owner.Type)))
                    throw new ArgumentException("QueryCommands requires a top-level, non-generic static partial class: " + owner.Type.Name);
                string[] folders = relative.Split('/');
                string member = Path.GetFileNameWithoutExtension(folders[folders.Length - 1]);
                var types = new List<string> { container };
                types.AddRange(folders.Take(folders.Length - 1));
                if (types[types.Count - 1] == member || types.Where((name, index) => index > 0 && name == types[index - 1]).Any())
                    throw new ArgumentException("SQL members and nested classes cannot have the same name as an enclosing class: " + file.Path);
                foreach (string name in types.Concat(new[] { member }))
                    if (!Identifier(name))
                        throw new ArgumentException($"'{name}' is not a C# identifier for '{file.Path}'. Rename it or configure its SQL root/class.");
                if (ns.Length != 0 && ns.Split('.').Any(static name => !Identifier(name)))
                    throw new ArgumentException("Invalid generated namespace: " + ns);
                string mode = Setting(metadata, options.GlobalOptions, "Content", "Inline");
                if (!mode.Equals("Inline", StringComparison.OrdinalIgnoreCase) && !mode.Equals("File", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Content must be Inline or File: " + file.Path);
                string runtime = Get(metadata, "build_metadata.AdditionalFiles.RuntimePath", Get(metadata, "build_metadata.AdditionalFiles.Link", Relative(project, full))).Replace('\\', '/');
                if (mode.Equals("File", StringComparison.OrdinalIgnoreCase) && (Path.IsPathRooted(runtime) || Outside(runtime) || runtime.Split('/').Contains("..")))
                    throw new ArgumentException("File content requires a relative deployed RuntimePath or Link inside the application directory: " + file.Path);
                string prefix = Setting(metadata, options.GlobalOptions, "KeyPrefix", "").Replace('\\', '/').Trim('/');
                string key = (prefix.Length == 0 ? "" : prefix + "/") + relative;
                SourceText? text = file.GetText(output.CancellationToken);
                if (text is null)
                    throw new ArgumentException("Cannot read SQL source: " + file.Path);
                commands.Add(new Command(file.Path, ns, types.ToArray(), member, key, mode.Equals("File", StringComparison.OrdinalIgnoreCase), runtime, text.ToString()));
            }
            catch (Exception error) when (error is ArgumentException || error is IOException || error is RegexMatchTimeoutException || error is NotSupportedException) {
                output.ReportDiagnostic(Diagnostic.Create(Invalid, Location.None, error.Message));
            }
        }
        var members = new Dictionary<string, Command>(StringComparer.Ordinal);
        var keys = new Dictionary<string, Command>(StringComparer.Ordinal);
        var containers = new HashSet<string>(StringComparer.Ordinal);
        bool invalid = false;
        foreach (var command in commands) {
            string type = command.Namespace;
            foreach (string name in command.Types) {
                type += "." + name;
                containers.Add(type);
            }
            string qualified = type + "." + command.Member;
            if (members.ContainsKey(qualified) || keys.ContainsKey(command.Key)) {
                output.ReportDiagnostic(Diagnostic.Create(Collision, Location.None, "Duplicate SQL member or registry key: " + command.Path));
                invalid = true;
            }
            else {
                members.Add(qualified, command);
                keys.Add(command.Key, command);
            }
            INamedTypeSymbol? existing = compilation.GetTypeByMetadataName((command.Namespace.Length == 0 ? "" : command.Namespace + ".") + string.Join("+", command.Types));
            if (existing is not null && existing.GetMembers(command.Member).Length != 0) {
                output.ReportDiagnostic(Diagnostic.Create(Collision, Location.None, "SQL member conflicts with an existing declaration: " + qualified));
                invalid = true;
            }
            for (int index = 0; index < command.Types.Length; index++) {
                string name = (command.Namespace.Length == 0 ? "" : command.Namespace + ".") + string.Join("+", command.Types.Take(index + 1));
                var declared = compilation.GetTypeByMetadataName(name);
                if (declared is not null && !ValidDeclaration(declared)) {
                    output.ReportDiagnostic(Diagnostic.Create(Invalid, Location.None, "Generated SQL containers must be non-generic static partial classes: " + name));
                    invalid = true;
                }
            }
        }
        foreach (var pair in members)
            if (containers.Contains(pair.Key)) {
                output.ReportDiagnostic(Diagnostic.Create(Collision, Location.None, "SQL member conflicts with a folder/container: " + pair.Key));
                invalid = true;
            }
        if (invalid || commands.Count == 0)
            return;
        var code = new StringBuilder("// <auto-generated/>\n");
        for (int index = 0; index < commands.Count; index++) {
            var command = commands[index];
            if (command.Namespace.Length != 0)
                code.Append("namespace ").Append(EscapeNamespace(command.Namespace)).Append(" {\n");
            for (int depth = 0; depth < command.Types.Length; depth++) {
                string typeName = (command.Namespace.Length == 0 ? "" : command.Namespace + ".") + string.Join("+", command.Types.Take(depth + 1));
                var type = compilation.GetTypeByMetadataName(typeName);
                string access = type?.DeclaredAccessibility == Accessibility.Internal ? "internal" : "public";
                code.Append(access).Append(" static partial class ").Append(Escape(command.Types[depth])).Append(" {\n");
            }
            code.Append("public static readonly global::Rinku.QueryCommand ").Append(Escape(command.Member)).Append(" = new global::Rinku.QueryCommand(");
            if (command.File)
                code.Append("global::System.IO.File.ReadAllText(global::System.IO.Path.Combine(global::System.AppContext.BaseDirectory, ").Append(Literal(command.Runtime)).Append("))");
            else
                code.Append(Literal(command.Sql));
            code.Append(");\n");
            foreach (string unused in command.Types)
                code.Append("}\n");
            if (command.Namespace.Length != 0)
                code.Append("}\n");
        }
        string bootstrap = "__RinkuSql_" + StableHash(compilation.AssemblyName ?? "Assembly");
        code.Append("internal static class ").Append(bootstrap).Append(" {\n[global::System.Runtime.CompilerServices.ModuleInitializer]\ninternal static void Initialize() {\n");
        foreach (var command in commands) {
            string member = "global::" + (command.Namespace.Length == 0 ? "" : EscapeNamespace(command.Namespace) + ".") + string.Join(".", command.Types.Select(Escape)) + "." + Escape(command.Member);
            code.Append("if (!global::Rinku.ConnectionQueryExtensions.CommandCache.TryAdd(").Append(Literal(command.Key)).Append(", ").Append(member).Append(")) throw new global::System.InvalidOperationException(").Append(Literal("A Rinku SQL command is already registered as '" + command.Key + "'. Configure a distinct KeyPrefix.")).Append(");\n");
        }
        code.Append("}\n}\n");
        output.AddSource("RinkuSql.g.cs", SourceText.From(code.ToString(), Encoding.UTF8));
    }

    internal static string Get(AnalyzerConfigOptions options, string key, string fallback) => options.TryGetValue(key, out string? value) && value.Length != 0 ? value : fallback;
    private static string Setting(AnalyzerConfigOptions local, AnalyzerConfigOptions global, string name, string fallback) => Get(local, "build_metadata.AdditionalFiles." + name, Get(global, "build_property.RinkuSql" + name, fallback));
    internal static string Resolve(string project, string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(project, path));
    internal static string Relative(string root, string path) => Uri.UnescapeDataString(new Uri(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar).MakeRelativeUri(new Uri(Path.GetFullPath(path))).ToString()).Replace('\\', '/');
    private static bool SamePath(string left, string right) => string.Equals(left, right, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool Outside(string relative) => relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Uri.TryCreate(relative, UriKind.Absolute, out _);
    private static bool Matches(string relative, string pattern) => pattern.Length == 0 || Regex.IsMatch(relative, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static bool Identifier(string name) => SyntaxFacts.IsValidIdentifier(name) || SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None;
    private static string Escape(string name) => "@" + name;
    private static string EscapeNamespace(string ns) => string.Join(".", ns.Split('.').Select(Escape));
    private static string Literal(string text) => SymbolDisplay.FormatLiteral(text, true);
    private static bool ValidDeclaration(INamedTypeSymbol type) => type.TypeKind == TypeKind.Class && type.IsStatic && type.Arity == 0 && type.DeclaringSyntaxReferences.All(static reference => reference.GetSyntax() is ClassDeclarationSyntax syntax && syntax.Modifiers.Any(SyntaxKind.PartialKeyword));
    private static string StableHash(string text) {
        uint hash = 2166136261;
        foreach (char character in text)
            hash = unchecked((hash ^ character) * 16777619);
        return hash.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
    }
    private sealed class Declaration(INamedTypeSymbol type, AttributeData attribute) {
        internal INamedTypeSymbol Type { get; } = type;
        internal string Path { get; } = attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as string ?? "" : "";
        internal string Pattern { get; } = attribute.NamedArguments.FirstOrDefault(static pair => pair.Key == "Pattern").Value.Value as string ?? "";
        internal bool Recursive { get; } = attribute.NamedArguments.FirstOrDefault(static pair => pair.Key == "Recursive").Value.Value is true;
    }
    private sealed class Command(string path, string ns, string[] types, string member, string key, bool file, string runtime, string sql) {
        internal string Path { get; } = path;
        internal string Namespace { get; } = ns;
        internal string[] Types { get; } = types;
        internal string Member { get; } = member;
        internal string Key { get; } = key;
        internal bool File { get; } = file;
        internal string Runtime { get; } = runtime;
        internal string Sql { get; } = sql;
    }
}
