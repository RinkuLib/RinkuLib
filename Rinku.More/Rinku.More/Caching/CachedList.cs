using System.Collections;
using System.ComponentModel;

namespace Rinku.Caching;

public interface ILazyLoaded
{
    bool IsLoaded { get; }
    bool IsLoading { get; }
    void Invalidate();
}

public sealed partial class CachedList<T, TContext>(ICacheLoader<T, TContext>? loader = null) : IDisposable, IReloadableCache<TContext>, ICacheCollection<T>, IReadOnlyList<T>, IBindingList, ICacheLoader<T, TContext>, ICancelAddNew, IRaiseItemChangedEvents
{
    [Flags]
    private enum CacheStatus : byte
    {
        None = 0,
        Loaded = 1,
        Loading = 2,
        RefreshPending = 4
    }

    private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private List<T> _items = [];
    private volatile CacheStatus _status;
    private TaskCompletionSource? _loading;

    public ICacheLoader<T, TContext>? Loader { get; } = loader;

    public bool IsLoaded => (_status & CacheStatus.Loaded) != 0 && (Loader?.IsLoaded ?? true);
    public bool IsLoading => (_status & CacheStatus.Loading) != 0;

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _items.Count;
            }
        }
    }

    public T this[int index]
    {
        get
        {
            lock (_sync)
            {
                return _items[index];
            }
        }
        set
        {
            lock (_sync)
            {
                _items[index] = value;
                OnListChanged(new(ListChangedType.ItemChanged, index));
            }
        }
    }

    public event ListChangedEventHandler? ListChanged;

    public void Invalidate()
    {
        Loader?.Invalidate();
        lock (_sync)
        {
            _status &= ~CacheStatus.Loaded;
            if ((_status & CacheStatus.Loading) != 0)
            {
                _status |= CacheStatus.RefreshPending;
            }
        }
    }

    private void ReloadCore(TContext context, bool force = false)
    {
        if (!TryBeginReload(force))
        {
            WaitForLoad(CancellationToken.None).GetAwaiter().GetResult();
            return;
        }
        try
        {
            Loader?.Load(context, this);
            MarkLoaded();
            CompleteReload();
        }
        catch (Exception exception)
        {
            FailReload(exception);
            OnLoadFailed(exception);
            throw;
        }
    }

    private async Task ReloadCoreAsync(TContext context, CancellationToken cancellationToken, bool force = false)
    {
        if (!TryBeginReload(force))
        {
            await WaitForLoad(cancellationToken).ConfigureAwait(false);
            return;
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var lifetime = cancellationToken.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token) : null;
            if (Loader is not null)
            {
                await Loader.LoadAsync(context, this, lifetime?.Token ?? _lifetime.Token).ConfigureAwait(false);
            }
            MarkLoaded();
            CompleteReload();
        }
        catch (Exception exception)
        {
            FailReload(exception);
            if (exception is not OperationCanceledException)
            {
                OnLoadFailed(exception);
            }
            throw;
        }
    }

    public event Action<Exception>? LoadFailed;
    private void OnLoadFailed(Exception exception) => LoadFailed?.Invoke(exception);
    private void OnListChanged(ListChangedEventArgs args) => ListChanged?.Invoke(this, args);

    public void SetItems(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var replacement = new List<T>(items);
        lock (_sync)
        {
            _items = replacement;
            MarkLoaded();
            OnListChanged(new(ListChangedType.Reset, -1));
        }
    }

    private void MarkLoaded()
    {
        lock (_sync)
        {
            _status |= CacheStatus.Loaded;
        }
    }

    public void Add(T item) => AddItem(item);

    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        T[] addition = [.. items];
        if (addition.Length == 0)
        {
            return;
        }
        lock (_sync)
        {
            _items.AddRange(addition);
            OnListChanged(new(ListChangedType.Reset, -1));
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            if (_items.Count == 0)
            {
                return;
            }
            _items.Clear();
            OnListChanged(new(ListChangedType.Reset, -1));
        }
    }

    public bool Contains(T item)
    {
        lock (_sync)
        {
            return _items.Contains(item);
        }
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        lock (_sync)
        {
            _items.CopyTo(array, arrayIndex);
        }
    }

    public int IndexOf(T item)
    {
        lock (_sync)
        {
            return _items.IndexOf(item);
        }
    }

    public void Insert(int index, T item)
    {
        lock (_sync)
        {
            _items.Insert(index, item);
            OnListChanged(new(ListChangedType.ItemAdded, index));
        }
    }

    public bool Remove(T item)
    {
        int index;
        lock (_sync)
        {
            index = _items.IndexOf(item);
            if (index < 0)
            {
                return false;
            }
            _items.RemoveAt(index);
            OnListChanged(new(ListChangedType.ItemDeleted, index));
        }
        return true;
    }

    public void RemoveAt(int index)
    {
        lock (_sync)
        {
            _items.RemoveAt(index);
            OnListChanged(new(ListChangedType.ItemDeleted, index));
        }
    }

    public List<T>.Enumerator GetEnumerator()
    {
        lock (_sync)
        {
            return _items.GetEnumerator();
        }
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    bool ICollection<T>.IsReadOnly => false;
    bool IList.IsReadOnly => false;
    bool IList.IsFixedSize => false;
    bool ICollection.IsSynchronized => true;
    object ICollection.SyncRoot => _sync;
    bool IBindingList.AllowEdit => true;
    bool IBindingList.AllowNew => false;
    bool IBindingList.AllowRemove => true;
    bool IBindingList.SupportsChangeNotification => true;
    bool IBindingList.SupportsSearching => false;
    bool IBindingList.SupportsSorting => false;
    bool IBindingList.IsSorted => false;
    PropertyDescriptor? IBindingList.SortProperty => null;
    ListSortDirection IBindingList.SortDirection => ListSortDirection.Ascending;
    bool IRaiseItemChangedEvents.RaisesItemChangedEvents => false;

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = Cast(value);
    }

    int IList.Add(object? value) => AddItem(Cast(value));

    bool IList.Contains(object? value) => TryCast(value, out var item) && Contains(item);
    int IList.IndexOf(object? value) => TryCast(value, out var item) ? IndexOf(item) : -1;
    void IList.Insert(int index, object? value) => Insert(index, Cast(value));
    void IList.Remove(object? value)
    {
        if (TryCast(value, out var item))
        {
            Remove(item);
        }
    }

    void ICollection.CopyTo(Array array, int index)
    {
        lock (_sync)
        {
            ((ICollection)_items).CopyTo(array, index);
        }
    }

    object? IBindingList.AddNew() => throw new NotSupportedException();
    void IBindingList.AddIndex(PropertyDescriptor property) { }
    void IBindingList.RemoveIndex(PropertyDescriptor property) { }
    int IBindingList.Find(PropertyDescriptor property, object key) => -1;
    void IBindingList.ApplySort(PropertyDescriptor property, ListSortDirection direction) => throw new NotSupportedException();
    void IBindingList.RemoveSort() => throw new NotSupportedException();
    void ICancelAddNew.CancelNew(int itemIndex) { }
    void ICancelAddNew.EndNew(int itemIndex) { }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _status &= ~CacheStatus.RefreshPending;
            ListChanged = null;
            LoadFailed = null;
        }
        _lifetime.Cancel();
        if (IsLoading)
        {
            _ = DisposeLoaderAsync();
        }
        else
        {
            Loader?.Dispose();
        }
    }

    private bool TryBeginReload(bool force)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if ((_status & CacheStatus.Loading) != 0 || (!force && IsLoaded))
            {
                return false;
            }
            _loading = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _status |= CacheStatus.Loading;
            return true;
        }
    }

    private void CompleteReload()
    {
        TaskCompletionSource? loading;
        lock (_sync)
        {
            loading = _loading;
            bool pending = (_status & CacheStatus.RefreshPending) != 0;
            _status &= ~(CacheStatus.Loading | CacheStatus.RefreshPending);
            if (pending)
            {
                _status &= ~CacheStatus.Loaded;
            }
        }
        loading?.TrySetResult();
    }

    private Task WaitForLoad(CancellationToken cancellationToken)
    {
        Task task;
        lock (_sync)
        {
            task = _loading?.Task ?? Task.CompletedTask;
        }
        return cancellationToken.CanBeCanceled ? task.WaitAsync(cancellationToken) : task;
    }

    private void FailReload(Exception error)
    {
        TaskCompletionSource? loading;
        lock (_sync)
        {
            _status &= ~(CacheStatus.Loading | CacheStatus.RefreshPending);
            loading = _loading;
        }
        loading?.TrySetException(error);
        _ = loading?.Task.Exception;
    }

    private int AddItem(T item)
    {
        int index;
        lock (_sync)
        {
            index = _items.Count;
            _items.Add(item);
            OnListChanged(new(ListChangedType.ItemAdded, index));
        }
        return index;
    }

    private static bool TryCast(object? value, out T item)
    {
        item = default!;
        if (value is T typed)
        {
            item = typed;
            return true;
        }
        if (value is null && default(T) is null)
        {
            return true;
        }
        return false;
    }

    private static T Cast(object? value)
    {
        if (TryCast(value, out var item))
        {
            return item;
        }
        throw new ArgumentException($"Value must be assignable to {typeof(T)}.", nameof(value));
    }
}
