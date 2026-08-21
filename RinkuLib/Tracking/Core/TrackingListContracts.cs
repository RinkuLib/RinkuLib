namespace Rinku.Tracking;

/// <summary>
/// Reports whether an item is new to its surrounding source.
/// </summary>
public interface ITrackingListNewState
{
    /// <summary>Gets whether the item is new.</summary>
    bool IsNew { get; }
}

/// <summary>
/// Creates items and confirms changes for a tracking list.
/// </summary>
public interface ITrackingListContext<T>
{
    /// <summary>Gets whether this context can create an item.</summary>
    bool CanCreateNew { get; }
    /// <summary>Creates a new item.</summary>
    T CreateNew();

    /// <summary>Confirms that an item was added.</summary>
    bool ConfirmAdded(T item);
    /// <summary>Confirms edits made to an item.</summary>
    bool ConfirmEdit(T item);
    /// <summary>Confirms that an item was deleted.</summary>
    bool ConfirmDelete(T item);
}

/// <summary>Access to the singleton default context for a given T.</summary>
public static class TrackingListContext<T>
{
    /// <summary>Gets the default context.</summary>
    public static ITrackingListContext<T> Default => DefaultTrackingListContext<T>.Instance;
}
