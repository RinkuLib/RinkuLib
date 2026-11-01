using System.ComponentModel;

namespace Rinku.Tracking;

public enum TrackingConfirmation : byte
{
    Insert,
    Update,
    Delete
}

public delegate void TrackingConfirmationHandler(ITrackingConfirmationSource item, TrackingConfirmation operation);

public interface ITrackingConfirmationSource : ITrackingListNewState, ICanInsert, ICanUpdate
{
    event TrackingConfirmationHandler? Confirmed;
}

[EditorBrowsable(EditorBrowsableState.Never)]
public static class TrackingEvents
{
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

    public static void Raise(TrackingConfirmationHandler? handler, ITrackingConfirmationSource item, TrackingConfirmation operation) => handler?.Invoke(item, operation);
}
