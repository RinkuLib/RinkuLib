using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.ProjectSystem.Query;
using Microsoft.VisualStudio.RpcContracts.Notifications;
using Newtonsoft.Json.Linq;
using RinkuLib.Commands;

namespace RinkuPowerTools; 
/// <summary>
/// Command1 handler.
/// </summary>
[VisualStudioContribution]
internal class AddClassFromProcCommand : Command {
    private readonly TraceSource logger;
    public const string ConfigFileName = "rinkuOptions.json";
    public readonly JsonSerializerOptions PrettyIndent = new() { WriteIndented = true };

    /// <summary>
    /// Initializes a new instance of the <see cref="AddClassFromProcCommand"/> class.
    /// </summary>
    /// <param name="traceSource">Trace source instance to utilize.</param>
    public AddClassFromProcCommand(TraceSource traceSource) {
        // This optional TraceSource can be used for logging in the command. You can use dependency injection to access
        // other services here as well.
        this.logger = Requires.NotNull(traceSource, nameof(traceSource));
    }

    /// <inheritdoc />
    public override CommandConfiguration CommandConfiguration => new("%RinkuPowerTools.Command1.DisplayName%") {
        Icon = new(ImageMoniker.KnownValues.Extension, IconSettings.IconAndText),
        Placements = [
            CommandPlacement.VsctParent(new Guid("{d309f791-903f-11d0-9efc-00a0c911004f}"), id: 0x0203, priority: 0x1000)
        ]
    };

    /// <inheritdoc />
    public override Task InitializeAsync(CancellationToken cancellationToken) {
        // Use InitializeAsync for any one-time setup or initialization.
        return base.InitializeAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken ct) {
        var options = await this.GetOrPromptOptionsAsync(context, ct);
        if (options is null)
            return;

        var resolutionResult = await ResolveNamespaceAndPathAsync(context, ct);
        if (resolutionResult is null)
            return;
        var (targetNamespace, targetDir) = resolutionResult.Value;

        var (databases, procedures, defaultDb) = await GetInitialSqlDataAsync(options.ConnectionString, ct);

        if (databases is null)
            return;

        var classData = new ProcLinkInfoControlData(defaultDb, databases, procedures);

        if (!await GetFileInfoAsync(options, classData, ct))
            return;

        await GenerateFileAsync(options, targetNamespace, targetDir, classData, ct);
        await this.Extensibility.Shell().ShowPromptAsync($"Created {classData.ClassName}.cs", PromptOptions.OK, ct);
    }

