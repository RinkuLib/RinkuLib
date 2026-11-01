namespace Rinku.Tracking;

/// <summary>Confirms item-owned state for a list insertion.</summary>
public interface ICanInsert
{
    /// <summary>Accepts the item-owned state associated with an insertion.</summary>
    /// <returns><see langword="true"/> when the insertion can be confirmed.</returns>
    bool ConfirmInsert();
}

/// <summary>Confirms item-owned state for a list update.</summary>
public interface ICanUpdate
{
    /// <summary>Accepts the item-owned state associated with an update.</summary>
    /// <returns><see langword="true"/> when the update can be confirmed.</returns>
    bool ConfirmUpdate();
}

/// <summary>Confirms item-owned state for a list deletion.</summary>
public interface ICanDelete
{
    /// <summary>Accepts the item-owned state associated with a deletion.</summary>
    /// <returns><see langword="true"/> when the deletion can be confirmed.</returns>
    bool ConfirmDelete();
}

/// <summary>
/// Reports whether an item is a pending list addition.
/// </summary>
public interface ITrackingListNewState
{
    /// <summary>Gets whether the item is new.</summary>
    bool IsNew { get; }
}

/// <summary>Pending persistence operations and their confirmation, independent of list storage.</summary>
public interface ITrackingCommitSource<T>
{
    /// <summary>Enumerates pending insertions and updates.</summary>
    IEnumerable<(T Item, bool IsInsert)> GetChanged();
    /// <summary>Gets pending deletions.</summary>
    ReadOnlySpan<T> RemovedSpan { get; }
    /// <summary>Accepts the operations after persistence succeeds.</summary>
    bool ConfirmChanges();
}

/// <summary>An editable collection of tracked items, independent of storage and UI binding.</summary>
public interface ITrackingSource<T> : IReadOnlyList<T>, ITrackingCommitSource<T> {
    /// <summary>Gets whether the source contains pending changes.</summary>
    bool HasChanges { get; }
    /// <summary>Gets whether a new item can be created.</summary>
    bool CanAddNew { get; }
    /// <summary>Creates and adds a pending item.</summary>
    T AddNew();
    /// <summary>Discards pending changes without undoing confirmations or restoring ordering.</summary>
    void RevertChanges();
}

/// <summary>Accepted list operations observed by caches and other projections.</summary>
public interface ITrackingConfirmations<out T> : IReadOnlyList<T>
{
    /// <summary>Observes accepted operations until the returned subscription is disposed.</summary>
    IDisposable SubscribeConfirmations(Action<int, T>? onAdded = null, Action<int, T>? onUpdated = null, Action<T>? onDeleted = null);
}
