using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.RpcContracts.Notifications;

namespace RinkuPowerTools;

/// <summary>
/// Command handler that reuses SyncDBCommand's static layer to load configurations
/// and discover target paths based on the user's right-click context.
/// </summary>
[VisualStudioContribution]
public class AddClassQueryInSettingsCommand : Command {
    /// <inheritdoc />
    public override CommandConfiguration CommandConfiguration => new("%RinkuPowerTools.Command1.DisplayName%") {
        Icon = new(ImageMoniker.KnownValues.Extension, IconSettings.IconAndText),
        Placements = [
            CommandPlacement.VsctParent(new Guid("{d309f791-903f-11d0-9efc-00a0c911004f}"), id: 0x0203, priority: 0x1000)
        ]
    };

    /// <inheritdoc />
    public override Task InitializeAsync(CancellationToken cancellationToken) {
        return base.InitializeAsync(cancellationToken);
    }
    /// <inheritdoc />
    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken ct) {
        var projectSnapshot = await context.GetActiveProjectAsync(ct);
        if (projectSnapshot is null) {
            await this.Extensibility.Shell().ShowPromptAsync("No active project found.", PromptOptions.OK, ct);
            return;
        }

        var projectDirectory = Path.GetDirectoryName(projectSnapshot.Path);
        if (projectDirectory is null)
            return;

        var settings = await SyncDBCommand.CompleteGetSettingsAsync(this.Extensibility, projectDirectory, showDialogs: false, ct);
        if (settings is null)
            return;

        var targetDir = await ResolveTargetDirectoryAsync(context, ct);
        if (targetDir is null) {
            await this.Extensibility.Shell().ShowPromptAsync("Could not determine selected path repository frame.", PromptOptions.OK, ct);
            return;
        }
        string targetNamespace = await SyncDBCommand.DeduceNamespaceFromPathAsync(
            this.Extensibility, projectSnapshot, projectDirectory, targetDir, ct);

        var dialogResult = await PromptForQuerySelectionAsync(settings, ct);
        if (dialogResult is null)
            return;

        var (className, queryMethodName) = dialogResult.Value;

        var selectedQuery = settings.Queries.FirstOrDefault(q =>
            string.Equals(q.MethodName, queryMethodName, StringComparison.OrdinalIgnoreCase));

        if (selectedQuery is null) {
            await this.Extensibility.Shell().ShowPromptAsync($"Could not locate configuration details for query '{queryMethodName}'.", PromptOptions.OK, ct);
            return;
        }
        string fullFilePath = Path.GetFullPath(Path.Combine(projectDirectory, settings.OutputPath, $"{settings.ClassName}.cs"));

        if (!File.Exists(fullFilePath) && (!await SyncDBCommand.GenerateCommandsFileAsync(this.Extensibility, projectSnapshot, projectDirectory, settings, ct) || !File.Exists(fullFilePath)))
            return;


        var root = await CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(fullFilePath, ct), cancellationToken: ct).GetRootAsync(ct);
        var ns = root.GetNamespaceName();
        var parameters = root.FindMethod(selectedQuery.MethodName)?.ParameterList.Parameters
            .Skip(1).Select(p => new MethodParameterInfo {
                Name = p.Identifier.ValueText,
                Type = p.Type?.ToString() ?? "object"
            }).ToList();

        if (ns is null || parameters is null) {
            await this.Extensibility.Shell().ShowPromptAsync($"Could not locate configuration details for query '{queryMethodName}'.", PromptOptions.OK, ct);
            return;
        }
        var columns = await DiscoverQueryColumnsAsync(settings, selectedQuery, projectDirectory, parameters, ct);

        await GenerateClassFileAsync(ns, targetDir, targetNamespace, className, selectedQuery.MethodName, columns, parameters, ct);
    }

    /// <summary>
    /// Extracts the local file system target path from the user's current execution selection context.
    /// </summary>
    private static async Task<string?> ResolveTargetDirectoryAsync(IClientContext context, CancellationToken ct) {
        var selectedPathUri = await context.GetSelectedPathAsync(ct);
        if (selectedPathUri is null)
            return null;

        string selectedPath = selectedPathUri.LocalPath;
        return Directory.Exists(selectedPath) ? selectedPath : Path.GetDirectoryName(selectedPath)!;
    }

    /// <summary>
    /// Displays the QuerySelector dialog loop until inputs pass internal checks or user explicitly cancels.
    /// </summary>
    private async Task<(string ClassName, string QueryName)?> PromptForQuerySelectionAsync(ExtensionSettings settings, CancellationToken ct) {
        var dialogContext = new QuerySelectorControlData(settings);

        while (true) {
            using var control = new QuerySelectorControl(dialogContext);
            var result = await this.Extensibility.Shell().ShowDialogAsync(
                control, "Select Query Class Context", DialogOption.OKCancel, ct);

            if (result != DialogResult.OK)
                return null;

            dialogContext.TriggerValidate();

            if (dialogContext.IsValid) {
                return (dialogContext.TargetClassName, dialogContext.QueryInputText);
            }

            await this.Extensibility.Shell().ShowPromptAsync(
                "Please resolve input errors before submitting generation parameters.",
                PromptOptions.OK,
                ct);
        }
    }
    /// <summary>
    /// Queries the database to extract the column schema layout of the first result set,
    /// using parsed method parameters to generate a safe compilation frame.
    /// </summary>
    private static async Task<List<ParameterMetadata>> DiscoverQueryColumnsAsync(ExtensionSettings settings, QuerySetting query, string projectDirectory, List<MethodParameterInfo> methodParameters, CancellationToken ct) {
        var columns = new List<ParameterMetadata>();

        string? sqlPayload = query.Target;
        if (query.SourceType == QuerySourceType.FromFile) {
            string fullPath = Path.Combine(projectDirectory, query.Target);
            sqlPayload = File.Exists(fullPath) ? await File.ReadAllTextAsync(fullPath, ct) : null;
        }

        var declarations = new List<string>();
        foreach (var param in methodParameters) {
            string sqlParamName = param.Name.StartsWith('@') ? param.Name : $"@{param.Name}";
            string sqlType = ParameterMetadata.MapCSharpToSqlDeclaration(param.Type);

            declarations.Add($"{sqlParamName} {sqlType}");
        }
        string paramDeclarationBlock = string.Join(", ", declarations);

        using var conn = settings.GetConnection();
        await conn.OpenAsync(ct);

        string colSql = "SELECT name, system_type_name, is_nullable FROM sys.dm_exec_describe_first_result_set(@sql, @params, 0)";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = colSql;
        cmd.CommandType = CommandType.Text;

        var pSql = cmd.CreateParameter();
        pSql.ParameterName = "@sql";
        pSql.DbType = DbType.String;
        pSql.Value = query.SourceType == QuerySourceType.StoredProcedure
            ? $"EXEC {sqlPayload}"
            : sqlPayload;
        cmd.Parameters.Add(pSql);

        var pParams = cmd.CreateParameter();
        pParams.ParameterName = "@params";
        pParams.DbType = DbType.String;
        pParams.Value = query.SourceType == QuerySourceType.StoredProcedure || string.IsNullOrEmpty(paramDeclarationBlock)
            ? DBNull.Value
            : paramDeclarationBlock;
        cmd.Parameters.Add(pParams);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) {
            if (await reader.IsDBNullAsync(0, ct))
                continue;

            string columnName = reader.GetString(0);
            string sysTypeName = await reader.IsDBNullAsync(1, ct) ? "nvarchar(max)" : reader.GetString(1);
            bool isNullable = reader.GetBoolean(2);

            var metadata = new ParameterMetadata(
                dbName: columnName,
                dbType: DbType.Object,
                isNullable: isNullable,
                size: 0,
                direction: ParameterDirection.Input,
                precision: 0,
                scale: 0
            );

            metadata.UpdateFromSqlType(sysTypeName, isNullable);
            columns.Add(metadata);
        }

        return columns;
    }
    private async Task GenerateClassFileAsync(string commandsClassNamespace, string targetDir, string targetNamespace, string className, string methodName, List<ParameterMetadata> columns, List<MethodParameterInfo> methodParameters, CancellationToken ct) {
        string targetFilePath = Path.Combine(targetDir, $"{className}.cs");

        var propertiesBuilder = new StringBuilder();
        var constructorParams = new List<string>();
        var constructorBody = new StringBuilder();

        foreach (var col in columns) {
            propertiesBuilder.AppendLine($"    public {col.CSharpType} {col.CleanName} {{ get; set; }}");
            string paramName = char.ToLower(col.CleanName[0]) + col.CleanName.Substring(1);
            constructorParams.Add($"{col.CSharpType} {paramName}");
            constructorBody.AppendLine($"        {col.CleanName} = {paramName};");
        }

        var methodArguments = new List<string> { "DbConnection connection" };
        methodArguments.AddRange(methodParameters.Select(p => $"{p.Type} {p.Name}"));

        string usingsString = string.Join(Environment.NewLine, new List<string> {
            "using System;", "using System.Collections.Generic;", "using System.Data;",
            "using System.Data.Common;", "using RinkuLib.Commands;"
        });

        if (!string.Equals(targetNamespace, commandsClassNamespace, StringComparison.Ordinal))
            usingsString += $"{Environment.NewLine}using {commandsClassNamespace};";

        string classContent = $@" {usingsString}

namespace {targetNamespace};

public class {className} 
{{
    private static readonly SingleTypeCache<List<{className}>> _cache = new();

{propertiesBuilder}
    public {className}({string.Join(", ", constructorParams)}) 
    {{
{constructorBody}    }}

    public static List<{className}> {methodName}({string.Join(", ", methodArguments)}) => 
        _cache.Query(connection.{methodName}({string.Join(", ", methodParameters.Select(p => p.Name))}), true);
}}";

        try {
            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);
            await File.WriteAllTextAsync(targetFilePath, classContent, Encoding.UTF8, ct);
            await this.Extensibility.Documents().OpenDocumentAsync(new Uri(targetFilePath), ct);
        }
        catch (Exception ex) {
            await this.Extensibility.Shell().ShowPromptAsync($"Failed to write {className}.cs: {ex.Message}", PromptOptions.OK, ct);
        }
    }
}
public static class RoslynExtensions {
    public static string? GetNamespaceName(this SyntaxNode root) {
        return root.DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault()?.Name.ToString();
    }
    public static MethodDeclarationSyntax? FindMethod(this SyntaxNode root, string methodName) {
        return root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == methodName);
    }
}