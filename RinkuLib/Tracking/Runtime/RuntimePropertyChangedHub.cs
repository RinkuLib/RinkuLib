using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Rinku.Tracking.Runtime;

// Public only because generated types live in a separate dynamic assembly.
/// <summary>Provides notification helpers for generated types.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class RuntimePropertyChangedHub {
    /// <summary>Adds a notification handler.</summary>
    public static void Add(ref PropertyChangedEventHandler? handlers, PropertyChangedEventHandler handler) {
        ArgumentNullException.ThrowIfNull(handler);
        PropertyChangedEventHandler? current;
        PropertyChangedEventHandler? combined;
        do {
            current = handlers;
            combined = (PropertyChangedEventHandler?)Delegate.Combine(current, handler);
        } while (!ReferenceEquals(Interlocked.CompareExchange(ref handlers, combined, current), current));
    }

    /// <summary>Removes a notification handler.</summary>
    public static void Remove(ref PropertyChangedEventHandler? handlers, PropertyChangedEventHandler handler) {
        ArgumentNullException.ThrowIfNull(handler);
        PropertyChangedEventHandler? current;
        PropertyChangedEventHandler? removed;
        do {
            current = handlers;
            removed = (PropertyChangedEventHandler?)Delegate.Remove(current, handler);
            if (ReferenceEquals(current, removed)) return;
        } while (!ReferenceEquals(Interlocked.CompareExchange(ref handlers, removed, current), current));
    }

    /// <summary>Raises a property notification.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Raise(PropertyChangedEventHandler? handlers, object item, string? propertyName) {
        if (handlers is not null) handlers(item, new PropertyChangedEventArgs(propertyName));
    }
}
