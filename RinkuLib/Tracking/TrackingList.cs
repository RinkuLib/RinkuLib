using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace RinkuLib.Tracking;
/// <summary>
/// A collection that track removals and revivals 
/// </summary>
public class TrackingList<TOg, TTrackingItem> : ITrackingList<TOg>, IList<TTrackingItem>, IReadOnlyList<TTrackingItem> where TTrackingItem : ITrackingItem<TOg> {
    private TTrackingItem[] _items;
    private int _count;
    private readonly List<TOg> _removed = [];
    /// <summary>
    /// Gets items to track from
    /// </summary>
    public TrackingList(IEnumerable<TTrackingItem> items, int initialCapacity = 4) {
        _items = new TTrackingItem[items.TryGetNonEnumeratedCount(out var count) ? count : initialCapacity];
        foreach (var item in items)
            Add(item);
    }
    /// <summary>Check if items are equals</summary>
    protected virtual bool ItemEquals(TTrackingItem item1, TTrackingItem item2)
        => EqualityComparer<TTrackingItem>.Default.Equals(item1, item2);
    /// <summary>Get the tracking item at the specified index</summary>
    public ref TTrackingItem Get(int index) {
        if (index >= _count)
            throw new IndexOutOfRangeException();
        return ref _items[index];
    }
    /// <summary>Set the tracking item at the specified index</summary>
    /// <remarks>Will be treated as a removal of the previous and an insertion of the new (without moving around)</remarks>
    public void Set(int index, TTrackingItem item) {
        if (index >= _count)
            throw new IndexOutOfRangeException();
        if (_items[index].HasOriginal(out var og))
            _removed.Add(og);
        TryResurrect(ref item);
        _items[index] = item;
    }
    /// <inheritdoc/>
    public IReadOnlyList<TOg> Removed => _removed;
    /// <inheritdoc/>
    public bool HasOriginal(int index, [MaybeNullWhen(false)] out TOg originalValue)
        => _items[index].HasOriginal(out originalValue);
    /// <inheritdoc/>
    public int Count => _count;
    /// <inheritdoc/>
    public int ItemCount => _count;
    bool ICollection<TTrackingItem>.IsReadOnly => false;
    TTrackingItem IReadOnlyList<TTrackingItem>.this[int index] => Get(index);
    TTrackingItem IList<TTrackingItem>.this[int index] { get => Get(index); set => Set(index, value); }
    /// <inheritdoc/>
    public void Add(TTrackingItem item) {
        TryResurrect(ref item);
        EnsureCapacity(_count + 1);
        _items[_count++] = item;
    }
    private bool TryResurrect(ref TTrackingItem item) {
        for (int i = 0; i < _removed.Count; i++)
            if (item.TryReattach(_removed[i])) {
                _removed.RemoveAt(i);
                return true;
            }
        return false;
    }
    /// <inheritdoc/>
    public bool Remove(TTrackingItem item) {
        var index = IndexOf(item);
        if (index < 0)
            return false;
        RemoveAt(index);
        return true;
    }
    /// <inheritdoc/>
    public virtual void Clear() {
        for (int i = 0; i < _count; i++)
            if (_items[i].HasOriginal(out var og))
                _removed.Add(og);
        Array.Clear(_items, 0, _count);
        _count = 0;
    }
    /// <inheritdoc/>
    public virtual void CommitRemoved() => _removed.Clear();
    /// <inheritdoc/>
    public bool Contains(TTrackingItem item) => IndexOf(item) >= 0;
    /// <inheritdoc/>
    public int IndexOf(TTrackingItem item) {
        for (int i = 0; i < _count; i++)
            if (ItemEquals(item, _items[i]))
                return i;
        return -1;
    }
    /// <inheritdoc/>
    public void Insert(int index, TTrackingItem item) {
        if (index > _count)
            throw new IndexOutOfRangeException();
        TryResurrect(ref item);
        EnsureCapacity(_count + 1);
        if (index < _count)
            Array.Copy(_items, index, _items, index + 1, _count - index);
        _items[index] = item;
        _count++;
    }
    /// <inheritdoc/>
    public virtual void RemoveAt(int index) {
        if (index >= _count)
            throw new IndexOutOfRangeException();
        if (_items[index].HasOriginal(out var og))
            _removed.Add(og);
        _count--;
        if (index < _count)
            Array.Copy(_items, index + 1, _items, index, _count - index);
        _items[_count] = default!;
    }
    private void EnsureCapacity(int min) {
        if (_items.Length >= min)
            return;
        int newCapacity = _items.Length == 0 ? 4 : _items.Length * 2;
        if (newCapacity < min)
            newCapacity = min;

        var newArray = new TTrackingItem[newCapacity];
        Array.Copy(_items, newArray, _count);
        _items = newArray;
    }
    /// <inheritdoc/>
    public void CopyTo(TTrackingItem[] array, int arrayIndex)
        => Array.Copy(_items, 0, array, arrayIndex, _count);
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<TTrackingItem>)this).GetEnumerator();
    IEnumerator<TTrackingItem> IEnumerable<TTrackingItem>.GetEnumerator() {
        for (int i = 0; i < _count; i++)
            yield return _items[i];
    }
}