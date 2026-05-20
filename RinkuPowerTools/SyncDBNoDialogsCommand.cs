using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace RinkuPowerTools;

[VisualStudioContribution]
public class SyncDBNoDialogsCommand : Command {
    /// <inheritdoc />
    public override CommandConfiguration CommandConfiguration => new("%RinkuPowerTools.SyncDBNoDialogsCommand.DisplayName%") {
        Icon = new(ImageMoniker.KnownValues.DatabaseColumn, IconSettings.IconAndText),
        Placements = [
            CommandPlacement.VsctParent(new Guid("{d309f791-903f-11d0-9efc-00a0c911004f}"), id: 0x209, priority: 0x0100)
        ],
        VisibleWhen = ActivationConstraint.ClientContext(
            ClientContextKey.Shell.ActiveSelectionFileName,
            "rptOptions\\.json")
    };

    /// <inheritdoc />
    public override Task InitializeAsync(CancellationToken cancellationToken)
        => base.InitializeAsync(cancellationToken);

    /// <inheritdoc />
    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken ct) 
        => SyncDBCommand.ExecuteCommandAsync(this.Extensibility, context, false, ct);
}