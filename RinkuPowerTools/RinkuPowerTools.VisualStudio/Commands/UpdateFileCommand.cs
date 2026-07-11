using System.Text.Json;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace RinkuPowerTools.VisualStudio.Commands;

[VisualStudioContribution]
public class UpdateFileCommand : Command {
    public static readonly JsonSerializerOptions Deserialize = new() { PropertyNameCaseInsensitive = true };

    /// <inheritdoc />
    public override CommandConfiguration CommandConfiguration => new("%RinkuPowerTools.VisualStudio.Configure.DisplayName%") {
        Icon = new(ImageMoniker.KnownValues.DatabaseColumn, IconSettings.IconAndText),
        Placements = [
            CommandPlacement.VsctParent(new Guid("{d309f791-903f-11d0-9efc-00a0c911004f}"), id: 0x209, priority: 0x0100)
        ],
        VisibleWhen = ActivationConstraint.ClientContext(
            ClientContextKey.Shell.ActiveSelectionFileName, ".*rinkupt(\\.[^.]+)?\\.json$")
    };

    /// <inheritdoc />
    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken ct) {
        var (projectSnapshot, projectDirectory) = await context.GetProjectAndDirectoryAsync(ct);
        if (projectSnapshot is null || projectDirectory is null) {
            await this.Extensibility.ShowPromptAsync("No active project found. Please select a project or a file inside a project.", ct);
            return;
        }

        var fileName = (await context.GetSelectedPathAsync(ct)).GetFileNameWithTrueCasing();
        if (!fileName.TryExtractClassName(out var className))
            return;

        var settings = await SettingsProvider.ConfigureSettingsAsync(this.Extensibility, projectDirectory, className, ct);
        if (settings is null)
            return;
        await this.Extensibility.GenerateAndOpenCommandsFileAsync(projectSnapshot, settings, ct);
    }
}