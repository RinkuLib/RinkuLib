using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace RinkuLib.Tracking;

/// <summary>
/// A collection that track removal and revival and uses <typeparamref name="TEditItem"/> to manage editing state
/// </summary>
public abstract class TrackingEditListBase<TOg, TEdit, TEditItem>(IEnumerable<TEditItem> items, int initialCapacity = 4)
    : TrackingList<TOg, TEditItem>(items, items.TryGetNonEnumeratedCount(out var count) && initialCapacity < count ? count : initialCapacity),
    IList<TEdit>, IReadOnlyList<TEdit>, IEditableList<TOg, TEdit>, IList where TEditItem : IEditableItem<TEdit>, ITrackingItem<TOg> {
    /// <summary>Create a new instance of the edit item from an instance of <typeparamref name="TEdit"/></summary>
    public abstract TEditItem MakeNewEditItem(TEdit newItem);
    /// <inheritdoc/>
    public TEdit this[int index] {
        get => GetValue(index);
        set => Set(index, MakeNewEditItem(value));
    }
    private TEdit GetValue(int index) => Get(index).CurrentValue ?? throw new Exception($"No values were available to display at index {index}");
    /// <inheritdoc/>
    public bool HasEditValue(int index, [MaybeNullWhen(false)] out TEdit editValue) {
        ref var item = ref Get(index);
        editValue = item.IsEditing ? item.EditableValue : default;
        return item.IsEditing;
    }
    /// <summary>Check if items are equals</summary>
    protected virtual bool ItemEquals(TEdit item1, TEdit? item2)
        => EqualityComparer<TEdit>.Default.Equals(item1, item2);
    /// <inheritdoc/>
    public bool IsEditing(int index) => Get(index).IsEditing;
    /// <inheritdoc/>
    public virtual void CommitEdit(int index) => Get(index).CommitEdit();
    /// <inheritdoc/>
    public virtual void CancelEdit(int index) => Get(index).CancelEdit();
    /// <inheritdoc/>
    public virtual Task CommitEditAsync(int index) => Get(index).CommitEditAsync();
    /// <inheritdoc/>
    public virtual Task CancelEditAsync(int index) => Get(index).CancelEditAsync();
    /// <inheritdoc/>
    public bool IsReadOnly => false;
    /// <inheritdoc/>
    public bool IsFixedSize => false;
    /// <inheritdoc/>
    public bool IsSynchronized => false;
    /// <inheritdoc/>
    public object SyncRoot => this;
    /// <inheritdoc/>
    public bool HasEdit {
        get {
            for (int i = 0; i < Count; i++)
                if (Get(i).IsEditing)
                    return true;
            return false;
        }
    }
    /// <inheritdoc/>
    public void Add(TEdit item) => Add(MakeNewEditItem(item));
    /// <inheritdoc/>
    public void Insert(int index, TEdit item) => Insert(index, MakeNewEditItem(item));
    /// <inheritdoc/>
    public bool Remove(TEdit item) {
        int index = IndexOf(item);
        if (index < 0)
            return false;
        RemoveAt(index);
        return true;
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
                array[arrayIndex + i] = val.EditableValue;
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
}
