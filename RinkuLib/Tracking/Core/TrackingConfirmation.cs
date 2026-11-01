using System.ComponentModel;

namespace Rinku.Tracking;

/// <summary>Identifies the operation being confirmed.</summary>
public enum TrackingConfirmation : byte
{
    /// <summary>Confirms an inserted item.</summary>
    Insert,
    /// <summary>Confirms an updated item.</summary>
    Update,
    /// <summary>Confirms a deleted item.</summary>
    Delete
}

/// <summary>Receives a confirmation event for a tracked item.</summary>
public delegate void TrackingConfirmationHandler(ITrackingConfirmationSource item, TrackingConfirmation operation);

/// <summary>Exposes confirmation notifications from a tracked item.</summary>
public interface ITrackingConfirmationSource : ITrackingListNewState, ICanInsert, ICanUpdate
{
    /// <summary>Occurs when the item is confirmed.</summary>
    event TrackingConfirmationHandler? Confirmed;
}

/// <summary>Provides thread-safe event delegate composition for generated tracking types.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class TrackingEvents
{
    /// <summary>Adds a handler to an event field atomically.</summary>
    public static void Add<TDelegate>(ref TDelegate? field, TDelegate handler) where TDelegate : Delegate
    {
        TDelegate? current;
        TDelegate? updated;
        do
        {
            current = field;
            updated = (TDelegate?)Delegate.Combine(current, handler);
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref field, updated, current), current));
    }

    /// <summary>Removes a handler from an event field atomically.</summary>
    public static void Remove<TDelegate>(ref TDelegate? field, TDelegate handler) where TDelegate : Delegate
    {
        TDelegate? current;
        TDelegate? updated;
        do
        {
            current = field;
            updated = (TDelegate?)Delegate.Remove(current, handler);
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref field, updated, current), current));
    }

    /// <summary>Raises a confirmation event when a handler is registered.</summary>
    public static void Raise(TrackingConfirmationHandler? handler, ITrackingConfirmationSource item, TrackingConfirmation operation) => handler?.Invoke(item, operation);
}
