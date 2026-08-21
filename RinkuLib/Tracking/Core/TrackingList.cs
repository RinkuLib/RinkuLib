using System.Collections;

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

/// <summary>Identifies a confirmed operation.</summary>
public enum TrackingListConfirmationKind : byte
{
    /// <summary>An addition was confirmed.</summary>
    Added,
    /// <summary>An edit was confirmed.</summary>
    Edit,
    /// <summary>A deletion was confirmed.</summary>
    Delete
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
/// Tracks list membership, order, additions, and removals.
/// </summary>
public class TrackingList<T> : IList<T>, IReadOnlyList<T>
{
    private T[] _items;
    private int _count;
    private T[] _removed = [];
    private int _removedCount;
    private int _version;
    private StructuralOriginMap _origins;
    private readonly IEqualityComparer<T>? _comparer;

    /// <summary>Creates an empty tracking list.</summary>
    public TrackingList(int capacity = 0, IEqualityComparer<T>? comparer = null, ITrackingListContext<T>? context = null)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _items = capacity == 0 ? [] : new T[capacity];
        _comparer = comparer is null || ReferenceEquals(comparer, EqualityComparer<T>.Default) ? null : comparer;
        Context = context ?? TrackingListContext<T>.Default;
    }

    /// <summary>Creates a tracking list with baseline items.</summary>
    public TrackingList(IEnumerable<T> items, int initialCapacity = 0, IEqualityComparer<T>? comparer = null, ITrackingListContext<T>? context = null)
        : this(initialCapacity, comparer, context)
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
    /// <summary>Gets the confirmation context.</summary>
    public ITrackingListContext<T> Context { get; }
    /// <summary>Gets whether a new item can be created.</summary>
    public bool CanAddNew => Context.CanCreateNew;

    /// <summary>Gets the number of active added items.</summary>
    public int AddedCount => CountAdded();
    /// <summary>Gets the number of removed items.</summary>
    public int RemovedCount => _removedCount;
    /// <summary>Gets whether structural changes exist.</summary>
    public bool HasChanges => _removedCount != 0 || HasAdded();

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
            if (value < _count) throw new ArgumentOutOfRangeException(nameof(value));
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

    /// <inheritdoc/>
    public virtual void Add(T item) => AddTrackedItem(_count, item);

    /// <summary>Creates and adds a new item.</summary>
    public virtual T AddNew()
    {
        if (!Context.CanCreateNew)
            throw new NotSupportedException($"The tracking-list context for {typeof(T)} cannot create a new item.");
        T item = Context.CreateNew();
        Add(item);
        return item;
    }

    /// <inheritdoc/>
    public virtual void Insert(int index, T item) => AddTrackedItem(index, item);

