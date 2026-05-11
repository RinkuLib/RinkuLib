using System.Runtime.Versioning;
using Microsoft.VisualStudio.Extensibility.UI;

namespace RinkuPowerTools;

/// <summary>
/// Interaction logic for UserControl1.xaml
/// </summary>
[SupportedOSPlatform("windows8.0")]
public partial class RinkuOptionControl : RemoteUserControl {

    /// <summary>
    /// Initializes a new instance of the <see cref="RinkuOptionControl" /> class.
    /// </summary>
    /// <param name="dataContext">
    /// Data context of the remote control which can be referenced from xaml through data binding.
    /// </param>
    /// <param name="synchronizationContext">
    /// Optional synchronizationContext that the extender can provide to ensure that <see cref="IAsyncCommand"/>
    /// are executed and properties are read and updated from the extension main thread.
    /// </param>
    public RinkuOptionControl(RinkuOptionControlData? dataContext, SynchronizationContext? synchronizationContext = null)
        : base(dataContext, synchronizationContext) {
        this.ResourceDictionaries.AddEmbeddedResource("RinkuPowerTools.Resources.DialogText.xaml");
    }
}