using System.Collections;
using Rinku.Tracking.Core;

namespace Rinku.Tracking;

/// <summary>Identifies a structural list change.</summary>
public enum TrackingListChangeKind : byte
{
    /// <summary>An item was added.</summary>
    Add,
    /// <summary>An item was removed.</summary>
    Remove,
    /// <summary>An item was replaced.</summary>
    Replace,
    /// <summary>An item was moved.</summary>
    Move,
    /// <summary>The list was reset.</summary>
    Reset
}

/// <summary>Describes a structural list change.</summary>
public readonly record struct TrackingListChange<T>
{
    /// <summary>Creates a list change description.</summary>
    public TrackingListChange(TrackingListChangeKind kind, int index, int oldIndex, T? item, T? oldItem)
    {
        Kind = kind;
        Index = index;
        OldIndex = oldIndex;
        Item = item;
        OldItem = oldItem;
    }

    /// <summary>Gets the kind of change.</summary>
    public TrackingListChangeKind Kind { get; }
    /// <summary>Gets the current index.</summary>
    public int Index { get; }
    /// <summary>Gets the previous index.</summary>
    public int OldIndex { get; }
    /// <summary>Gets the current item.</summary>
    public T? Item { get; }
    /// <summary>Gets the previous item.</summary>
    public T? OldItem { get; }
}

/// <summary>
/// Tracks list membership, order, additions, removals, and item changes.
/// </summary>
public class TrackingList<T> : IList<T>, ITrackingSource<T>, ITrackingConfirmations<T>, IDisposable
{
    private T[] _items;
    private int _count;
    private T[] _removed = [];
    private int _removedCount;
    private int _version;
    private StructuralOriginMap _origins;
    private readonly IEqualityComparer<T>? _comparer;
    private readonly Func<T>? _newItemFactory;
    private TrackingConfirmationHandler? _confirmationHandler;
    private bool _disposed;
    private Action<int, T>? _addedConfirmed;
    private Action<int, T>? _updatedConfirmed;
    private Action<T>? _deletedConfirmed;

    /// <summary>Creates an empty tracking list with optional comparison and new-item behavior.</summary>
    public TrackingList(int capacity = 0, IEqualityComparer<T>? comparer = null, Func<T>? newItemFactory = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _items = capacity == 0 ? [] : new T[capacity];
        _comparer = comparer is null || ReferenceEquals(comparer, EqualityComparer<T>.Default) ? null : comparer;
        _newItemFactory = newItemFactory ?? DefaultConstructor<T>.Factory;
    }

