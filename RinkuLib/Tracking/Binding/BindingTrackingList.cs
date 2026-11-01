using System.Collections;
using System.ComponentModel;

namespace Rinku.Tracking.Binding;

/// <summary>Provides component model binding over a tracking list.</summary>
public class BindingTrackingList<T> : TrackingList<T>, IBindingList, ITypedList, ICancelAddNew, IList
{
    private readonly Dictionary<INotifyPropertyChanged, int> _subscriptions = new(ReferenceEqualityComparer.Instance);
    private ListChangedEventHandler? _listChanged;
    private AddingNewEventHandler? _addingNew;
    private PropertyDescriptorCollection? _properties;
    private string? _listName;
    private int _pendingIndex = -1;
    private Func<object, T>? _baselineFactory;
    private IDisposable? _sourceBinding;
    private AddDelta _pendingDelta;

    /// <summary>Creates an empty binding tracking list.</summary>
    public BindingTrackingList(int capacity = 0, IEqualityComparer<T>? comparer = null, Func<T>? newItemFactory = null)
        : base(capacity, comparer, newItemFactory) { }

    /// <summary>Creates a binding tracking list with baseline items.</summary>
    public BindingTrackingList(IEnumerable<T> items, int initialCapacity = 0, IEqualityComparer<T>? comparer = null, Func<T>? newItemFactory = null)
        : base(items, initialCapacity, comparer, newItemFactory) { }

    internal void ConfigureBaselineFactory<TOriginal>(Func<TOriginal, T> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _baselineFactory = item => factory((TOriginal)item);
    }

    internal void BindSource<TOriginal>(IEnumerable<TOriginal> originals)
    {
        _sourceBinding?.Dispose();
        _sourceBinding = originals is IList<TOriginal> { IsReadOnly: false } target && (target is not IList list || !list.IsFixedSize) ? this.BindBack(target) : null;
    }

    /// <summary>Replaces the accepted baseline using the original-item factory configured during materialization.</summary>
    /// <remarks>Automatic bind-back follows the new writable source list. Explicitly registered bindings remain unchanged.</remarks>
    public void ResetBaseline<TOriginal>(IEnumerable<TOriginal> originals)
    {
        ArgumentNullException.ThrowIfNull(originals);
        Func<object, T> factory = _baselineFactory ?? throw new InvalidOperationException("This binding list was not materialized from original items.");
        base.ResetBaseline(originals.Select(item => factory(item!)));
        BindSource(originals);
    }

    /// <inheritdoc/>
    public bool AllowEdit => true;
    /// <inheritdoc/>
    public bool AllowNew => CanAddNew || _addingNew is not null;
    /// <inheritdoc/>
    public bool AllowRemove => true;
    /// <inheritdoc/>
    public bool SupportsChangeNotification => true;
    /// <inheritdoc/>
    public bool SupportsSearching => false;
    /// <inheritdoc/>
    public bool SupportsSorting => false;
    /// <inheritdoc/>
    public bool IsSorted => false;
    /// <inheritdoc/>
    public PropertyDescriptor? SortProperty => null;
    /// <inheritdoc/>
    public ListSortDirection SortDirection => ListSortDirection.Ascending;

    /// <inheritdoc/>
    public event ListChangedEventHandler? ListChanged
    {
        add
        {
            bool first = _listChanged is null;
            _listChanged += value;
            if (first) SubscribeAll();
        }
        remove
        {
            _listChanged -= value;
            if (_listChanged is null) UnsubscribeAll();
        }
    }

    /// <inheritdoc/>
    public event AddingNewEventHandler? AddingNew
    {
        add => _addingNew += value;
        remove => _addingNew -= value;
    }

    /// <summary>Sets the properties and name exposed through typed list access.</summary>
    public void Configure(PropertyDescriptorCollection properties, string? listName = null)
    {
        ArgumentNullException.ThrowIfNull(properties);
        _properties = properties;
        _listName = listName;
    }

    /// <inheritdoc/>
    public override void Add(T item)
    {
        EndPending();
        base.Add(item);
    }

    /// <inheritdoc/>
    public override void Insert(int index, T item)
    {
        EndPending();
        base.Insert(index, item);
    }

    /// <inheritdoc/>
    public override bool Remove(T item)
    {
        EndPending();
        return base.Remove(item);
    }

    /// <inheritdoc/>
    public override void RemoveAt(int index)
    {
        EndPending();
        base.RemoveAt(index);
    }

    /// <inheritdoc/>
    public override void Clear()
    {
        EndPending();
        base.Clear();
    }

    /// <inheritdoc/>
    public override T this[int index]
    {
        get => base[index];
        set
        {
            EndPending();
            base[index] = value;
        }
    }

    /// <inheritdoc/>
    public override T AddNew()
    {
        EndPending();
        var args = new AddingNewEventArgs();
        _addingNew?.Invoke(this, args);
        T item;
        if (args.NewObject is null)
        {
            if (!CanAddNew) throw new NotSupportedException($"{typeof(T)} has no public parameterless constructor and no new-item factory was supplied.");
            item = CreateNewItem();
        }
        else
        {
            item = Cast(args.NewObject);
        }

        _pendingIndex = Count;
        _pendingDelta = AddTrackedItem(_pendingIndex, item);
        return item;
    }

    object IBindingList.AddNew()
    {
        object? item = AddNew();
        return item ?? throw new InvalidOperationException("AddNew returned null.");
    }

    /// <inheritdoc/>
    public void CancelNew(int itemIndex)
    {
        if (itemIndex != _pendingIndex) return;
        CancelTrackedAddition(itemIndex, _pendingDelta);
        _pendingIndex = -1;
    }

