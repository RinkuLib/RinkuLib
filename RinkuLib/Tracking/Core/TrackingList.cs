using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

namespace Rinku.Tracking;

// Structural delta tracking only. Active items are stored once, in current list order.
/// <summary>Tracks structural changes in a list.</summary>
public sealed class TrackingList<T> : IList<T>, IReadOnlyList<T>, IList, IBindingList, ITypedList, ICancelAddNew {
    private T[] _items;
    private int _count;
    private T[] _removed = [];
    private int _removedCount;
    private int _version;
    private StructuralOriginMap _origins;
    private readonly IEqualityComparer<T>? _comparer;
    private BindingState? _binding;

    /// <inheritdoc/>
    public TrackingList(int capacity = 0, IEqualityComparer<T>? comparer = null) {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _comparer = comparer is null || ReferenceEquals(comparer, EqualityComparer<T>.Default) ? null : comparer;
        _items = capacity == 0 ? [] : new T[capacity];
    }

    /// <inheritdoc/>
    public TrackingList(IEqualityComparer<T> equalityComparer) : this(0, equalityComparer)
        => ArgumentNullException.ThrowIfNull(equalityComparer);

    /// <inheritdoc/>
    public TrackingList(IEnumerable<T> items, int initialCapacity = 0, IEqualityComparer<T>? comparer = null) : this(initialCapacity, comparer) {
        ArgumentNullException.ThrowIfNull(items);
        Materialize(items, initialCapacity);
    }

    /// <inheritdoc/>
    public TrackingList(IEnumerable<T> items, IEqualityComparer<T> equalityComparer, int initialCapacity = 0) : this(items, initialCapacity, equalityComparer)
        => ArgumentNullException.ThrowIfNull(equalityComparer);

    /// <summary>Gets the number of active items.</summary>
    public int Count => _count;
    /// <summary>Gets or sets the backing capacity.</summary>
    public int Capacity {
        get => _items.Length;
        set {
            if (value < _count) throw new ArgumentOutOfRangeException(nameof(value));
            if (value != _items.Length) Array.Resize(ref _items, value);
        }
    }
    /// <inheritdoc/>
    public bool IsReadOnly => false;
    /// <summary>Gets the comparer used by the list.</summary>
    public IEqualityComparer<T> Comparer => _comparer ?? EqualityComparer<T>.Default;
    /// <summary>Gets removed items.</summary>
    public RemovedCollection Removed => new(this);
    /// <summary>Gets added items.</summary>
    public AddedCollection Added => new(this);
    /// <summary>Gets the number of removed items.</summary>
    public int RemovedCount => _removedCount;
    /// <summary>Gets the number of added items.</summary>
    public int AddedCount => CountAdded();
    /// <summary>Gets whether structural changes are pending.</summary>
    public bool HasStructuralChanges => _removedCount != 0 || AddedCount != 0;

    /// <summary>Gets or sets whether bound edits are allowed.</summary>
    public bool AllowEdit { get => _binding?.AllowEdit ?? true; set => Binding.AllowEdit = value; }
    /// <summary>Gets whether new items can be created.</summary>
    public bool CanAddNew => _binding?.AddingNew is not null || _binding?.Factory is not null || typeof(T).IsValueType || NewItemConstructorCache.Value is not null;
    /// <summary>Gets or sets the new-item factory.</summary>
    public Func<T>? NewItemFactory { get => _binding?.Factory; set { if (value is not null || _binding is not null) Binding.Factory = value; } }
    /// <summary>Gets or sets the pending-new cancellation handler.</summary>
    public Func<T, bool>? CancelNewHandler { get => _binding?.CancelNew; set { if (value is not null || _binding is not null) Binding.CancelNew = value; } }

    /// <summary>Occurs when the list or one of its observed items changes.</summary>
    public event ListChangedEventHandler? ListChanged {
        add {
            if (value is null) return;
            BindingState state = Binding;
            bool first = state.ListChanged is null;
            state.ListChanged += value;
            if (first) SubscribeInitial(state);
        }
        remove {
            if (_binding is null || value is null) return;
            _binding.ListChanged -= value;
            if (_binding.ListChanged is null) UnsubscribeAll(_binding);
        }
    }

