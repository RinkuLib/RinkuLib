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

        var code = File.ReadAllText(fullPathFile);
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetCompilationUnitRoot();
        var resultSet = root.DescendantNodes().OfType<RecordDeclarationSyntax>().FirstOrDefault(record => record.Identifier.ValueText == targetResultSetName)
            ?? throw new InvalidOperationException($"Result set '{targetResultSetName}' was not found in '{fullPathFile}'. Refresh the linked SQL command before generating its model.");

        sb.AppendLine("using Rinku.Mapping;");
        if (root.Members.OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault() is { } sourceNamespace) {
            sb.AppendLine($"using {sourceNamespace.Name};");
            sb.AppendLine();
        }
        sb.AppendLine($"namespace {nameSpace};");
        sb.AppendLine();

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'");
        sb.AppendLine($"[BasedOn(typeof({targetResultSetName}), lastUpdated: \"{timestamp}\")]");
        sb.Append($"public class {className}");
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
            sb.Append(type).Append(' ').Append(ParameterName(parameter)).Append(", ");
        }

        sb.Length -= 2;
        sb.AppendLine(") {");

        foreach (var parameter in parameters) {
            var type = parameter.Type?.ToString().Trim() ?? "object?";
            var name = parameter.Identifier.Text;

            sb.Append("    public ").Append(type).Append(' ').Append(name).Append(" { get; set; } = ").Append(ParameterName(parameter)).AppendLine(";");
        }
        sb.AppendLine("}");
    }
    private static string ParameterName(ParameterSyntax parameter) {
        string name = parameter.Identifier.ValueText;
        if (name.Length > 0) name = char.ToLowerInvariant(name[0]) + name[1..];
        return SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;
    }
}