    /// <summary>Adds an item and returns a token for cancellation.</summary>
    protected AdditionToken AddTrackedItem(int index, T item)
    {
        if ((uint)index > (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        AddDelta delta = TrackAddition(ref item);
        InsertCore(index, item, FallbackAdded(ref item, delta.Kind));
        return new(delta);
    }

    private protected void CancelTrackedAddition(int index, in AdditionToken token)
    {
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        UndoAddition(token.Delta);
        T item = _items[index];
        RemoveActiveAt(index);
        OnChanged(new(TrackingListChangeKind.Remove, index, index, default, item));
    }

    /// <summary>Moves an active item.</summary>
    public void Move(int oldIndex, int newIndex)
    {
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
        int index = IndexOf(item);
        if (index < 0) return false;
        RemoveAt(index);
        return true;
    }

    /// <inheritdoc/>
    public virtual void RemoveAt(int index)
    {
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        T item = _items[index];
        if (!IsAddedUnchecked(index)) TrackRemoved(item);
        RemoveActiveAt(index);
        OnChanged(new(TrackingListChangeKind.Remove, index, index, default, item));
    }

    /// <summary>Restores an observed removal as baseline membership.</summary>
    public T RestoreAt(int removedIndex, int index = -1)
    {
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
        int removedIndex = IndexOfRemoved(item);
        if (removedIndex < 0) return false;
        RestoreAt(removedIndex, index);
        return true;
    }

    /// <inheritdoc/>
    public virtual void Clear()
    {
        if (_count == 0) return;
        for (int i = 0; i < _count; i++)
            if (!IsAddedUnchecked(i)) TrackRemoved(_items[i]);

        Array.Clear(_items, 0, _count);
        _count = 0;
        _origins.Reset();
        _version++;
        OnChanged(new(TrackingListChangeKind.Reset, -1, -1, default, default));
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
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        ref T item = ref _items[index];
        bool itemOwnsState = TrackingListNewStateAccess<T>.TryGet(ref item, out bool wasNew);
        bool fallbackAdded = !itemOwnsState && _origins.IsAdded(index);
        if (!(itemOwnsState ? wasNew : fallbackAdded)) return false;
        if (!Context.ConfirmAdded(item)) return false;

        if (fallbackAdded)
        {
            _origins.Replace(index, _count, false);
            _version++;
        }
        else if (TrackingListNewStateAccess<T>.TryGet(ref item, out bool isNew) && isNew != wasNew)
        {
            _version++;
        }

        OnConfirmed(TrackingListConfirmationKind.Added, index, item);
        return true;
    }

    /// <summary>Confirms the item's edit independently of whether the row is structurally Added.</summary>
    public bool ConfirmEditAt(int index)
    {
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        T item = _items[index];
        if (!Context.ConfirmEdit(item)) return false;
        OnConfirmed(TrackingListConfirmationKind.Edit, index, item);
        return true;
    }

    /// <summary>Confirms deletion of a removed item.</summary>
    public bool ConfirmDeleteAt(int removedIndex)
    {
        if ((uint)removedIndex >= (uint)_removedCount) throw new ArgumentOutOfRangeException(nameof(removedIndex));
        T item = _removed[removedIndex];
        if (!Context.ConfirmDelete(item)) return false;
        RemoveRemovedAt(removedIndex);
        _version++;
        OnConfirmed(TrackingListConfirmationKind.Delete, removedIndex, item);
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
        bool success = true;

        for (int i = _removedCount - 1; i >= 0; i--)
            if (!ConfirmDeleteAt(i)) success = false;

        for (int i = 0; i < _count; i++)
            if (!ConfirmAt(i)) success = false;

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
    }

    /// <summary>Handles a structural change.</summary>
    protected virtual void OnChanged(TrackingListChange<T> change) { }
    /// <summary>Handles a confirmed operation.</summary>
    protected virtual void OnConfirmed(TrackingListConfirmationKind kind, int index, T item) { }

    /// <summary>Replaces an item while tracking structural state.</summary>
    protected void SetTrackedItem(int index, T item) => SetItem(index, item);

    private void SetItem(int index, T item)
    {
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        T previous = _items[index];
        if (!typeof(T).IsValueType && ReferenceEquals(previous, item)) return;

        bool previousAdded = IsAddedUnchecked(index);
        if (Comparer.Equals(previous, item))
        {
            bool fallback = FallbackAdded(ref item, previousAdded ? AdditionKind.Added : AdditionKind.Existing);
            _items[index] = item;
            _origins.Replace(index, _count, fallback);
            _version++;
            OnChanged(new(TrackingListChangeKind.Replace, index, index, item, previous));
            return;
        }

        if (!previousAdded) TrackRemoved(previous);
        AddDelta delta = TrackAddition(ref item);
        _items[index] = item;
        _origins.Replace(index, _count, FallbackAdded(ref item, delta.Kind));
        _version++;
        OnChanged(new(TrackingListChangeKind.Replace, index, index, item, previous));
    }

    private AddDelta TrackAddition(ref T item)
    {
        if (TrackingListNewStateAccess<T>.TryGet(ref item, out bool isNew))
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
        if (TrackingListNewStateAccess<T>.TryGet(ref item, out _)) return false;
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
        OnChanged(new(TrackingListChangeKind.Add, index, -1, item, default));
    }

    private void RemoveActiveAt(int index)
    {
        _origins.Remove(index, _count);
        _count--;
        if (index < _count) Array.Copy(_items, index + 1, _items, index, _count - index);
        Array.Clear(_items, _count, 1);
        _version++;
    }

    private bool IsAddedUnchecked(int index)
    {
        ref T item = ref _items[index];
        return TrackingListNewStateAccess<T>.TryGet(ref item, out bool isNew) ? isNew : _origins.IsAdded(index);
    }

    private bool HasAdded()
    {
        for (int i = 0; i < _count; i++)
            if (IsAddedUnchecked(i)) return true;
        return false;
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
    }

    private T RemoveRemovedAt(int index) => RemoveAt(_removed, ref _removedCount, index);
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

    protected readonly struct AdditionToken
    {
        internal readonly AddDelta Delta;
        internal AdditionToken(AddDelta delta) => Delta = delta;
    }

    protected enum AdditionKind : byte { Added, Existing, Restored }

    protected readonly struct AddDelta
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
}