    /// <summary>Occurs when a data-binding consumer requests a new item.</summary>
    public event AddingNewEventHandler? AddingNew {
        add { if (value is not null) Binding.AddingNew += value; }
        remove { if (_binding is not null) _binding.AddingNew -= value; }
    }

    /// <inheritdoc/>
    public T this[int index] {
        get {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
            return _items[index];
        }
        set => SetItem(index, value);
    }

    /// <summary>Returns active items as a span.</summary>
    public ReadOnlySpan<T> AsSpan() => _items.AsSpan(0, _count);
    /// <summary>Gets removed items as a span.</summary>
    public ReadOnlySpan<T> RemovedSpan => _removed.AsSpan(0, _removedCount);

    /// <summary>Gets whether an item was added at an index.</summary>
    public bool IsAddedAt(int index) {
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        return IsAddedUnchecked(index);
    }

    private bool IsAddedUnchecked(int index)
        => TrackingItemCapabilities<T>.HasOriginalCapability
            ? !TrackingItemCapabilities<T>.HasOriginal(_items[index])
            : _origins.IsAdded(index);

    private int CountAdded() {
        if (!TrackingItemCapabilities<T>.HasOriginalCapability) return _origins.AddedCount(_count);
        int count = 0;
        for (int i = 0; i < _count; i++) if (!TrackingItemCapabilities<T>.HasOriginal(_items[i])) count++;
        return count;
    }

    /// <inheritdoc/>
    public void Add(T item) { EndPendingNew(); AddCore(item, out _); }

