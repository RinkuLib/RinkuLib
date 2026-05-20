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

namespace RinkuPowerTools;

[VisualStudioContribution]
public class SyncDBCommand(TraceSource traceSource) : Command {
    public const string ConfigFileName = "rptOptions.json";

    public static readonly JsonSerializerOptions PrettyIndent = new() { WriteIndented = true, PropertyNameCaseInsensitive = false };
    public static readonly JsonSerializerOptions Deserialize = new() { PropertyNameCaseInsensitive = true };
    private readonly TraceSource logger = Requires.NotNull(traceSource, nameof(traceSource));

    /// <inheritdoc />
    public override CommandConfiguration CommandConfiguration => new("%RinkuPowerTools.SyncDBCommand.DisplayName%") {
        Icon = new(ImageMoniker.KnownValues.DatabaseColumn, IconSettings.IconAndText),
        Placements = [
            CommandPlacement.VsctParent(new Guid("{d309f791-903f-11d0-9efc-00a0c911004f}"), id: 518, priority: 0x0100)
        ],
    };

    /// <inheritdoc />
    public override Task InitializeAsync(CancellationToken cancellationToken) 
        => base.InitializeAsync(cancellationToken);
    /// <inheritdoc />
    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken ct)
        => ExecuteCommandAsync(this.Extensibility, context, true, ct);
    public static async Task<ExtensionSettings?> CompleteGetSettingsAsync(VisualStudioExtensibility extensibility, string projectDirectory, bool showDialogs, CancellationToken ct) {
        if (string.IsNullOrEmpty(projectDirectory)) {
            await extensibility.Shell().ShowPromptAsync("Could not resolve the project path directory.", PromptOptions.OK, ct);
            return null;
        }

        string configFilePath = Path.Combine(projectDirectory, ConfigFileName);
        ExtensionSettings settings = await GetSettingsAsync(configFilePath, ct);
        if (showDialogs || settings.ConnectionString is null) {
            if (!await ShowBaseConfigurationDialogAsync(extensibility, settings, projectDirectory, ct))
                return null;

            if (!await ShowQueryManagerDialogAsync(extensibility, settings, projectDirectory, ct))
                return null;
        }
        return settings;
    }

    public static async Task ExecuteCommandAsync(VisualStudioExtensibility extensibility, IClientContext context, bool showDialogs, CancellationToken ct) {
        var projectSnapshot = await context.GetActiveProjectAsync(ct);
        if (projectSnapshot is null) {
            await extensibility.Shell().ShowPromptAsync("No active project found. Please select a project or a file inside a project.", PromptOptions.OK, ct);
            return;
        }
        var projectDirectory = Path.GetDirectoryName(projectSnapshot.Path);
        if (projectDirectory is null)
            return;
        var settings = await CompleteGetSettingsAsync(extensibility, projectDirectory, showDialogs, ct);
        if (settings is null)
            return;
        string configFilePath = Path.Combine(projectDirectory, ConfigFileName);

        bool saveSuccess = await SaveSettingsAsync(extensibility, settings, configFilePath, ct);
        if (!saveSuccess)
            return;
        await GenerateCommandsFileAsync(extensibility, projectSnapshot, projectDirectory, settings, ct);
    }

    public static async Task<bool> GenerateCommandsFileAsync(VisualStudioExtensibility extensibility, IProjectSnapshot projectSnapshot, string projectDirectory, ExtensionSettings settings, CancellationToken ct) {
        string absoluteTargetDirectory = Path.GetFullPath(Path.Combine(projectDirectory, settings.OutputPath));
        if (!absoluteTargetDirectory.StartsWith(projectDirectory, StringComparison.OrdinalIgnoreCase)) {
            await extensibility.Shell().ShowPromptAsync(
                "Invalid output path. The target directory must reside inside the active project.",
                PromptOptions.OK,
                ct);
            return false;
        }
        if (string.IsNullOrWhiteSpace(settings.Namespace))
            settings.Namespace = await DeduceNamespaceFromPathAsync(extensibility, projectSnapshot, projectDirectory, absoluteTargetDirectory, ct);

        string absoluteOutputFilePath = Path.Combine(absoluteTargetDirectory, $"{settings.ClassName}.cs");
        string classContent = await CodeGenerator.GenerateClassAsync(settings, projectDirectory, ct);

        try {
            if (!Directory.Exists(absoluteTargetDirectory))
                Directory.CreateDirectory(absoluteTargetDirectory);

            await File.WriteAllTextAsync(absoluteOutputFilePath, classContent, Encoding.UTF8, ct);
            await extensibility.Documents().OpenDocumentAsync(new(absoluteOutputFilePath), ct);
            return true;
        }
        catch (Exception ex) {
            await extensibility.Shell().ShowPromptAsync($"Failed to write class file to disk: {ex.Message}", PromptOptions.OK, ct);
            return false;
        }
    }

    private static async Task<bool> TestConnectionAsync(VisualStudioExtensibility extensibility, ExtensionSettings settings, CancellationToken ct) {
        try {
            using var cnn = settings.GetConnection();
            await cnn.OpenAsync(ct);

            using var cmd = cnn.CreateCommand();
            cmd.CommandText = "SELECT 1;";
            cmd.CommandType = System.Data.CommandType.Text;

            await cmd.ExecuteScalarAsync(ct);
        }
        catch (Exception ex) {
            string message = $"Could not connect to the database with the provided settings.\n\n" +
                             $"Error: {ex.Message}\n\n" +
                             $"Please check your Connection String and ensure the database server is accessible before continuing.";

            await extensibility.Shell().ShowPromptAsync(message, PromptOptions.OK, ct);

            return false;
        }

        return true;
    }

    public static async Task<string> DeduceNamespaceFromPathAsync(VisualStudioExtensibility extensibility, IProjectSnapshot projectSnapshot, string projectRootDirectory, string targetAbsoluteDirectory, CancellationToken ct) {

        var projectResults = await extensibility.Workspaces().QueryProjectsAsync(
            query => query
                .Where(p => p.Path == projectSnapshot.Path)
                .With(p => p.DefaultNamespace)
                .With(p => p.Name),
            ct);

        var project = projectResults.FirstOrDefault();
        string baseNamespace = project?.DefaultNamespace ?? project?.Name ?? "RinkuPowerTools";

        string relativePath = Path.GetRelativePath(projectRootDirectory, targetAbsoluteDirectory);

        if (relativePath == ".")
            return baseNamespace;

        string[] pathFolders = relativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        List<string> cleanSegments = [];

        foreach (var folder in pathFolders)
            cleanSegments.Add(folder.Replace(" ", "_"));

        return $"{baseNamespace}.{string.Join(".", cleanSegments)}";
    }
    /// <summary>
    /// Displays the dialog responsible for setting connection details, class names, and directories.
    /// </summary>
    private static async Task<bool> ShowBaseConfigurationDialogAsync(VisualStudioExtensibility extensibility, ExtensionSettings settings, string projectDirectory, CancellationToken ct) {
        while (true) {
            var uiContext = new SyncDBOptionsControlData(extensibility, projectDirectory, settings);
            uiContext.TriggerValidate();
            var control = new SyncDBOptionsControl(uiContext);
            var userChoice = await extensibility.Shell().ShowDialogAsync(
                control, "Rinku Power Tools - Database Configuration", DialogOption.OKCancel, ct);

            if (userChoice != DialogResult.OK)
                return false;

            await Task.Delay(50, ct);

            if (!uiContext.IsValid)
                await extensibility.Shell().ShowPromptAsync("Please fix the validation errors before saving.", PromptOptions.OK, ct);
            else if (await TestConnectionAsync(extensibility, settings, ct))
                break;
        }
        return true;
    }

    /// <summary>
    /// Displays the Query Manager dialog allowing users to add, filter, and alter text/proc mappings.
    /// </summary>
    private static async Task<bool> ShowQueryManagerDialogAsync(VisualStudioExtensibility extensibility, ExtensionSettings settings, string projectDirectory, CancellationToken ct) {
        settings.Queries ??= [];

        var queryUiContext = new QueryManagerControlData(extensibility, projectDirectory, settings);
        var queryControl = new QueryManagerControl(queryUiContext);

        var userChoice = await extensibility.Shell().ShowDialogAsync(
            queryControl, "Rinku Power Tools - Manage Database Queries", DialogOption.OKCancel, ct);

        if (userChoice != DialogResult.OK)
            return false;
        return true;
    }

    /// <summary>
    /// Serializes unified configuration elements safely back to disk.
    /// </summary>
    private static async Task<bool> SaveSettingsAsync(VisualStudioExtensibility extensibility, ExtensionSettings settings, string configFilePath, CancellationToken ct) {
        try {
            string targetJsonOutput = JsonSerializer.Serialize(settings, PrettyIndent);
            await File.WriteAllTextAsync(configFilePath, targetJsonOutput, ct);
            return true;
        }
        catch (Exception ex) {
            await extensibility.Shell().ShowPromptAsync($"Failed to save rptOptions.json: {ex.Message}", PromptOptions.OK, ct);
            return false;
        }
    }

    private static async Task<ExtensionSettings> GetSettingsAsync(string configFilePath, CancellationToken ct) {
        if (File.Exists(configFilePath)) {
            try {
                string jsonText = await File.ReadAllTextAsync(configFilePath, ct);
                var r = JsonSerializer.Deserialize<ExtensionSettings>(jsonText, Deserialize);
                if (r is not null)
                    return r;
            }
            catch { }
        }
        return new() { ConnectionString = string.Empty };
    }
}