    /// <summary>Creates a tracking list whose supplied items form the accepted baseline.</summary>
    public TrackingList(IEnumerable<T> items, int initialCapacity = 0, IEqualityComparer<T>? comparer = null, Func<T>? newItemFactory = null)
        : this(initialCapacity, comparer, newItemFactory)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.TryGetNonEnumeratedCount(out int count) && count > _items.Length)
            _items = new T[Math.Max(count, initialCapacity)];
        foreach (T item in items) AddInitial(item);
    }

    /// <inheritdoc/>
    public int Count => _count;
    /// <inheritdoc/>
    public bool IsReadOnly => false;
    /// <summary>Gets the item comparer.</summary>
    public IEqualityComparer<T> Comparer => _comparer ?? EqualityComparer<T>.Default;
    /// <summary>Gets whether <see cref="AddNew"/> can create an item.</summary>
    public bool CanAddNew => _newItemFactory is not null;

    /// <summary>Gets the number of active added items.</summary>
    public int AddedCount => CountAdded();
    /// <summary>Gets the number of removed items.</summary>
    public int RemovedCount => _removedCount;
    /// <summary>Gets whether any tracked change exists.</summary>
    public bool HasChanges
    {
        get
        {
            if (_removedCount != 0) return true;
            for (int index = 0; index < _count; index++)
            {
                if (IsAddedUnchecked(index) || _items[index] is ITrackingChanges changes && changes.HasChanges())
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>Gets the active added items.</summary>
    public AddedCollection Added => new(this);
    /// <summary>Gets the removed items.</summary>
    public RemovedCollection Removed => new(this);
    /// <summary>Gets a span over the active items.</summary>
    public ReadOnlySpan<T> AsSpan() => _items.AsSpan(0, _count);
    /// <summary>Gets a span over the removed items.</summary>
    public ReadOnlySpan<T> RemovedSpan => _removed.AsSpan(0, _removedCount);

    /// <summary>Gets or sets the active item capacity.</summary>
    public int Capacity
    {
        get => _items.Length;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, _count);
            if (value != _items.Length) Array.Resize(ref _items, value);
        }
    }

    /// <inheritdoc/>
    public virtual T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
            return _items[index];
        }
        set => SetItem(index, value);
    }

    /// <summary>Returns whether the item at an index is added.</summary>
    public bool IsAddedAt(int index)
    {
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        return IsAddedUnchecked(index);
    }

    /// <summary>Enumerates active items with pending changes and identifies insertions.</summary>
    public IEnumerable<(T Item, bool IsInsert)> GetChanged()
    {
        for (int i = 0; i < _count; i++)
        {
            T item = _items[i];
            bool isInsert = IsAddedUnchecked(i);
            if (isInsert || item is ITrackingChanges changes && changes.HasChanges())
                yield return (item, isInsert);
        }
    }

    /// <inheritdoc/>
    public virtual void Add(T item) => AddTrackedItem(_count, item);

    /// <summary>Creates and adds a new item.</summary>
    public virtual T AddNew()
    {
        T item = CreateNewItem();
        Add(item);
        return item;
    }

    /// <summary>Creates an item using the configured new-item factory.</summary>
    protected T CreateNewItem()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Func<T> factory = _newItemFactory
            ?? throw new NotSupportedException($"{typeof(T)} has no public parameterless constructor and no new-item factory was supplied.");
        return factory();
    }

    /// <summary>Subscribes to successful added, updated, and deleted confirmations.</summary>
    /// <remarks>The returned subscription removes these handlers when disposed.</remarks>
    public IDisposable SubscribeConfirmations(Action<int, T>? onAdded = null, Action<int, T>? onUpdated = null, Action<T>? onDeleted = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (onAdded is null && onUpdated is null && onDeleted is null)
            throw new ArgumentException("At least one confirmation callback is required.");

        _addedConfirmed += onAdded;
        _updatedConfirmed += onUpdated;
        _deletedConfirmed += onDeleted;
        return new ConfirmationSubscription(this, onAdded, onUpdated, onDeleted);
    }

    /// <inheritdoc/>
    public virtual void Insert(int index, T item) => AddTrackedItem(index, item);

    private protected AddDelta AddTrackedItem(int index, T item)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)index > (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        AddDelta delta = TrackAddition(ref item);
        InsertCore(index, item, FallbackAdded(ref item, delta.Kind));
        return delta;
    }

    private protected void CancelTrackedAddition(int index, in AddDelta delta)
    {
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        UndoAddition(delta);
        T item = _items[index];
        RemoveActiveAt(index);
        OnChanged(new(TrackingListChangeKind.Remove, index, index, default, item));
    }

    /// <summary>Moves an active item.</summary>
    public void Move(int oldIndex, int newIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)oldIndex >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(oldIndex));
        if ((uint)newIndex >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(newIndex));
        if (oldIndex == newIndex) return;

        T item = _items[oldIndex];
        _origins.Move(oldIndex, newIndex, _count);
        if (oldIndex < newIndex)
            Array.Copy(_items, oldIndex + 1, _items, oldIndex, newIndex - oldIndex);
        else
            Array.Copy(_items, newIndex, _items, newIndex + 1, oldIndex - newIndex);
        _items[newIndex] = item;
        _version++;
        OnChanged(new(TrackingListChangeKind.Move, newIndex, oldIndex, item, item));
    }

    /// <inheritdoc/>
    public virtual bool Remove(T item)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int index = IndexOf(item);
        if (index < 0) return false;
        RemoveAt(index);
        return true;
    }

    /// <inheritdoc/>
    public virtual void RemoveAt(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        T item = _items[index];
        if (!IsAddedUnchecked(index)) TrackRemoved(item);
        RemoveActiveAt(index);
        OnChanged(new(TrackingListChangeKind.Remove, index, index, default, item));
    }

    /// <summary>Restores an observed removal as baseline membership.</summary>
    public T RestoreAt(int removedIndex, int index = -1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)removedIndex >= (uint)_removedCount) throw new ArgumentOutOfRangeException(nameof(removedIndex));
        if (index < 0) index = _count;
        if ((uint)index > (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        T item = RemoveRemovedAt(removedIndex);
        InsertCore(index, item, false);
        return item;
    }

    /// <summary>Restores a removed item.</summary>
    public bool Restore(T item, int index = -1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int removedIndex = IndexOfRemoved(item);
        if (removedIndex < 0) return false;
        RestoreAt(removedIndex, index);
        return true;
    }

    /// <inheritdoc/>
    public virtual void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_count == 0) return;
        StopObserving();
        for (int i = 0; i < _count; i++)
            if (!IsAddedUnchecked(i)) TrackRemoved(_items[i]);

        Array.Clear(_items, 0, _count);
        _count = 0;
        _origins.Reset();
        _version++;
        for (int index = 0; index < _removedCount; index++)
        {
            Observe(_removed[index]);
        }
        OnChanged(new(TrackingListChangeKind.Reset, -1, -1, default, default));
    }

    /// <summary>Replaces the accepted baseline with a new set of items.</summary>
    /// <remarks>This is intended for projections refreshed from an external source.</remarks>
    public void ResetBaseline(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ObjectDisposedException.ThrowIf(_disposed, this);
        T[] baseline = items.ToArray();
        ReplaceBaseline(baseline, baseline.Length);
    }

    private void ReplaceBaseline(T[] baseline, int count) {
        StopObserving();
        _items = baseline;
        _count = 0;
        _removed = [];
        _removedCount = 0;
        _origins.Reset();
        _version++;
        for (int index = 0; index < count; index++) AddInitial(baseline[index]);
        OnChanged(new(TrackingListChangeKind.Reset, -1, -1, default, default));
    }

    /// <summary>Discards pending item changes, additions and removals without undoing confirmations or reordering surviving items.</summary>
    /// <remarks>Pending removals are restored at the end. No external collection or confirmation callback is required.</remarks>
    public void RevertChanges() {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (int index = 0; index < _count; index++) CancelEdit(ref _items[index]);
        for (int index = 0; index < _removedCount; index++) CancelEdit(ref _removed[index]);
        StopObserving();
        int retained = 0;
        for (int index = 0; index < _count; index++)
            if (!IsAddedUnchecked(index)) _items[retained++] = _items[index];
        int restored = retained + _removedCount;
        EnsureCapacity(restored);
        Array.Copy(_removed, 0, _items, retained, _removedCount);
        if (restored < _count) Array.Clear(_items, restored, _count - restored);
        Array.Clear(_removed, 0, _removedCount);
        _removedCount = 0;
        _count = restored;
        _origins.Reset();
        _version++;
        for (int index = 0; index < _count; index++) Observe(_items[index]);
        OnChanged(new(TrackingListChangeKind.Reset, -1, -1, default, default));
    }

    private static void CancelEdit(ref T item) {
        if (!typeof(T).IsValueType) {
            if (item is IEditable editable) editable.CancelEdit();
            return;
        }
        if (item is not IEditable) return;
        object boxed = item;
        try { ((IEditable)boxed).CancelEdit(); }
        finally { item = (T)boxed; }
    }
    /// <summary>Confirms the row as Added or Edit according to its current structural state.</summary>
    public bool ConfirmAt(int index)
    {
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        return IsAddedUnchecked(index) ? ConfirmAddedAt(index) : ConfirmEditAt(index);
    }

    /// <summary>Confirms the active operation for an item.</summary>
    public bool Confirm(T item)
    {
        int index = IndexOf(item);
        return index >= 0 && ConfirmAt(index);
    }

    /// <summary>
    /// Confirms an addition and clears list-owned added state on success.
    /// </summary>
    public bool ConfirmAddedAt(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        ref T item = ref _items[index];
        if (!typeof(T).IsValueType && item is ITrackingConfirmationSource)
        {
            return IsAddedUnchecked(index) && ConfirmInsert(ref item);
        }
        bool itemOwnsState = TryGetNewState(item, out bool wasNew);
        bool fallbackAdded = !itemOwnsState && _origins.IsAdded(index);
        if (!(itemOwnsState ? wasNew : fallbackAdded)) return false;
        if (!ConfirmInsert(ref item)) return false;

        if (fallbackAdded)
        {
            _origins.Replace(index, _count, false);
            _version++;
        }
        else if (TryGetNewState(item, out bool isNew) && isNew != wasNew)
        {
            _version++;
        }

        PublishAddedConfirmed(index, item);
        return true;
    }

    /// <summary>Confirms the item's edit independently of whether the row is structurally Added.</summary>
    public bool ConfirmEditAt(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        ref T item = ref _items[index];
        if (!ConfirmUpdate(ref item)) return false;
        if (typeof(T).IsValueType || item is not ITrackingConfirmationSource)
        {
            PublishUpdatedConfirmed(index, item);
        }
        return true;
    }

    /// <summary>Confirms deletion of a removed item.</summary>
    public bool ConfirmDeleteAt(int removedIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)removedIndex >= (uint)_removedCount) throw new ArgumentOutOfRangeException(nameof(removedIndex));
        ref T itemReference = ref _removed[removedIndex];
        if (!typeof(T).IsValueType && itemReference is ITrackingConfirmationSource && itemReference is ICanDelete)
        {
            return ConfirmDelete(ref itemReference);
        }
        if (!ConfirmDelete(ref itemReference)) return false;
        T item = itemReference;
        RemoveRemovedAt(removedIndex);
        _version++;
        PublishDeletedConfirmed(item);
        return true;
    }

    /// <summary>Confirms deletion of a removed item.</summary>
    public bool ConfirmDelete(T item)
    {
        int index = IndexOfRemoved(item);
        return index >= 0 && ConfirmDeleteAt(index);
    }

    /// <summary>
    /// Confirms all observed operations independently.
    /// </summary>
    public bool ConfirmChanges()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool success = true;
        List<Exception>? failures = null;

        for (int i = _removedCount - 1; i >= 0; i--)
        {
            try
            {
                if (!ConfirmDeleteAt(i)) success = false;
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
                success = false;
            }
        }

        for (int i = 0; i < _count; i++)
        {
            try
            {
                if (!ConfirmAt(i)) success = false;
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
                success = false;
            }
        }

        if (failures is not null)
        {
            if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException(failures);
        }

        return success;
    }

    /// <inheritdoc/>
    public int IndexOf(T item) => IndexOf(_items, _count, item);
    /// <inheritdoc/>
    public bool Contains(T item) => IndexOf(item) >= 0;

    /// <inheritdoc/>
    public void CopyTo(T[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        if ((uint)arrayIndex > (uint)array.Length || array.Length - arrayIndex < _count)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        Array.Copy(_items, 0, array, arrayIndex, _count);
    }

    /// <summary>Copies added items to a destination span.</summary>
    public int CopyAddedTo(Span<T> destination)
    {
        int count = 0;
        for (int i = 0; i < _count; i++)
        {
            if (!IsAddedUnchecked(i)) continue;
            if ((uint)count >= (uint)destination.Length)
                throw new ArgumentException("Destination is too small.", nameof(destination));
            destination[count++] = _items[i];
        }
        return count;
    }

    /// <summary>Ensures capacity for active items.</summary>
    public int EnsureCapacity(int capacity)
    {
        if (capacity <= _items.Length) return _items.Length;
        int next = _items.Length == 0 ? 4 : _items.Length * 2;
        if ((uint)next > 0X7FEFFFFF) next = 0X7FEFFFFF;
        if (next < capacity) next = capacity;
        Array.Resize(ref _items, next);
        return next;
    }

    /// <summary>Trims unused active and removed storage.</summary>
    public void TrimExcess()
    {
        if (_count != _items.Length) Array.Resize(ref _items, _count);
        if (_removedCount != _removed.Length) Array.Resize(ref _removed, _removedCount);
        _origins.Trim(_count);
    }

    internal void AddInitial(T item)
    {
        EnsureCapacity(_count + 1);
        _items[_count] = item;
        _origins.Insert(_count, _count, false);
        _count++;
        Observe(item);
    }

    /// <summary>Handles a structural change.</summary>
    protected virtual void OnChanged(TrackingListChange<T> change) { }
    /// <summary>Handles an addition after its item and list state are confirmed.</summary>
    protected virtual void OnAddedConfirmed(int index, T item) { }
    /// <summary>Handles an update after its item state is confirmed.</summary>
    protected virtual void OnUpdatedConfirmed(int index, T item) { }
    /// <summary>Handles a deletion after its item and list state are confirmed.</summary>
    protected virtual void OnDeletedConfirmed(T item) { }

    private void PublishAddedConfirmed(int index, T item)
    {
        OnAddedConfirmed(index, item);
        _addedConfirmed?.Invoke(index, item);
    }

    private void PublishUpdatedConfirmed(int index, T item)
    {
        OnUpdatedConfirmed(index, item);
        _updatedConfirmed?.Invoke(index, item);
    }

    private void PublishDeletedConfirmed(T item)
    {
        OnDeletedConfirmed(item);
        _deletedConfirmed?.Invoke(item);
    }

    private void Observe(T item)
    {
        if (!typeof(T).IsValueType && item is ITrackingConfirmationSource source)
        {
            _confirmationHandler ??= ItemConfirmed;
            source.Confirmed -= _confirmationHandler;
            source.Confirmed += _confirmationHandler;
        }
    }

    private void Unobserve(T item)
    {
        if (_confirmationHandler is null || typeof(T).IsValueType || item is not ITrackingConfirmationSource source)
        {
            return;
        }
        for (int index = 0; index < _count; index++)
        {
            if (ReferenceEquals(_items[index], source))
            {
                return;
            }
        }
        for (int index = 0; index < _removedCount; index++)
        {
            if (ReferenceEquals(_removed[index], source))
            {
                return;
            }
        }
        source.Confirmed -= _confirmationHandler;
    }

    private void StopObserving()
    {
        if (_confirmationHandler is null)
        {
            return;
        }
        for (int index = 0; index < _count; index++)
        {
            if (_items[index] is ITrackingConfirmationSource source)
            {
                source.Confirmed -= _confirmationHandler;
            }
        }
        for (int index = 0; index < _removedCount; index++)
        {
            if (_removed[index] is ITrackingConfirmationSource source)
            {
                source.Confirmed -= _confirmationHandler;
            }
        }
    }

    private void ItemConfirmed(ITrackingConfirmationSource source, TrackingConfirmation operation)
    {
        for (int index = 0; index < _count; index++)
        {
            if (!ReferenceEquals(_items[index], source))
            {
                continue;
            }
            T item = _items[index];
            switch (operation)
            {
                case TrackingConfirmation.Insert:
                    _version++;
                    PublishAddedConfirmed(index, item);
                    break;
                case TrackingConfirmation.Update:
                    PublishUpdatedConfirmed(index, item);
                    break;
                case TrackingConfirmation.Delete:
                    RemoveActiveAt(index);
                    PublishDeletedConfirmed(item);
                    OnChanged(new(TrackingListChangeKind.Remove, index, index, default, item));
                    break;
            }
            return;
        }
        if (operation == TrackingConfirmation.Delete)
        {
            for (int index = 0; index < _removedCount; index++)
            {
                if (ReferenceEquals(_removed[index], source))
                {
                    T item = RemoveRemovedAt(index);
                    _version++;
                    PublishDeletedConfirmed(item);
                    return;
                }
            }
        }
    }

    /// <summary>Releases subscriptions and managed list resources.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            Dispose(true);
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases managed resources owned by the list.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopObserving();
            _addedConfirmed = null;
            _updatedConfirmed = null;
            _deletedConfirmed = null;
        }
    }

    private void RemoveSubscription(ConfirmationSubscription subscription)
    {
        _addedConfirmed -= subscription.OnAdded;
        _updatedConfirmed -= subscription.OnUpdated;
        _deletedConfirmed -= subscription.OnDeleted;
    }

    /// <summary>Replaces an item while tracking structural state.</summary>
    protected void SetTrackedItem(int index, T item) => SetItem(index, item);

    private void SetItem(int index, T item)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        T previous = _items[index];
        if (!typeof(T).IsValueType && ReferenceEquals(previous, item)) return;

        bool previousAdded = IsAddedUnchecked(index);
        if (Comparer.Equals(previous, item))
        {
            bool fallback = FallbackAdded(ref item, previousAdded ? AdditionKind.Added : AdditionKind.Existing);
            _items[index] = item;
            Observe(item);
            Unobserve(previous);
            _origins.Replace(index, _count, fallback);
            _version++;
            OnChanged(new(TrackingListChangeKind.Replace, index, index, item, previous));
            return;
        }

        if (!previousAdded) TrackRemoved(previous);
        AddDelta delta = TrackAddition(ref item);
        _items[index] = item;
        Observe(item);
        Unobserve(previous);
        _origins.Replace(index, _count, FallbackAdded(ref item, delta.Kind));
        _version++;
        OnChanged(new(TrackingListChangeKind.Replace, index, index, item, previous));
    }

    private static bool TryGetNewState(T item, out bool isNew)
    {
        if (item is ITrackingListNewState state)
        {
            isNew = state.IsNew;
            return true;
        }

        isNew = false;
        return false;
    }

    private static bool ConfirmInsert(ref T item)
    {
        if (!typeof(T).IsValueType) return item is not ICanInsert capability || capability.ConfirmInsert();
        if (!typeof(ICanInsert).IsAssignableFrom(typeof(T))) return true;
        object boxed = (object?)item ?? throw new InvalidOperationException("A value-type tracking item cannot be null.");
        try { return ((ICanInsert)boxed).ConfirmInsert(); }
        finally { item = (T)boxed; }
    }

    private static bool ConfirmUpdate(ref T item)
    {
        if (!typeof(T).IsValueType) return item is not ICanUpdate capability || capability.ConfirmUpdate();
        if (!typeof(ICanUpdate).IsAssignableFrom(typeof(T))) return true;
        object boxed = (object?)item ?? throw new InvalidOperationException("A value-type tracking item cannot be null.");
        try { return ((ICanUpdate)boxed).ConfirmUpdate(); }
        finally { item = (T)boxed; }
    }

    private static bool ConfirmDelete(ref T item)
    {
        if (!typeof(T).IsValueType) return item is not ICanDelete capability || capability.ConfirmDelete();
        if (!typeof(ICanDelete).IsAssignableFrom(typeof(T))) return true;
        object boxed = (object?)item ?? throw new InvalidOperationException("A value-type tracking item cannot be null.");
        try { return ((ICanDelete)boxed).ConfirmDelete(); }
        finally { item = (T)boxed; }
    }

    private AddDelta TrackAddition(ref T item)
    {
        if (TryGetNewState(item, out bool isNew))
        {
            if (isNew) return AddDelta.Added;
            int acceptedRemoved = IndexOfRemoved(item);
            return acceptedRemoved < 0
                ? AddDelta.Existing
                : AddDelta.Restored(RemoveRemovedAt(acceptedRemoved), acceptedRemoved);
        }

        int removedIndex = IndexOfRemoved(item);
        return removedIndex < 0
            ? AddDelta.Added
            : AddDelta.Restored(RemoveRemovedAt(removedIndex), removedIndex);
    }

    private static bool FallbackAdded(ref T item, AdditionKind kind)
    {
        if (TryGetNewState(item, out _)) return false;
        return kind == AdditionKind.Added;
    }

    private void InsertCore(int index, T item, bool fallbackAdded)
    {
        EnsureCapacity(_count + 1);
        if (index < _count) Array.Copy(_items, index, _items, index + 1, _count - index);
        _items[index] = item;
        _origins.Insert(index, _count, fallbackAdded);
        _count++;
        _version++;
        Observe(item);
        OnChanged(new(TrackingListChangeKind.Add, index, -1, item, default));
    }

    private void RemoveActiveAt(int index)
    {
        T removed = _items[index];
        _origins.Remove(index, _count);
        _count--;
        if (index < _count) Array.Copy(_items, index + 1, _items, index, _count - index);
        Array.Clear(_items, _count, 1);
        _version++;
        Unobserve(removed);
    }

    private bool IsAddedUnchecked(int index)
    {
        ref T item = ref _items[index];
        return TryGetNewState(item, out bool isNew) ? isNew : _origins.IsAdded(index);
    }

    private int CountAdded()
    {
        int count = 0;
        for (int i = 0; i < _count; i++)
            if (IsAddedUnchecked(i)) count++;
        return count;
    }

    private void TrackRemoved(T item)
    {
        EnsureDeltaCapacity(ref _removed, _removedCount + 1);
        _removed[_removedCount++] = item;
    }

    private void InsertRemoved(int index, T item)
    {
        EnsureDeltaCapacity(ref _removed, _removedCount + 1);
        if (index < _removedCount) Array.Copy(_removed, index, _removed, index + 1, _removedCount - index);
        _removed[index] = item;
        _removedCount++;
        Observe(item);
    }

    private T RemoveRemovedAt(int index)
    {
        T item = RemoveAt(_removed, ref _removedCount, index);
        Unobserve(item);
        return item;
    }
    private int IndexOfRemoved(T item) => IndexOf(_removed, _removedCount, item);

    private int IndexOf(T[] items, int count, T item)
    {
        IEqualityComparer<T> comparer = _comparer ?? EqualityComparer<T>.Default;
        for (int i = 0; i < count; i++)
            if (comparer.Equals(item, items[i])) return i;
        return -1;
    }

    private static T RemoveAt(T[] items, ref int count, int index)
    {
        T item = items[index];
        count--;
        if (index < count) Array.Copy(items, index + 1, items, index, count - index);
        Array.Clear(items, count, 1);
        return item;
    }

    private static void EnsureDeltaCapacity(ref T[] items, int capacity)
    {
        if (capacity <= items.Length) return;
        int next = items.Length == 0 ? 4 : items.Length * 2;
        if (next < capacity) next = capacity;
        Array.Resize(ref items, next);
    }

    private void UndoAddition(in AddDelta delta)
    {
        if (delta.Kind == AdditionKind.Restored && delta.RemovedItem is T item)
            InsertRemoved(Math.Min(delta.RemovedIndex, _removedCount), item);
    }

    /// <summary>Gets an active item enumerator.</summary>
    public Enumerator GetEnumerator() => new(this);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);
    IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

    /// <summary>Enumerates active items.</summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly TrackingList<T> _list;
        private readonly int _version;
        private int _index;

        internal Enumerator(TrackingList<T> list)
        {
            _list = list;
            _version = list._version;
            _index = -1;
        }

        /// <inheritdoc/>
        public readonly T Current => _list._items[_index];
        readonly object? IEnumerator.Current => Current;

        /// <inheritdoc/>
        public bool MoveNext()
        {
            if (_version != _list._version) throw new InvalidOperationException("Collection was modified during enumeration.");
            int next = _index + 1;
            if (next >= _list._count) return false;
            _index = next;
            return true;
        }

        /// <inheritdoc/>
        public void Reset()
        {
            if (_version != _list._version) throw new InvalidOperationException("Collection was modified during enumeration.");
            _index = -1;
        }

        /// <inheritdoc/>
        public readonly void Dispose() { }
    }

    /// <summary>Provides the active added items.</summary>
    public readonly struct AddedCollection : IReadOnlyCollection<T>
    {
        private readonly TrackingList<T> _list;
        internal AddedCollection(TrackingList<T> list) => _list = list;
        /// <inheritdoc/>
        public int Count => _list.AddedCount;
        /// <summary>Gets an added item enumerator.</summary>
        public AddedEnumerator GetEnumerator() => new(_list);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Enumerates active added items.</summary>
    public struct AddedEnumerator : IEnumerator<T>
    {
        private readonly TrackingList<T> _list;
        private readonly int _version;
        private int _index;

        internal AddedEnumerator(TrackingList<T> list)
        {
            _list = list;
            _version = list._version;
            _index = -1;
        }

        /// <inheritdoc/>
        public readonly T Current => _list._items[_index];
        readonly object? IEnumerator.Current => Current;

        /// <inheritdoc/>
        public bool MoveNext()
        {
            if (_version != _list._version) throw new InvalidOperationException("Collection was modified during enumeration.");
            while (++_index < _list._count)
                if (_list.IsAddedUnchecked(_index)) return true;
            return false;
        }

        /// <inheritdoc/>
        public void Reset()
        {
            if (_version != _list._version) throw new InvalidOperationException("Collection was modified during enumeration.");
            _index = -1;
        }

        /// <inheritdoc/>
        public readonly void Dispose() { }
    }

    /// <summary>Provides removed items.</summary>
    public readonly struct RemovedCollection : IReadOnlyList<T>
    {
        private readonly TrackingList<T> _list;
        internal RemovedCollection(TrackingList<T> list) => _list = list;
        /// <inheritdoc/>
        public int Count => _list._removedCount;
        /// <inheritdoc/>
        public T this[int index] => (uint)index < (uint)_list._removedCount
            ? _list._removed[index]
            : throw new ArgumentOutOfRangeException(nameof(index));
        /// <summary>Gets a removed item enumerator.</summary>
        public RemovedEnumerator GetEnumerator() => new(_list);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Enumerates removed items.</summary>
    public struct RemovedEnumerator : IEnumerator<T>
    {
        private readonly TrackingList<T> _list;
        private readonly int _version;
        private int _index;

        internal RemovedEnumerator(TrackingList<T> list)
        {
            _list = list;
            _version = list._version;
            _index = -1;
        }

        /// <inheritdoc/>
        public readonly T Current => _list._removed[_index];
        readonly object? IEnumerator.Current => Current;

        /// <inheritdoc/>
        public bool MoveNext()
        {
            if (_version != _list._version) throw new InvalidOperationException("Collection was modified during enumeration.");
            return ++_index < _list._removedCount;
        }

        /// <inheritdoc/>
        public void Reset()
        {
            if (_version != _list._version) throw new InvalidOperationException("Collection was modified during enumeration.");
            _index = -1;
        }

        /// <inheritdoc/>
        public readonly void Dispose() { }
    }

    internal enum AdditionKind : byte { Added, Existing, Restored }

    private protected readonly struct AddDelta
    {
        private AddDelta(AdditionKind kind, T? removedItem, int removedIndex)
        {
            Kind = kind;
            RemovedItem = removedItem;
            RemovedIndex = removedIndex;
        }

        internal AdditionKind Kind { get; }
        internal T? RemovedItem { get; }
        internal int RemovedIndex { get; }
        internal static AddDelta Added => new(AdditionKind.Added, default, -1);
        internal static AddDelta Existing => new(AdditionKind.Existing, default, -1);
        internal static AddDelta Restored(T item, int index) => new(AdditionKind.Restored, item, index);
    }

    private sealed class ConfirmationSubscription : IDisposable
    {
        internal ConfirmationSubscription(TrackingList<T> owner, Action<int, T>? onAdded, Action<int, T>? onUpdated, Action<T>? onDeleted)
        {
            Owner = owner;
            OnAdded = onAdded;
            OnUpdated = onUpdated;
            OnDeleted = onDeleted;
        }

        internal TrackingList<T>? Owner;
        internal readonly Action<int, T>? OnAdded;
        internal readonly Action<int, T>? OnUpdated;
        internal readonly Action<T>? OnDeleted;

        public void Dispose()
        {
            TrackingList<T>? owner = Interlocked.Exchange(ref Owner, null);
            owner?.RemoveSubscription(this);
        }
    }
}
