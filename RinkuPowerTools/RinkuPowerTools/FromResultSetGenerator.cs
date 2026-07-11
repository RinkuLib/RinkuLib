using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RinkuPowerTools;

public static class FromResultSetGenerator {
    public static async Task<string> GenerateClassAsync(string? baseNamespace, string currentPath, string className, ExtensionSettings settings, QuerySetting query, CancellationToken ct) {
        var targetDirectory = Directory.Exists(currentPath)
            ? currentPath
            : Path.GetDirectoryName(currentPath)
              ?? throw new InvalidOperationException("Unable to determine target directory.");

        if (string.IsNullOrWhiteSpace(settings.ClassName))
            settings.ClassName = "DbCommands";
        string fullPathFile = Path.Combine(settings.GetFullPath(settings.OutputPath), $"{settings.ClassName}.rinku.cs");
        var fullPath = Path.Combine(targetDirectory, $"{className}.cs");

        if (File.Exists(fullPath))
            throw new InvalidOperationException($"The file '{className}.cs' already exists.");

        var sb = new StringBuilder();
        var nameSpace = MainClassGenerator.DeduceNamespaceFromPath(baseNamespace ?? "Rinku", settings.GetRelativePath(targetDirectory));
        var targetResultSetName = query.ResultSetName ?? query.MethodName + "Result";
        GenerateContent(sb, nameSpace, className, fullPathFile, targetResultSetName);

        await File.WriteAllTextAsync(fullPath, sb.ToString(), ct);

        return fullPath;
    }

    private static void GenerateContent(StringBuilder sb, string nameSpace, string className, string fullPathFile, string targetResultSetName) {

        if (!File.Exists(fullPathFile))
            return;

        var code = File.ReadAllText(fullPathFile);
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetCompilationUnitRoot();

        if (TryExtractNamespace(root, out var namespaceName)) {
            sb.AppendLine($"using {namespaceName};");
            sb.AppendLine();
        }
        sb.AppendLine($"namespace {nameSpace};");
        sb.AppendLine();

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm");
        sb.AppendLine($"/// <BasedOn LastUpdated=\"{timestamp}\" cref=\"{targetResultSetName}\" />");
        sb.Append($"public class {className}");
        var resultSet = FindResultSet(root, targetResultSetName);
        if (resultSet is not null)
            HandleResultSet(sb, resultSet);
    }
    private static void HandleResultSet(StringBuilder sb, RecordDeclarationSyntax resultSet) {
        if (resultSet.ParameterList is null || resultSet.ParameterList.Parameters.Count == 0) {
            sb.AppendLine(" {");
            sb.AppendLine("}");
            return;
        }
        var parameters = resultSet.ParameterList.Parameters;
        sb.Append('(');
        foreach (var parameter in parameters) {
            var type = parameter.Type?.ToString().Trim() ?? "object?";
            var name = parameter.Identifier.Text;
            sb.Append(type).Append(' ');
            if (name.Length > 0 && char.IsUpper(name[0]))
                sb.Append(char.ToLowerInvariant(name[0])).Append(name.AsSpan(1));
            else
                sb.Append(name);
            sb.Append(", ");
        }

        sb.Length -= 2;
        sb.AppendLine(") {");

        foreach (var parameter in parameters) {
            var type = parameter.Type?.ToString().Trim() ?? "object?";
            var name = parameter.Identifier.Text;

            sb.Append("    public ").Append(type).Append(' ').Append(name).Append(" { get; set; } = ");

            if (name.Length > 0 && char.IsUpper(name[0]))
                sb.Append(char.ToLowerInvariant(name[0])).Append(name.AsSpan(1)).AppendLine(";");
            else
                sb.Append(name).AppendLine(";");
        }
        sb.AppendLine("}");
    }
    private static bool TryExtractNamespace(CompilationUnitSyntax root, [MaybeNullWhen(false)] out string nameSpace) {
        foreach (var node in root.Members) {
            if (node is NamespaceDeclarationSyntax ns) {
                nameSpace = ns.Name.ToString();
                return !string.IsNullOrWhiteSpace(nameSpace);
            }
            if (node is FileScopedNamespaceDeclarationSyntax fns) {
                nameSpace = fns.Name.ToString();
                return !string.IsNullOrWhiteSpace(nameSpace);
            }
        }
        nameSpace = null;
        return false;
    }
    private static RecordDeclarationSyntax? FindResultSet(CompilationUnitSyntax root, string targetResultSetName) {
        foreach (var member in root.Members) {
            if (member is BaseNamespaceDeclarationSyntax ns) {
                foreach (var inner in ns.Members) {
                    if (inner is RecordDeclarationSyntax rec &&
                        rec.Identifier.Text == targetResultSetName) {
                        return rec;
                    }
                }
            }
            if (member is RecordDeclarationSyntax globalRec &&
                globalRec.Identifier.Text == targetResultSetName) {
                return globalRec;
            }
        }
        return null;
    }
}