    /// <inheritdoc/>
    public void Insert(int index, T item) {
        if ((uint)index > (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        EndPendingNew();
        AddDelta delta = TrackAddition(item);
        InsertCore(index, item, delta.IsAdded);
    }

    // Reorders the active row without changing its structural origin.
    /// <inheritdoc/>
    public void Move(int oldIndex, int newIndex) {
        if ((uint)oldIndex >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(oldIndex));
        if ((uint)newIndex >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(newIndex));
        if (oldIndex == newIndex) return;

        T item = _items[oldIndex];
        if (!TrackingItemCapabilities<T>.HasOriginalCapability) _origins.Move(oldIndex, newIndex, _count);
        if (oldIndex < newIndex) Array.Copy(_items, oldIndex + 1, _items, oldIndex, newIndex - oldIndex);
        else Array.Copy(_items, newIndex, _items, newIndex + 1, oldIndex - newIndex);
        _items[newIndex] = item;
        UpdatePendingAfterMove(oldIndex, newIndex);
        _version++;
        RaiseListChanged(new(ListChangedType.ItemMoved, newIndex, oldIndex));
    }

    /// <inheritdoc/>
    public bool Remove(T item) {
        int index = IndexOf(item);
        if (index < 0) return false;
        RemoveAt(index);
        return true;
    }

    /// <inheritdoc/>
    public void RemoveAt(int index) {
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        if (_binding?.Pending is PendingNew pending && pending.Index == index) { CancelPendingNewCore(pending); return; }
        RemoveAtCore(index, true);
    }

    /// <inheritdoc/>
    public T RestoreAt(int removedIndex, int index = -1) {
        if ((uint)removedIndex >= (uint)_removedCount) throw new ArgumentOutOfRangeException(nameof(removedIndex));
        if (index < 0) index = _count;
        if ((uint)index > (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        EndPendingNew();
        T item = RemoveRemovedAt(removedIndex);
        InsertCore(index, item, false);
        return item;
    }

    /// <inheritdoc/>
    public bool Restore(T item, int index = -1) {
        int removedIndex = IndexOfRemoved(item);
        if (removedIndex < 0) return false;
        RestoreAt(removedIndex, index);
        return true;
    }

    /// <inheritdoc/>
    public void Clear() {
        if (_count == 0) return;
        if (_binding?.Pending is PendingNew pending) CancelPendingNewCore(pending, notify: false);

        for (int i = 0; i < _count; i++) {
            T item = _items[i];
            Unsubscribe(item);
            if (!IsAddedUnchecked(i)) TrackRemoved(item);
        }
        Array.Clear(_items, 0, _count);
        _count = 0;
        _origins.Reset();
        _binding?.Subscriptions?.Clear();
        _version++;
        RaiseListChanged(new(ListChangedType.Reset, -1));
    }

    /// <inheritdoc/>
    public void CommitRemoved() {
        if (_removedCount == 0) return;
        Array.Clear(_removed, 0, _removedCount);
        _removedCount = 0;
        _version++;
    }

    // Accepts structural changes that are owned by the list. For IHasOriginal rows, added/original
    // state belongs to the item and is intentionally never duplicated or overwritten here.
    /// <inheritdoc/>
    public void CommitStructuralChanges() {
        EndPendingNew();
        bool changed = _removedCount != 0;
        if (_removedCount != 0) { Array.Clear(_removed, 0, _removedCount); _removedCount = 0; }
        if (!TrackingItemCapabilities<T>.HasOriginalCapability && _origins.AddedCount(_count) != 0) {
            _origins.Reset();
            changed = true;
        }
        if (changed) _version++;
    }

    // Commits item edits first, then accepts list-owned structural state. A generated new item
    // acquires its original through CommitEdit(), so no extra "persisted" state is required.
    /// <inheritdoc/>
    public bool CommitChanges() {
        EndPendingNew();
        bool changed = false;
        if (TrackingItemCapabilities<T>.IsEditable)
            for (int i = 0; i < _count; i++) changed |= TrackingItemCapabilities<T>.CommitEdit(_items[i]);

        bool structural = _removedCount != 0 || (!TrackingItemCapabilities<T>.HasOriginalCapability && _origins.AddedCount(_count) != 0);
        if (_removedCount != 0) { Array.Clear(_removed, 0, _removedCount); _removedCount = 0; }
        if (!TrackingItemCapabilities<T>.HasOriginalCapability) _origins.Reset();
        if (changed || structural) _version++;
        return changed || structural;
    }

    /// <inheritdoc/>
    public bool CancelEdits() {
        bool changed = false;
        if (TrackingItemCapabilities<T>.IsEditable)
            for (int i = 0; i < _count; i++) changed |= TrackingItemCapabilities<T>.CancelEdit(_items[i]);
        return changed;
    }

    /// <inheritdoc/>
    public int IndexOf(T item) => IndexOf(_items, _count, item);
    /// <inheritdoc/>
    public bool Contains(T item) => IndexOf(item) >= 0;

    /// <inheritdoc/>
    public void CopyTo(T[] array, int arrayIndex) {
        ArgumentNullException.ThrowIfNull(array);
        if ((uint)arrayIndex > (uint)array.Length || array.Length - arrayIndex < _count) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        Array.Copy(_items, 0, array, arrayIndex, _count);
    }

    /// <inheritdoc/>
    public int CopyAddedTo(Span<T> destination) {
        int addedCount = AddedCount;
        if (destination.Length < addedCount) throw new ArgumentException("Destination is too small.", nameof(destination));
        if (addedCount == _count) { _items.AsSpan(0, _count).CopyTo(destination); return _count; }
        int count = 0;
        for (int i = 0; i < _count; i++) if (IsAddedUnchecked(i)) destination[count++] = _items[i];
        return count;
    }

    /// <inheritdoc/>
    public int EnsureCapacity(int capacity) {
        if (capacity <= _items.Length) return _items.Length;
        int next = _items.Length == 0 ? 4 : _items.Length * 2;
        if ((uint)next > 0X7FEFFFFF) next = 0X7FEFFFFF;
        if (next < capacity) next = capacity;
        Array.Resize(ref _items, next);
        return next;
    }

    /// <inheritdoc/>
    public void TrimExcess() {
        if (_count != _items.Length) Array.Resize(ref _items, _count);
        if (_removedCount != _removed.Length) Array.Resize(ref _removed, _removedCount);
        if (!TrackingItemCapabilities<T>.HasOriginalCapability) _origins.Trim(_count);
    }

    /// <inheritdoc/>
    public T AddNew() {
        EndPendingNew();
        BindingState state = Binding;
        var args = new AddingNewEventArgs();
        state.AddingNew?.Invoke(this, args);
        T item = args.NewObject is null ? CreateNewItem(state) : Cast(args.NewObject);
        AddCore(item, out AddDelta delta);
        state.Pending = new(_count - 1, delta);
        return item;
    }

    /// <inheritdoc/>
    public void CancelNew(int itemIndex) {
        if (_binding?.Pending is not PendingNew pending || pending.Index != itemIndex) return;
        T item = _items[itemIndex];
        if (_binding.CancelNew?.Invoke(item) == false) { _binding.Pending = null; return; }
        CancelPendingNewCore(pending);
    }

    /// <inheritdoc/>
    public void EndNew(int itemIndex) {
        if (_binding?.Pending is PendingNew pending && pending.Index == itemIndex) _binding.Pending = null;
    }

    internal void AddInitial(T item) {
        EnsureCapacity(_count + 1);
        _items[_count] = item;
        if (!TrackingItemCapabilities<T>.HasOriginalCapability) _origins.Insert(_count, _count, false);
        _count++;
    }

    internal void ConfigureBinding(Func<T>? newItemFactory = null, Func<PropertyDescriptorCollection>? propertiesFactory = null, string? listName = null) {
        if (newItemFactory is null && propertiesFactory is null && listName is null) return;
        BindingState state = Binding;
        if (newItemFactory is not null) state.Factory = newItemFactory;
        if (propertiesFactory is not null) state.PropertiesFactory = propertiesFactory;
        if (listName is not null) state.ListName = listName;
    }

    private void Materialize(IEnumerable<T> items, int initialCapacity) {
        if (items is ICollection<T> collection) {
            int count = collection.Count;
            if (count == 0) return;
            int capacity = Math.Max(count, initialCapacity);
            if (_items.Length != capacity) _items = new T[capacity];
            collection.CopyTo(_items, 0);
            _count = count;
            return;
        }
        if (items.TryGetNonEnumeratedCount(out int knownCount) && knownCount > _items.Length) _items = new T[Math.Max(knownCount, initialCapacity)];
        foreach (T item in items) AddInitial(item);
    }

    private void AddCore(T item, out AddDelta delta) {
        delta = TrackAddition(item);
        InsertCore(_count, item, delta.IsAdded);
    }

    private AddDelta TrackAddition(T item) {
        if (TrackingItemCapabilities<T>.HasOriginalCapability) {
            if (!TrackingItemCapabilities<T>.HasOriginal(item)) return AddDelta.Added;
            int restored = IndexOfRemoved(item);
            return restored < 0 ? AddDelta.Existing : AddDelta.Restored(RemoveRemovedAt(restored), restored);
        }

        int removedIndex = IndexOfRemoved(item);
        return removedIndex < 0 ? AddDelta.Added : AddDelta.Restored(RemoveRemovedAt(removedIndex), removedIndex);
    }

    private void UndoAddition(in AddDelta delta) {
        if (delta.IsRestored) InsertRemoved(Math.Min(delta.RemovedIndex, _removedCount), delta.RemovedItem);
    }

    private void SetItem(int index, T item) {
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        if (_binding?.Pending is PendingNew pending) {
            if (pending.Index == index) { SetPendingItem(index, item, pending); return; }
            EndPendingNew();
        }

        T previous = _items[index];
        if (!typeof(T).IsValueType && ReferenceEquals(previous, item)) return;
        bool previousAdded = IsAddedUnchecked(index);

        if (!TrackingItemCapabilities<T>.HasOriginalCapability && Equals(previous, item)) {
            _origins.Replace(index, _count, previousAdded);
            ReplaceItem(index, previous, item);
            return;
        }

        if (!previousAdded) TrackRemoved(previous);
        AddDelta delta = TrackAddition(item);
        if (!TrackingItemCapabilities<T>.HasOriginalCapability) _origins.Replace(index, _count, delta.IsAdded);
        ReplaceItem(index, previous, item);
    }

    private void SetPendingItem(int index, T item, PendingNew pending) {
        T previous = _items[index];
        if (!typeof(T).IsValueType && ReferenceEquals(previous, item)) return;

        if (!TrackingItemCapabilities<T>.HasOriginalCapability && Equals(previous, item)) {
            _origins.Replace(index, _count, pending.Delta.IsAdded);
            ReplaceItem(index, previous, item);
            return;
        }

        UndoAddition(pending.Delta);
        pending.Delta = TrackAddition(item);
        if (!TrackingItemCapabilities<T>.HasOriginalCapability) _origins.Replace(index, _count, pending.Delta.IsAdded);
        ReplaceItem(index, previous, item);
    }

    private void ReplaceItem(int index, T previous, T item) {
        Unsubscribe(previous);
        _items[index] = item;
        Subscribe(item);
        _version++;
        RaiseListChanged(new(ListChangedType.ItemChanged, index));
    }

    private void InsertCore(int index, T item, bool added) {
        EnsureCapacity(_count + 1);
        if (index < _count) Array.Copy(_items, index, _items, index + 1, _count - index);
        _items[index] = item;
        if (!TrackingItemCapabilities<T>.HasOriginalCapability) _origins.Insert(index, _count, added);
        _count++;
        if (_binding?.Pending is PendingNew pending && pending.Index >= index) pending.Index++;
        _version++;
        Subscribe(item);
        RaiseListChanged(new(ListChangedType.ItemAdded, index));
    }

    private void RemoveAtCore(int index, bool trackRemoval, bool notify = true) {
        T item = _items[index];
        bool added = IsAddedUnchecked(index);
        if (!added && trackRemoval) TrackRemoved(item);
        Unsubscribe(item);
        RemoveActiveAt(index);
        if (notify) RaiseListChanged(new(ListChangedType.ItemDeleted, index));
    }

    private void RemoveActiveAt(int index) {
        if (!TrackingItemCapabilities<T>.HasOriginalCapability) _origins.Remove(index, _count);
        _count--;
        if (index < _count) Array.Copy(_items, index + 1, _items, index, _count - index);
        _items[_count] = default!;
        if (_binding?.Pending is PendingNew pending) {
            if (pending.Index == index) _binding.Pending = null;
            else if (pending.Index > index) pending.Index--;
        }
        _version++;
    }

    private void CancelPendingNewCore(PendingNew pending, bool notify = true) {
        int index = pending.Index;
        T item = _items[index];
        UndoAddition(pending.Delta);
        Unsubscribe(item);
        _binding!.Pending = null;
        RemoveActiveAt(index);
        if (notify) RaiseListChanged(new(ListChangedType.ItemDeleted, index));
    }

    private void UpdatePendingAfterMove(int oldIndex, int newIndex) {
        if (_binding?.Pending is not PendingNew pending) return;
        if (pending.Index == oldIndex) pending.Index = newIndex;
        else if (oldIndex < newIndex && pending.Index > oldIndex && pending.Index <= newIndex) pending.Index--;
        else if (oldIndex > newIndex && pending.Index >= newIndex && pending.Index < oldIndex) pending.Index++;
    }

    private void EndPendingNew() { if (_binding is not null) _binding.Pending = null; }

    private void TrackRemoved(T item) {
        EnsureDeltaCapacity(ref _removed, _removedCount + 1);
        _removed[_removedCount++] = item;
    }

    private void InsertRemoved(int index, T item) {
        EnsureDeltaCapacity(ref _removed, _removedCount + 1);
        if (index < _removedCount) Array.Copy(_removed, index, _removed, index + 1, _removedCount - index);
        _removed[index] = item;
        _removedCount++;
    }

    private T RemoveRemovedAt(int index) => RemoveAt(_removed, ref _removedCount, index);
    private int IndexOfRemoved(T item) => IndexOf(_removed, _removedCount, item);
    private bool Equals(T left, T right) => (_comparer ?? EqualityComparer<T>.Default).Equals(left, right);

    private int IndexOf(T[] items, int count, T item) {
        IEqualityComparer<T> comparer = _comparer ?? EqualityComparer<T>.Default;
        for (int i = 0; i < count; i++) if (comparer.Equals(item, items[i])) return i;
        return -1;
    }

    private static T RemoveAt(T[] items, ref int count, int index) {
        T item = items[index];
        count--;
        if (index < count) Array.Copy(items, index + 1, items, index, count - index);
        items[count] = default!;
        return item;
    }

    private static void EnsureDeltaCapacity(ref T[] items, int capacity) {
        if (capacity <= items.Length) return;
        int next = items.Length == 0 ? 4 : items.Length * 2;
        if (next < capacity) next = capacity;
        Array.Resize(ref items, next);
    }

    private BindingState Binding => _binding ??= new();

    private T CreateNewItem(BindingState state) {
        if (state.Factory is Func<T> factory) return factory();
        if (typeof(T).IsValueType) return default!;
        ConstructorInfo? constructor = NewItemConstructorCache.Value;
        if (constructor is null) throw new NotSupportedException($"{typeof(T)} has no public parameterless constructor and no new-item factory was supplied.");
        return Cast(constructor.Invoke(null));
    }

    private static bool TryCast(object? value, out T item) {
        if (value is T typed) { item = typed; return true; }
        if (value is null && default(T) is null) { item = default!; return true; }
        item = default!;
        return false;
    }

    private static T Cast(object? value) {
        if (TryCast(value, out T item)) return item;
        throw new ArgumentException($"Expected an instance assignable to {typeof(T)}, got {(value is null ? "<null>" : value.GetType())}.");
    }

    private PropertyDescriptorCollection Properties {
        get {
            BindingState state = Binding;
            return state.Properties ??= state.PropertiesFactory?.Invoke() ?? TypeDescriptor.GetProperties(typeof(T));
        }
    }

    private void RaiseListChanged(ListChangedEventArgs args) => _binding?.ListChanged?.Invoke(this, args);
    private void SubscribeInitial(BindingState state) { for (int i = 0; i < _count; i++) Subscribe(_items[i], state); }
    private void Subscribe(T item) { if (_binding?.ListChanged is not null) Subscribe(item, _binding); }

    private void Subscribe(T item, BindingState state) {
        if (typeof(T).IsValueType || item is not INotifyPropertyChanged changed) return;
        state.Subscriptions ??= new(ReferenceEqualityComparer.Instance);
        if (state.Subscriptions.TryGetValue(changed, out int count)) { state.Subscriptions[changed] = count + 1; return; }
        state.Subscriptions.Add(changed, 1);
        changed.PropertyChanged += ItemPropertyChanged;
    }

    private void Unsubscribe(T item) {
        BindingState? state = _binding;
        if (state?.Subscriptions is null || item is not INotifyPropertyChanged changed || !state.Subscriptions.TryGetValue(changed, out int count)) return;
        if (count > 1) { state.Subscriptions[changed] = count - 1; return; }
        state.Subscriptions.Remove(changed);
        changed.PropertyChanged -= ItemPropertyChanged;
    }

    private void UnsubscribeAll(BindingState state) {
        if (state.Subscriptions is null) return;
        foreach (INotifyPropertyChanged item in state.Subscriptions.Keys) item.PropertyChanged -= ItemPropertyChanged;
        state.Subscriptions = null;
    }

    private void ItemPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (sender is null || _binding?.ListChanged is null) return;
        PropertyDescriptor? descriptor = string.IsNullOrEmpty(e.PropertyName) ? null : Properties[e.PropertyName];
        for (int i = 0; i < _count; i++) if (ReferenceEquals(sender, _items[i])) _binding.ListChanged(this, new(ListChangedType.ItemChanged, i, descriptor));
    }

    /// <inheritdoc/>
    public Enumerator GetEnumerator() => new(this);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Enumerates active list items.</summary>
    public struct Enumerator : IEnumerator<T> {
        private readonly TrackingList<T> _list;
        private readonly int _version;
        private int _index;
        internal Enumerator(TrackingList<T> list) { _list = list; _version = list._version; _index = -1; }
        /// <inheritdoc/>
        public readonly T Current => _list._items[_index];
        object IEnumerator.Current => Current!;
        /// <inheritdoc/>
        public bool MoveNext() {
            if (_version != _list._version) throw new InvalidOperationException("Collection was modified during enumeration.");
            return ++_index < _list._count;
        }
        /// <inheritdoc/>
        public void Reset() => throw new NotSupportedException();
        /// <inheritdoc/>
        public readonly void Dispose() { }
    }

    /// <summary>Exposes items added after list creation.</summary>
    public readonly struct AddedCollection : IReadOnlyList<T> {
        private readonly TrackingList<T> _list;
        internal AddedCollection(TrackingList<T> list) => _list = list;
        /// <inheritdoc/>
        public int Count => _list.AddedCount;
        /// <inheritdoc/>
        public T this[int index] {
            get {
                int count = _list.AddedCount;
                if ((uint)index >= (uint)count) throw new ArgumentOutOfRangeException(nameof(index));
                if (count == _list._count) return _list._items[index];
                for (int i = 0; i < _list._count; i++) if (_list.IsAddedUnchecked(i) && index-- == 0) return _list._items[i];
                throw new InvalidOperationException("Added provenance changed while the list was being read.");
            }
        }
        /// <inheritdoc/>
        public AddedEnumerator GetEnumerator() => new(_list);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Enumerates added items.</summary>
    public struct AddedEnumerator : IEnumerator<T> {
        private readonly TrackingList<T> _list;
        private readonly int _version;
        private int _index;
        private T _current;
        internal AddedEnumerator(TrackingList<T> list) { _list = list; _version = list._version; _index = -1; _current = default!; }
        /// <inheritdoc/>
        public readonly T Current => _current;
        object IEnumerator.Current => Current!;
        /// <inheritdoc/>
        public bool MoveNext() {
            if (_version != _list._version) throw new InvalidOperationException("Collection was modified during enumeration.");
            while (++_index < _list._count)
                if (_list.IsAddedUnchecked(_index)) { _current = _list._items[_index]; return true; }
            _current = default!;
            return false;
        }
        /// <inheritdoc/>
        public void Reset() => throw new NotSupportedException();
        /// <inheritdoc/>
        public readonly void Dispose() { }
    }

    /// <summary>Exposes items removed after list creation.</summary>
    public readonly struct RemovedCollection : IReadOnlyList<T> {
        private readonly TrackingList<T> _list;
        internal RemovedCollection(TrackingList<T> list) => _list = list;
        /// <inheritdoc/>
        public int Count => _list._removedCount;
        /// <inheritdoc/>
        public T this[int index] {
            get {
                if ((uint)index >= (uint)_list._removedCount) throw new ArgumentOutOfRangeException(nameof(index));
                return _list._removed[index];
            }
        }
        /// <inheritdoc/>
        public RemovedEnumerator GetEnumerator() => new(_list);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Enumerates removed items.</summary>
    public struct RemovedEnumerator : IEnumerator<T> {
        private readonly TrackingList<T> _list;
        private readonly int _version;
        private int _index;
        internal RemovedEnumerator(TrackingList<T> list) { _list = list; _version = list._version; _index = -1; }
        /// <inheritdoc/>
        public readonly T Current => _list._removed[_index];
        object IEnumerator.Current => Current!;
        /// <inheritdoc/>
        public bool MoveNext() {
            if (_version != _list._version) throw new InvalidOperationException("Collection was modified during enumeration.");
            return ++_index < _list._removedCount;
        }
        /// <inheritdoc/>
        public void Reset() => throw new NotSupportedException();
        /// <inheritdoc/>
        public readonly void Dispose() { }
    }

    private static class NewItemConstructorCache {
        internal static readonly ConstructorInfo? Value = typeof(T).IsValueType ? null : typeof(T).GetConstructor(Type.EmptyTypes);
    }

    private sealed class BindingState {
        internal bool AllowEdit = true;
        internal Func<T>? Factory;
        internal Func<T, bool>? CancelNew;
        internal AddingNewEventHandler? AddingNew;
        internal ListChangedEventHandler? ListChanged;
        internal Dictionary<INotifyPropertyChanged, int>? Subscriptions;
        internal PropertyDescriptorCollection? Properties;
        internal Func<PropertyDescriptorCollection>? PropertiesFactory;
        internal string? ListName;
        internal PendingNew? Pending;
    }

    private sealed class PendingNew(int index, AddDelta delta) {
        internal int Index = index;
        internal AddDelta Delta = delta;
    }

    private readonly struct AddDelta(byte kind, T removedItem, int removedIndex) {
        private const byte ExistingKind = 0, AddedKind = 1, RestoredKind = 2;
        internal readonly T RemovedItem = removedItem;
        internal readonly int RemovedIndex = removedIndex;
        internal bool IsAdded => kind == AddedKind;
        internal bool IsRestored => kind == RestoredKind;
        internal static AddDelta Existing => new(ExistingKind, default!, -1);
        internal static AddDelta Added => new(AddedKind, default!, -1);
        internal static AddDelta Restored(T item, int index) => new(RestoredKind, item, index);
    }

    PropertyDescriptorCollection ITypedList.GetItemProperties(PropertyDescriptor[]? listAccessors) => Properties;
    string ITypedList.GetListName(PropertyDescriptor[]? listAccessors) => _binding?.ListName ?? typeof(T).Name;

    bool IBindingList.AllowNew => CanAddNew;
    bool IBindingList.AllowEdit => AllowEdit;
    bool IBindingList.AllowRemove => true;
    bool IBindingList.SupportsChangeNotification => true;
    bool IBindingList.SupportsSearching => false;
    bool IBindingList.SupportsSorting => false;
    bool IBindingList.IsSorted => false;
    PropertyDescriptor? IBindingList.SortProperty => null;
    ListSortDirection IBindingList.SortDirection => ListSortDirection.Ascending;
    object IBindingList.AddNew() => AddNew()!;
    void IBindingList.AddIndex(PropertyDescriptor property) { }
    void IBindingList.ApplySort(PropertyDescriptor property, ListSortDirection direction) => throw new NotSupportedException();
    int IBindingList.Find(PropertyDescriptor property, object key) => throw new NotSupportedException();
    void IBindingList.RemoveIndex(PropertyDescriptor property) { }
    void IBindingList.RemoveSort() => throw new NotSupportedException();

    bool IList.IsFixedSize => false;
    bool IList.IsReadOnly => false;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;
    object? IList.this[int index] { get => this[index]; set => this[index] = Cast(value); }
    int IList.Add(object? value) { Add(Cast(value)); return _count - 1; }
    bool IList.Contains(object? value) => TryCast(value, out T item) && Contains(item);
    int IList.IndexOf(object? value) => TryCast(value, out T item) ? IndexOf(item) : -1;
    void IList.Insert(int index, object? value) => Insert(index, Cast(value));
    void IList.Remove(object? value) { if (TryCast(value, out T item)) Remove(item); }
    void ICollection.CopyTo(Array array, int index) => Array.Copy(_items, 0, array, index, _count);
}
