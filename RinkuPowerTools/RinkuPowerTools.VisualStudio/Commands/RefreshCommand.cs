using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace RinkuPowerTools.VisualStudio.Commands;

[VisualStudioContribution]
public class RefreshCommand : Command {
    /// <inheritdoc />
    public override CommandConfiguration CommandConfiguration => new("%RinkuPowerTools.VisualStudio.Refresh.DisplayName%") {
        Icon = new(ImageMoniker.Custom("Rinku"), IconSettings.IconAndText),
        Placements = [
            CommandPlacement.VsctParent(new Guid("{d309f791-903f-11d0-9efc-00a0c911004f}"), id: 0x209, priority: 0x0100)
        ],
        VisibleWhen = ActivationConstraint.ClientContext(
            ClientContextKey.Shell.ActiveSelectionFileName, SharedRegex.ConfigFilePattern)
    };

    /// <inheritdoc />
    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken ct) => this.Extensibility.UpdateCommandsAsync(context, configure: false, ct);
}
