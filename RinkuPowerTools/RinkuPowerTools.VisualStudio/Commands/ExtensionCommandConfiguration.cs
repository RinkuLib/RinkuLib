using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace RinkuPowerTools.VisualStudio.Commands;

internal static class ExtensionCommandConfiguration {
    [VisualStudioContribution]
    public static MenuConfiguration ProjectMenu => new("%RinkuPowerTools.VisualStudio.Menu.DisplayName%") {
        Placements = [
            CommandPlacement.VsctParent(new Guid("{d309f791-903f-11d0-9efc-00a0c911004f}"), id: 518, priority: 0x1000)
        ],
        Children = [
            MenuChild.Command<UpdateCommand>(),
            MenuChild.Command<RefreshAllCommand>()
        ]
    };
}
