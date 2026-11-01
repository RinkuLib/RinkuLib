using System.ComponentModel;

namespace Rinku.Tracking.Binding;

/// <summary>Provides event helpers used by generated binding types.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class PropertyChangedHub
{
    /// <summary>Raises a property changed event.</summary>
    public static void Raise(PropertyChangedEventHandler? field, object sender, PropertyChangedEventArgs args)
        => field?.Invoke(sender, args);
}