    private async Task<bool> GetFileInfoAsync(RinkuOption options, ProcLinkInfoControlData classData, CancellationToken ct) {
        while (true) {
            using var control = new ProcLinkInfoControl(this.Extensibility, classData, options.ConnectionString);
            var result = await this.Extensibility.Shell().ShowDialogAsync(control, "Generate Proc Link", DialogOption.OKCancel, ct);

            if (result != DialogResult.OK)
                return false;

            string? error = classData.GetValidationError();

            if (error is null)
                break;

            await this.Extensibility.Shell().ShowPromptAsync(error, PromptOptions.OK, ct);
        }

        return true;
    }
    private async Task<RinkuOption?> GetOrPromptOptionsAsync(IClientContext context, CancellationToken ct) {
        var project = await context.GetActiveProjectAsync(ct);
        if (project?.Path is null)
            return null;

        string projectDirectory = Path.GetDirectoryName(project.Path)!;
        string configPath = Path.Combine(projectDirectory, ConfigFileName);

        if (File.Exists(configPath)) {
            try {
                string json = await File.ReadAllTextAsync(configPath, ct);
                var savedOptions = JsonSerializer.Deserialize<RinkuOption>(json);
                if (savedOptions is not null)
                    return savedOptions;
            }
            catch (JsonException) {
                var choice = await this.Extensibility.Shell().ShowPromptAsync(
                    "The rinkuOptions.json file is corrupted. Would you like to overwrite it with new settings?",
                    PromptOptions.OKCancel,
                    ct);

                if (!choice)
                    return null;
            }
        }

        var dataContext = new RinkuOptionControlData();

        using var control = new RinkuOptionControl(dataContext);
        while (true) {
            var result = await this.Extensibility.Shell().ShowDialogAsync(control, "Database Configuration", DialogOption.OKCancel, ct);

            if (result != DialogResult.OK)
                return null;

            string? error = dataContext.GetValidationError();

            if (error is null)
                break;

            await this.Extensibility.Shell().ShowPromptAsync(error, PromptOptions.OK, ct);
        }

        var options = dataContext.ToOptions();
        string newJson = JsonSerializer.Serialize(options, PrettyIndent);
        await File.WriteAllTextAsync(configPath, newJson, ct);

        return options;
    }
    private async Task<(string Namespace, string Directory)?> ResolveNamespaceAndPathAsync(IClientContext context, CancellationToken ct) {
        var projectSnapshot = await context.GetActiveProjectAsync(ct);
        var selectedPathUri = await context.GetSelectedPathAsync(ct);

        if (projectSnapshot is null || selectedPathUri is null)
            return null;

        var projectResults = await this.Extensibility.Workspaces().QueryProjectsAsync(
            query => query
                .Where(p => p.Path == projectSnapshot.Path)
                .With(p => p.DefaultNamespace)
                .With(p => p.Name)
                .With(p => p.Path)
                .With(p => p.Folders
                    .With(f => f.Name)
                    .With(f => f.RelativePath)),
            ct);

        var project = projectResults.FirstOrDefault();
        if (project is null)
            return null;

        string rootNamespace = project.DefaultNamespace ?? project.Name;
        string projectDir = Path.GetDirectoryName(project.Path)!;
        string selectedPath = selectedPathUri.LocalPath;

        string targetDir = Directory.Exists(selectedPath) ? selectedPath : Path.GetDirectoryName(selectedPath)!;
        string targetRelative = Path.GetRelativePath(projectDir, targetDir).TrimEnd('\\', '/');

        if (targetRelative == "." || targetRelative.StartsWith("..")) {
            return (rootNamespace, targetDir);
        }

        var rootFolders = await project.Folders
                    .With(f => f.Name)
                    .With(f => f.RelativePath)
                    .ExecuteQueryAsync(ct);
        IFolderSnapshot? match = await FindFolderRecursiveAsync(rootFolders, targetRelative, ct);

        string finalLogicalPath = match?.RelativePath ?? targetRelative;
        string[] parts = finalLogicalPath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

        List<string> segments = [];
        foreach (var part in parts) {
            segments.Add(part.Replace(" ", "_"));
        }

        return ($"{rootNamespace}.{string.Join(".", segments)}", targetDir);
    }
    private static async Task<IFolderSnapshot?> FindFolderRecursiveAsync(IQueryResults<IFolderSnapshot> folders, string targetRelative, CancellationToken ct) {
        foreach (var folder in folders) {
            string currentFolderRelative = folder.RelativePath.TrimEnd('\\', '/');

            if (string.Equals(currentFolderRelative, targetRelative, StringComparison.OrdinalIgnoreCase)) {
                return folder;
            }

            if (targetRelative.StartsWith(currentFolderRelative + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                targetRelative.StartsWith(currentFolderRelative + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
                var subFolders = await folder.Folders
                    .With(f => f.Name)
                    .With(f => f.RelativePath)
                    .ExecuteQueryAsync(ct);

                return await FindFolderRecursiveAsync(subFolders, targetRelative, ct);
            }
        }

        return null;
    }
    private async Task<(List<string>? Databases, List<string> Procedures, string? DefaultDb)> GetInitialSqlDataAsync(string connectionString, CancellationToken ct) {
        var dbs = new List<string>();
        var procs = new List<string>();
        string? initialDb;

        try {
            var builder = new SqlConnectionStringBuilder(connectionString);
            initialDb = builder.InitialCatalog;

            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            var dbCmd = new SqlCommand("SELECT name FROM sys.databases WHERE state = 0 ORDER BY name", conn);
            using (var dbReader = await dbCmd.ExecuteReaderAsync(ct)) {
                while (await dbReader.ReadAsync(ct))
                    dbs.Add(dbReader.GetString(0));
            }

            if (!string.IsNullOrEmpty(initialDb) && dbs.Contains(initialDb)) {
                var procCmd = new SqlCommand("SELECT ROUTINE_NAME FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'PROCEDURE' ORDER BY ROUTINE_NAME", conn);
                using var procReader = await procCmd.ExecuteReaderAsync(ct);
                while (await procReader.ReadAsync(ct))
                    procs.Add(procReader.GetString(0));
            }

            return (dbs, procs, initialDb);
        }
        catch (Exception ex) {
            await this.Extensibility.Shell().ShowPromptAsync($"SQL Connection Error: {ex.Message}", PromptOptions.OK, ct);
            return (null, procs, null);
        }
    }

    private async Task GenerateFileAsync(RinkuOption options, string targetNamespace, string targetDir, ProcLinkInfoControlData classData, CancellationToken ct) {
        var metadata = await GetProcedureMetadataAsync(options.ConnectionString, classData.SelectedDatabase!, classData.SelectedProcedure!);
        string className = classData.ClassName;
        string procName = classData.SelectedProcedure!;

        var propsBuilder = new StringBuilder();
        foreach (var col in metadata.Columns)
            propsBuilder.AppendLine($"    public {col.CSharpTypeName} {col.Name} {{ get; set; }}");

        var argsBuilder = new StringBuilder("SqlConnection connection");
        var paramAssignments = new StringBuilder();

        foreach (var p in metadata.Parameters) {
            argsBuilder.Append(", ").Append(p.CSharpTypeName).Append(' ').Append(p.Name);
            var sizeValue = p.GetParameterSizeString();
            paramAssignments.AppendLine($@"        cmd.Parameters.Add($""@{{nameof({p.Name})}}"", SqlDbType.{p.DataType}{(sizeValue is not null ? $", {sizeValue}" : string.Empty)}).Value = (object?){p.Name} ?? DBNull.Value;");
        }
        string classContent = $@"using System.Data;
using Microsoft.Data.SqlClient;
using RinkuLib.Commands;

namespace {targetNamespace};

public class {className}
{{
{propsBuilder}
    public static StoredProcParser<{className}> {procName} {{ get; }} = new(""{procName}"");

    public static {className}? Query({argsBuilder})
    {{
        using var cmd = connection.CreateCommand();
{paramAssignments}
        return {procName}.Query(cmd, true);
    }}
}}";

        await File.WriteAllTextAsync(Path.Combine(targetDir, $"{className}.cs"), classContent, ct);
    }
    private async Task<StoredProcMetadata> GetProcedureMetadataAsync(string connectionString, string db, string procName) {
        var columns = new List<SqlColumnInfo>();
        var parameters = new List<SqlColumnInfo>();
        var builder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = db };

        using var conn = new SqlConnection(builder.ConnectionString);
        await conn.OpenAsync();

        var colSql = "SELECT name, system_type_name, is_nullable, max_length FROM sys.dm_exec_describe_first_result_set(@proc, NULL, 0)";
        using (var cmd = new SqlCommand(colSql, conn)) {
            cmd.Parameters.Add("@proc", SqlDbType.NVarChar, 255).Value = procName;
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                columns.Add(new SqlColumnInfo(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.GetInt16(3)));
        }

        var paramSql = "SELECT name, TYPE_NAME(user_type_id), is_nullable, max_length FROM sys.parameters WHERE object_id = OBJECT_ID(@proc)";
        using (var cmd = new SqlCommand(paramSql, conn)) {
            cmd.Parameters.Add("@proc", SqlDbType.NVarChar, 255).Value = procName;
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                parameters.Add(new SqlColumnInfo(reader.GetString(0).TrimStart('@'), reader.GetString(1), reader.GetBoolean(2), reader.GetInt16(3)));
        }

        return new StoredProcMetadata(columns, parameters);
    }
}
public record StoredProcMetadata(List<SqlColumnInfo> Columns, List<SqlColumnInfo> Parameters);
