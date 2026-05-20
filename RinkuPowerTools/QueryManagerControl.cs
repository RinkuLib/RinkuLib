using Microsoft.VisualStudio.Extensibility.UI;

namespace RinkuPowerTools;

/// <summary>
/// Represents the Remote User Control responsible for rendering the query management layout.
/// </summary>
public partial class QueryManagerControl(QueryManagerControlData dataContext, SynchronizationContext? synchronizationContext = null) : RemoteUserControl(dataContext, synchronizationContext);