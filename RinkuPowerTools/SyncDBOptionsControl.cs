using System.ComponentModel;
using Microsoft.VisualStudio.Extensibility.UI;

namespace RinkuPowerTools;

public partial class SyncDBOptionsControl(SyncDBOptionsControlData dataContext, SynchronizationContext? synchronizationContext = null) : RemoteUserControl(dataContext, synchronizationContext);