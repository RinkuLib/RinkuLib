using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace RinkuLib.Tracking;

/// <summary>
/// A collection that track removal and revival and uses <typeparamref name="TEditItem"/> to manage editing state
/// </summary>
public abstract class TrackingEditListBase<TOg, TEdit, TEditItem>(IEnumerable<TEditItem> items, int initialCapacity = 4)
    : TrackingList<TOg, TEditItem>(items, items.TryGetNonEnumeratedCount(out var count) && initialCapacity < count ? count : initialCapacity),
    IList<TEdit>, IReadOnlyList<TEdit>, IEditableList<TOg, TEdit>, IList, IBindingList, ICancelAddNew, IAddSetNewItem<TEdit> where TEditItem : IEditableItem<TEdit>, ITrackingItem<TOg> {
    /// <summary>Create a new instance of the edit item from an instance of <typeparamref name="TEdit"/></summary>
    public abstract TEditItem MakeNewEditItem(TEdit newItem);
    /// <inheritdoc/>
    public TEdit this[int index] {
        get => GetValue(index);
        set {
            Set(index, MakeNewEditItem(value));
            OnListChanged(new ListChangedEventArgs(ListChangedType.ItemChanged, index));
        }
    }
    private TEdit GetValue(int index) => Get(index).CurrentValue ?? throw new Exception($"No values were available to display at index {index}");
    /// <inheritdoc/>
    public bool HasEditValue(int index, [MaybeNullWhen(false)] out TEdit editValue) {
        ref var item = ref Get(index);
        editValue = item.EditableValue;
        return item.IsEditing;
    }
    /// <inheritdoc/>
    public bool EnsureEditing(int index) => EnsureEditing(index, out _);
    /// <inheritdoc/>
    public bool EnsureEditing(int index, [MaybeNullWhen(false)] out TEdit editValue) {
        ref var item = ref Get(index);
        if (item.IsEditing) {
            return item.EnsureIsEditing(out editValue);
        }
        var res = item.EnsureIsEditing(out editValue);
        if (res) {
            OnListChanged(new ListChangedEventArgs(ListChangedType.ItemChanged, index));
        }
        return res;
    }
    /// <inheritdoc/>
    public bool IsEditing(int index) => Get(index).IsEditing;
    /// <summary>Check if items are equals</summary>
    protected virtual bool ItemEquals(TEdit item1, TEdit? item2)
        => EqualityComparer<TEdit>.Default.Equals(item1, item2);
    /// <inheritdoc/>
    public virtual bool CommitEdit(int index) => Get(index).CommitEdit();
    /// <inheritdoc/>
    public virtual bool CancelEdit(int index, bool canRemove = false) {
        ref var item = ref Get(index);
        if (item.CancelEdit()) {
            return true;
        }
        if (canRemove && !item.HasOriginal(out _)) {
            RemoveAt(index);
            return true;
        }
        return false;
    }
    /// <inheritdoc/>
    public virtual Task<bool> CommitEditAsync(int index) => Get(index).CommitEditAsync();
    /// <inheritdoc/>
    public virtual Task<bool> CancelEditAsync(int index) => Get(index).CancelEditAsync();
    /// <inheritdoc/>
    public bool IsReadOnly => false;
    /// <inheritdoc/>
    public bool IsFixedSize => false;
    /// <inheritdoc/>
    public bool IsSynchronized => false;
    /// <inheritdoc/>
    public object SyncRoot => this;
    /// <inheritdoc/>
    public virtual bool HasChanges {
        get {
            for (int i = 0; i < Count; i++) {
                if (Get(i).IsEditing) {
                    return true;
                }
            }

            return false;
        }
    }
    /// <inheritdoc/>
    public event ListChangedEventHandler? ListChanged;
    private Func<TEdit>? _addNewFactory;
    /// <inheritdoc/>
    public void SetNewItemFactory(Func<TEdit> factory) => _addNewFactory = factory;
    private int _addNewPos = -1;
    /// <summary>
    /// Raises the ListChanged event.
    /// </summary>
    public virtual bool RaiseListChangedEvents { get; set; } = true;
    /// <summary>
    /// Raises the ListChanged event. You should also call this from your base TrackingList 
    /// overrides (like Remove, Insert, Clear) to fully support UI bindings.
    /// </summary>
    protected virtual void OnListChanged(ListChangedEventArgs e) {
        if (RaiseListChangedEvents)
            ListChanged?.Invoke(this, e);
    }
    /// <inheritdoc/>
    public virtual void CancelNew(int itemIndex) {
        if (_addNewPos >= 0 && _addNewPos == itemIndex) {
            int indexToRemove = _addNewPos;
            _addNewPos = -1;
            RemoveAt(indexToRemove);
        }
    }
    /// <inheritdoc/>
    public virtual void EndNew(int itemIndex) {
        if (_addNewPos >= 0 && _addNewPos == itemIndex)
            _addNewPos = -1;
    }
    /// <inheritdoc/>
    public virtual bool AllowEdit => true;
    /// <inheritdoc/>
    public bool AllowNew => _addNewFactory is not null;
    /// <inheritdoc/>
    public virtual bool AllowRemove => true;
    /// <inheritdoc/>
    public virtual bool IsSorted => false;
    /// <inheritdoc/>
    public virtual ListSortDirection SortDirection => ListSortDirection.Ascending;
    /// <inheritdoc/>
    public virtual PropertyDescriptor? SortProperty => null;
    /// <inheritdoc/>
    public virtual bool SupportsChangeNotification => true;
    /// <inheritdoc/>
    public virtual bool SupportsSearching => false;
    /// <inheritdoc/>
    public virtual bool SupportsSorting => false;

    /// <inheritdoc/>
    public void Add(TEdit item) {
        Add(MakeNewEditItem(item));
        OnListChanged(new ListChangedEventArgs(ListChangedType.ItemAdded, Count - 1));
    }
    /// <inheritdoc/>
    public void Insert(int index, TEdit item) {
        Insert(index, MakeNewEditItem(item));
        OnListChanged(new ListChangedEventArgs(ListChangedType.ItemAdded, index));
    }
    /// <inheritdoc/>
    public bool Remove(TEdit item) {
        int index = IndexOf(item);
        if (index < 0)
            return false;
        RemoveAt(index);
        return true;
    }
    /// <inheritdoc/>
    public override void RemoveAt(int index) {
        base.RemoveAt(index);
        OnListChanged(new ListChangedEventArgs(ListChangedType.ItemDeleted, index));
    }
    /// <inheritdoc/>
    public bool Contains(TEdit item) => IndexOf(item) >= 0;
    /// <inheritdoc/>
    public int IndexOf(TEdit item) {
        for (int i = 0; i < Count; i++)
            if (ItemEquals(item, Get(i).CurrentValue))
                return i;
        return -1;
    }
    /// <inheritdoc/>
    public void CopyTo(TEdit[] array, int arrayIndex) {
        ArgumentNullException.ThrowIfNull(array);
        if (arrayIndex < 0 || arrayIndex + Count > array.Length)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        for (int i = 0; i < Count; i++) {
            ref var val = ref Get(i);
            if (val.IsEditing)
                array[arrayIndex + i] = val.EditableValue!;
        }
    }
    object? IList.this[int index] {
        get => this[index];
        set {
            if (value is not TEdit item)
                throw new ArgumentException($"Value must be of type {typeof(TEdit).FullName}", nameof(value));
            this[index] = item;
        }
    }
    int IList.Add(object? value) {
        if (value is not TEdit item)
            throw new ArgumentException($"Value must be of type {typeof(TEdit).FullName}", nameof(value));
        Add(item);
        return Count - 1;
    }
    bool IList.Contains(object? value) => value is TEdit item && Contains(item);
    int IList.IndexOf(object? value) => value is TEdit item ? IndexOf(item) : -1;
    void IList.Insert(int index, object? value) {
        if (value is not TEdit item)
            throw new ArgumentException($"Value must be of type {typeof(TEdit).FullName}", nameof(value));
        Insert(index, item);
    }
    void IList.Remove(object? value) {
        if (value is not TEdit item)
            throw new ArgumentException($"Value must be of type {typeof(TEdit).FullName}", nameof(value));
        Remove(item);
    }
    void ICollection.CopyTo(Array array, int index) {
        ArgumentNullException.ThrowIfNull(array);
        if (array.Rank != 1)
            throw new ArgumentException("Multi-dimensional arrays are not supported.");
        for (int i = 0; i < Count; i++)
            array.SetValue(Get(i).EditableValue, index + i);
    }
    /// <inheritdoc/>
    public IEnumerator<TEdit> GetEnumerator() {
        for (int i = 0; i < Count; i++)
            yield return GetValue(i);
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    object? IBindingList.AddNew() => AddNew();
    /// <summary>A typed implementation of <see cref="IBindingList.AddNew"/></summary>
    public virtual TEdit AddNew() {
        if (_addNewFactory is null)
            throw new InvalidOperationException("Cannot add a new item. Provide a factory or handle the AddingNew event.");

        var newItem = _addNewFactory();
        EndNew(_addNewPos);
        Add(newItem);
        _addNewPos = Count - 1;
        return newItem;
    }
    /// <inheritdoc/>
    public virtual void ApplySort(PropertyDescriptor property, ListSortDirection direction) => throw new NotSupportedException();
    /// <inheritdoc/>
    public virtual void RemoveSort() => throw new NotSupportedException();
    /// <inheritdoc/>
    public virtual int Find(PropertyDescriptor property, object key) => throw new NotSupportedException();
    /// <inheritdoc/>
    public virtual void AddIndex(PropertyDescriptor property) { }
    /// <inheritdoc/>
    public virtual void RemoveIndex(PropertyDescriptor property) { }
}
/// <summary>
/// Indicate that you can add a factory to create a new instance of <typeparamref name="TEdit"/>
/// </summary>
public interface IAddSetNewItem<TEdit> {
    /// <summary>
    /// Sets the factory method required to create new items via IBindingList.AddNew().
    /// </summary>
    public void SetNewItemFactory(Func<TEdit> factory);
}