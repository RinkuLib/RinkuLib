using Microsoft.VisualStudio.Extensibility.UI;

namespace RinkuPowerTools;

public partial class QuerySelectorControl(QuerySelectorControlData dataContext, SynchronizationContext? synchronizationContext = null)
    : RemoteUserControl(dataContext, synchronizationContext);