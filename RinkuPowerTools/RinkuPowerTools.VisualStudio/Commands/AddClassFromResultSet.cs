using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.RpcContracts.Notifications;
using RinkuPowerTools.VisualStudio.Content.NewClass;

namespace RinkuPowerTools.VisualStudio.Commands;

[VisualStudioContribution]
public class AddClassFromResultSet() : Command {
    public override CommandConfiguration CommandConfiguration => new("%RinkuPowerTools.VisualStudio.AddClass.DisplayName%") {
        Icon = new(ImageMoniker.KnownValues.Add, IconSettings.IconAndText),
        Placements = [
            CommandPlacement.VsctParent(new Guid("{d309f791-903f-11d0-9efc-00a0c911004f}"), id: 0x0203, priority: 0x1000)
        ]
    };
    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken ct) {
        var currentPath = await context.GetSelectedPathAsync(ct);
        var (projectSnapshot, projectDirectory) = await context.GetProjectAndDirectoryAsync(ct);
        if (projectSnapshot is null || projectDirectory is null) {
            await this.Extensibility.ShowPromptAsync("No active project found. Please select a project or a file inside a project.", ct);
            return;
        }
        var promptData = new NewClassPromptData(projectDirectory);
        using (var control = new NewClassPrompt(promptData)) {
            var res = await this.Extensibility.Shell().ShowDialogAsync(control, "Manage queries", DialogOption.OKCancel, ct);
            if (res != DialogResult.OK)
                return;
        }
        var name = promptData.EditName;
        if (!name.IsValidCSharpName()) {
            await this.Extensibility.ShowPromptAsync("The provided name was invalid", ct);
            return;
        }
        var settings = promptData.SelectedSettings;
        var query = promptData.SelectedQuery;
        if (settings is null || query is null) {
            await this.Extensibility.ShowPromptAsync("No result set was selected", ct);
            return;
        }
        string? baseNamespace = null;
        if (string.IsNullOrWhiteSpace(settings.Namespace))
            baseNamespace = await this.Extensibility.GetBaseNamespaceAsync(projectSnapshot, ct);
        try {
            var fullPath = await FromResultSetGenerator.GenerateClassAsync(baseNamespace, currentPath.AbsolutePath, name, settings, query, ct);
            await this.Extensibility.Documents().OpenDocumentAsync(new Uri(fullPath), ct);
        }
        catch (Exception ex) {
            await this.Extensibility.ShowPromptAsync($"Failed to write class file to disk: {ex.Message}", ct);
        }
    }
}