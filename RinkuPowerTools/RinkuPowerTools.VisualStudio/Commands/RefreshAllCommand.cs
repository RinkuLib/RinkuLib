using System.IO;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace RinkuPowerTools.VisualStudio.Commands; 
[VisualStudioContribution]
public class RefreshAllCommand() : Command {
    public override CommandConfiguration CommandConfiguration => new("%RinkuPowerTools.VisualStudio.RefreshAll.DisplayName%") {
        Icon = new(ImageMoniker.KnownValues.DatabaseColumn, IconSettings.IconAndText),
        Placements = [
            CommandPlacement.VsctParent(new Guid("{d309f791-903f-11d0-9efc-00a0c911004f}"), id: 518, priority: 0x1000)
        ]
    };
    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken ct) {
        var (projectSnapshot, projectDirectory) = await context.GetProjectAndDirectoryAsync(ct);
        if (projectSnapshot is null || projectDirectory is null) {
            await this.Extensibility.ShowPromptAsync("No active project found. Please select a project or a file inside a project.", ct);
            return;
        }
        var files = SettingsProvider.GetConfigFiles(projectDirectory);

        using var enumerator = files.GetEnumerator();

        if (!enumerator.MoveNext())
            return;

        while (true) {
            var file = enumerator.Current;
            bool isLast = !enumerator.MoveNext();
            if (!Path.GetFileName(file).TryExtractClassName(out var className))
                return;
            var settings = await SettingsProvider.GetSettingsAsync(this.Extensibility, projectDirectory, className, ct);
            if (settings is null)
                return;
            await this.Extensibility.GenerateAndOpenCommandsFileAsync(projectSnapshot, settings, isLast, ct);
            if (isLast)
                break;
        }
    }
}