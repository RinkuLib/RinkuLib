using System.Collections;
using System.ComponentModel;

namespace Rinku.Caching;

public sealed class ProjectedBindingDictionary<TSource, TKey, TValue> : IReadOnlyDictionary<TKey, TValue>, IBindingList, ITypedList, IDisposable where TKey : notnull
{
    private readonly IEqualityComparer<TKey>? _comparer;
    private readonly Func<TSource, TKey> _key;
    private readonly Func<TSource, TValue> _value;
    private KeyValuePair<TKey, TValue>[] _items = [];
    private Dictionary<TKey, TValue> _values;

    public ProjectedBindingDictionary(IBindingList source, Func<TSource, TKey> key, Func<TSource, TValue> value, IEqualityComparer<TKey>? comparer = null)
    {
        Source = source;
        _key = key;
        _value = value;
        _comparer = comparer;
        _values = new(comparer);
        lock (Source.SyncRoot)
        {
            Rebuild();
            Source.ListChanged += SourceChanged;
        }
    }

    public IBindingList Source { get; }
    private Dictionary<TKey, TValue> Current
    {
        get
        {
            lock (Source.SyncRoot)
            {
                return _values;
            }
        }
    }

    public int Count => Current.Count;
    public TValue this[TKey key] => Current[key];
    public IEnumerable<TKey> Keys => Current.Keys;
    public IEnumerable<TValue> Values => Current.Values;
    public bool ContainsKey(TKey key) => Current.ContainsKey(key);
    public bool TryGetValue(TKey key, out TValue value) => Current.TryGetValue(key, out value!);
    public Dictionary<TKey, TValue>.Enumerator GetEnumerator() => Current.GetEnumerator();
    IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    PropertyDescriptorCollection ITypedList.GetItemProperties(PropertyDescriptor[]? listAccessors) => TypeDescriptor.GetProperties(typeof(KeyValuePair<TKey, TValue>));
    string ITypedList.GetListName(PropertyDescriptor[]? listAccessors) => typeof(KeyValuePair<TKey, TValue>).Name;
    public event ListChangedEventHandler? ListChanged;

    private void SourceChanged(object? sender, ListChangedEventArgs args)
    {
        lock (Source.SyncRoot)
        {
            Rebuild();
            ListChanged?.Invoke(this, new(ListChangedType.Reset, -1));
        }
    }

    private void Rebuild()
    {
        var values = new Dictionary<TKey, TValue>(_comparer);
        foreach (TSource item in Source)
        {
            values[_key(item)] = _value(item);
        }
        _values = values;
        _items = [.. values];
    }

    bool IList.IsReadOnly => true;
    bool IList.IsFixedSize => false;
    bool ICollection.IsSynchronized => true;
    object ICollection.SyncRoot => Source.SyncRoot;
    bool IBindingList.AllowEdit => false;
    bool IBindingList.AllowNew => false;
    bool IBindingList.AllowRemove => false;
    bool IBindingList.SupportsChangeNotification => true;
    bool IBindingList.SupportsSearching => false;
    bool IBindingList.SupportsSorting => false;
    bool IBindingList.IsSorted => false;
    PropertyDescriptor? IBindingList.SortProperty => null;
    ListSortDirection IBindingList.SortDirection => ListSortDirection.Ascending;
    object? IList.this[int index]
    {
        get
        {
            lock (Source.SyncRoot)
            {
                return _items[index];
            }
        }
        set => throw new NotSupportedException();
    }
    bool IList.Contains(object? value) => ((IList)this).IndexOf(value) >= 0;
    int IList.IndexOf(object? value)
    {
        lock (Source.SyncRoot)
        {
            return value is KeyValuePair<TKey, TValue> item ? Array.IndexOf(_items, item) : -1;
        }
    }
    void ICollection.CopyTo(Array array, int index)
    {
        lock (Source.SyncRoot)
        {
            _items.CopyTo(array, index);
        }
    }
    int IList.Add(object? value) => throw new NotSupportedException();
    void IList.Insert(int index, object? value) => throw new NotSupportedException();
    void IList.Remove(object? value) => throw new NotSupportedException();
    void IList.RemoveAt(int index) => throw new NotSupportedException();
    void IList.Clear() => throw new NotSupportedException();
    object? IBindingList.AddNew() => throw new NotSupportedException();
    void IBindingList.AddIndex(PropertyDescriptor property) { }
    void IBindingList.RemoveIndex(PropertyDescriptor property) { }
    int IBindingList.Find(PropertyDescriptor property, object key) => -1;
    void IBindingList.ApplySort(PropertyDescriptor property, ListSortDirection direction) => throw new NotSupportedException();
    void IBindingList.RemoveSort() => throw new NotSupportedException();

    public void Dispose()
    {
        lock (Source.SyncRoot)
        {
            Source.ListChanged -= SourceChanged;
            ListChanged = null;
        }
    }
}