    /// <inheritdoc/>
    public void EndNew(int itemIndex)
    {
        if (itemIndex == _pendingIndex) _pendingIndex = -1;
    }

    /// <inheritdoc/>
    protected override void OnAddedConfirmed(int index, T item)
    {
        if (_pendingIndex == index) _pendingIndex = -1;
        base.OnAddedConfirmed(index, item);
    }

    /// <inheritdoc/>
    protected override void OnChanged(TrackingListChange<T> change)
    {
        base.OnChanged(change);
        switch (change.Kind)
        {
            case TrackingListChangeKind.Add:
                if (change.Item is T added) Subscribe(added);
                if (_pendingIndex >= change.Index && _pendingIndex >= 0 && change.Index != _pendingIndex) _pendingIndex++;
                Raise(new ListChangedEventArgs(ListChangedType.ItemAdded, change.Index));
                break;
            case TrackingListChangeKind.Remove:
                if (change.OldItem is T removed) Unsubscribe(removed);
                if (_pendingIndex == change.Index) _pendingIndex = -1;
                else if (_pendingIndex > change.Index) _pendingIndex--;
                Raise(new ListChangedEventArgs(ListChangedType.ItemDeleted, change.Index));
                break;
            case TrackingListChangeKind.Replace:
                if (change.OldItem is T previous) Unsubscribe(previous);
                if (change.Item is T replacement) Subscribe(replacement);
                Raise(new ListChangedEventArgs(ListChangedType.ItemChanged, change.Index));
                break;
            case TrackingListChangeKind.Move:
                if (_pendingIndex == change.OldIndex) _pendingIndex = change.Index;
                else if (change.OldIndex < change.Index && _pendingIndex > change.OldIndex && _pendingIndex <= change.Index) _pendingIndex--;
                else if (change.OldIndex > change.Index && _pendingIndex >= change.Index && _pendingIndex < change.OldIndex) _pendingIndex++;
                Raise(new ListChangedEventArgs(ListChangedType.ItemMoved, change.Index, change.OldIndex));
                break;
            case TrackingListChangeKind.Reset:
                UnsubscribeAll();
                if (_listChanged is not null) SubscribeAll();
                _pendingIndex = -1;
                Raise(new ListChangedEventArgs(ListChangedType.Reset, -1));
                break;
        }
    }

    /// <summary>Releases binding subscriptions and list resources.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sourceBinding?.Dispose();
            _sourceBinding = null;
            UnsubscribeAll();
            _listChanged = null;
            _addingNew = null;
        }
        base.Dispose(disposing);
    }

    private void EndPending() => _pendingIndex = -1;

    private void SubscribeAll()
    {
        for (int i = 0; i < Count; i++) Subscribe(this[i]);
    }

    private void UnsubscribeAll()
    {
        foreach (INotifyPropertyChanged item in _subscriptions.Keys) item.PropertyChanged -= ItemChanged;
        _subscriptions.Clear();
    }

    private void Subscribe(T item)
    {
        if (_listChanged is null || item is not INotifyPropertyChanged notify) return;
        if (_subscriptions.TryGetValue(notify, out int count))
            _subscriptions[notify] = count + 1;
        else
        {
            _subscriptions.Add(notify, 1);
            notify.PropertyChanged += ItemChanged;
        }
    }

    private void Unsubscribe(T item)
    {
        if (item is not INotifyPropertyChanged notify || !_subscriptions.TryGetValue(notify, out int count)) return;
        if (count > 1)
            _subscriptions[notify] = count - 1;
        else
        {
            _subscriptions.Remove(notify);
            notify.PropertyChanged -= ItemChanged;
        }
    }

    private void ItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        int index = -1;
        for (int i = 0; i < Count; i++)
        {
            if (!ReferenceEquals(this[i], sender)) continue;
            index = i;
            break;
        }
        if (index < 0) return;
        PropertyDescriptor? property = e.PropertyName is null ? null : Properties.Find(e.PropertyName, true);
        Raise(new ListChangedEventArgs(ListChangedType.ItemChanged, index, property));
    }

    private void Raise(ListChangedEventArgs e) => _listChanged?.Invoke(this, e);

    private PropertyDescriptorCollection Properties => _properties ??= TypeDescriptor.GetProperties(typeof(T));
    PropertyDescriptorCollection ITypedList.GetItemProperties(PropertyDescriptor[]? listAccessors) => Properties;
    string ITypedList.GetListName(PropertyDescriptor[]? listAccessors) => _listName ?? typeof(T).Name;

    private static T Cast(object? value)
        => value is T item ? item : throw new ArgumentException($"Value must be assignable to {typeof(T)}.");

    object? IList.this[int index] { get => this[index]; set => this[index] = Cast(value); }
    bool IList.IsReadOnly => false;
    bool IList.IsFixedSize => false;
    int IList.Add(object? value) { Add(Cast(value)); return Count - 1; }
    bool IList.Contains(object? value) => value is T item && Contains(item);
    int IList.IndexOf(object? value) => value is T item ? IndexOf(item) : -1;
    void IList.Insert(int index, object? value) => Insert(index, Cast(value));
    void IList.Remove(object? value) { if (value is T item) Remove(item); }
    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        for (int i = 0; i < Count; i++) array.SetValue(this[i], index + i);
    }
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;

    void IBindingList.AddIndex(PropertyDescriptor property) { }
    void IBindingList.ApplySort(PropertyDescriptor property, ListSortDirection direction) => throw new NotSupportedException();
    int IBindingList.Find(PropertyDescriptor property, object key) => -1;
    void IBindingList.RemoveIndex(PropertyDescriptor property) { }
    void IBindingList.RemoveSort() => throw new NotSupportedException();
}
