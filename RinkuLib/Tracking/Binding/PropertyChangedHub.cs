using System.ComponentModel;

namespace Rinku.Tracking.Binding;

/// <summary>Provides event helpers used by generated binding types.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class PropertyChangedHub
{
    /// <summary>Adds a property changed handler.</summary>
    public static void Add(ref PropertyChangedEventHandler? field, PropertyChangedEventHandler handler)
    {
        PropertyChangedEventHandler? current;
        PropertyChangedEventHandler? updated;
        do
        {
            current = field;
            updated = (PropertyChangedEventHandler?)Delegate.Combine(current, handler);
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref field, updated, current), current));
    }

    /// <summary>Removes a property changed handler.</summary>
    public static void Remove(ref PropertyChangedEventHandler? field, PropertyChangedEventHandler handler)
    {
        PropertyChangedEventHandler? current;
        PropertyChangedEventHandler? updated;
        do
        {
            current = field;
            updated = (PropertyChangedEventHandler?)Delegate.Remove(current, handler);
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref field, updated, current), current));
    }

    /// <summary>Raises a property changed event.</summary>
    public static void Raise(PropertyChangedEventHandler? field, object sender, string? name)
        => field?.Invoke(sender, new PropertyChangedEventArgs(name));
}
