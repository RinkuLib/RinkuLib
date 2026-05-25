using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.ProjectSystem.Query;
using RinkuPowerTools.Core;

namespace RinkuPowerTools;

[VisualStudioContribution]
public class SyncDBNoDialogsCommand : Command {
    public static readonly JsonSerializerOptions Deserialize = new() { PropertyNameCaseInsensitive = true };
    /// <inheritdoc />
    public override CommandConfiguration CommandConfiguration => new("%RinkuPowerTools.SyncDBNoDialogsCommand.DisplayName%") {
        Icon = new(ImageMoniker.KnownValues.DatabaseColumn, IconSettings.IconAndText),
        Placements = [
            CommandPlacement.VsctParent(new Guid("{d309f791-903f-11d0-9efc-00a0c911004f}"), id: 0x209, priority: 0x0100)
        ],
        VisibleWhen = ActivationConstraint.ClientContext(
            ClientContextKey.Shell.ActiveSelectionFileName, ".*rinkupt(\\.[^.]+)?\\.json$")
    };

    /// <inheritdoc />
    public override Task InitializeAsync(CancellationToken cancellationToken)
        => base.InitializeAsync(cancellationToken);

    /// <inheritdoc />
    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken ct) {
        var fileName = (await context.GetSelectedPathAsync(ct)).GetFileNameWithTrueCasing();
        if (string.IsNullOrEmpty(fileName))
            return;
        var className = fileName.Split('.') switch {
            [_, var modifier, "json"] => modifier,
            _ => "DbCommands"
        };

        var projectSnapshot = await context.GetActiveProjectAsync(ct);
        if (projectSnapshot is null) {
            await this.Extensibility.Shell().ShowPromptAsync("No active project found. Please select a project or a file inside a project.", PromptOptions.OK, ct);
            return;
        }
        var projectDirectory = Path.GetDirectoryName(projectSnapshot.Path);
        if (projectDirectory is null)
            return;
        var settings = await SyncDBCommand.CompleteGetSettingsAsync(this.Extensibility, projectDirectory, false, ct, fileName);
        if (settings is null)
            return;
        await GenerateCommandsFileAsync(this.Extensibility, projectSnapshot, projectDirectory, className, settings, ct);
    }
    public static async Task<bool> GenerateCommandsFileAsync(VisualStudioExtensibility extensibility, IProjectSnapshot projectSnapshot, string projectDirectory, string className, ExtensionSettings settings, CancellationToken ct) {
        string? baseNamespace = null;
        if (string.IsNullOrWhiteSpace(settings.Namespace))
            baseNamespace = await GetBaseNamespaceAsync(extensibility, projectSnapshot, ct);

        try {
            var fullPath = await MainClassGenerator.GenerateClassAsync(settings, projectDirectory, className, baseNamespace, ct);
            await extensibility.Documents().OpenDocumentAsync(new(fullPath), ct);
            return true;
        }
        catch (Exception ex) {
            await extensibility.Shell().ShowPromptAsync($"Failed to write class file to disk: {ex.Message}", PromptOptions.OK, ct);
            return false;
        }
    }
    public static async Task<string> GetBaseNamespaceAsync(VisualStudioExtensibility extensibility, IProjectSnapshot projectSnapshot, CancellationToken ct) {

        var projectResults = await extensibility.Workspaces().QueryProjectsAsync(
            query => query
                .Where(p => p.Path == projectSnapshot.Path)
                .With(p => p.DefaultNamespace)
                .With(p => p.Name),
            ct);

        var project = projectResults.FirstOrDefault();
        return project?.DefaultNamespace ?? project?.Name ?? "Rinku";
    }
}