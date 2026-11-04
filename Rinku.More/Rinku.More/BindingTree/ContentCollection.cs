namespace BindingTree;

public interface IContentSlot<out TItem, out TContent> {
    ITreeEntry<TItem> Entry { get; }
    TContent Content { get; }
    bool Closable { get; }
}

public interface IContentCollection<out TItem, out TContent> {
    IReadOnlyList<IContentSlot<TItem, TContent>> Items { get; }
    IReadOnlyList<ITreeEntry<TItem>> Entries { get; }
    ITreeEntry<TItem>? Selected { get; }
    int SelectedIndex { get; }
    TContent Active { get; }
    void Select(int index);
    Task<bool> CloseAsync(int index);
    event Action? DisplayChanged;
}

/// <summary>Owns an ordered set of materialized entries. Traversal and source subscriptions belong to the caller.</summary>
public class ContentCollection<TItem, TContent> : IContentCollection<TItem, TContent>, IReadOnlyList<ITreeEntry<TItem>>, IDisposable where TContent : IDisposable {
    private readonly List<Slot> _items = [];
    private int _position;
    private Slot? _selected, _previous, _added;
    private Func<ITreeEntry<TItem>, Task<bool>>? _close;
    private bool _disposed;
    public IReadOnlyList<IContentSlot<TItem, TContent>> Items => _items;
    public IReadOnlyList<ITreeEntry<TItem>> Entries => this;
    public ITreeEntry<TItem>? Selected => _selected?.Entry;
    public int Count => _items.Count;
    public ITreeEntry<TItem> this[int index] => _items[index].Entry;
    public IEnumerator<ITreeEntry<TItem>> GetEnumerator() { foreach (var item in _items) yield return item.Entry; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    public int SelectedIndex => _selected is null ? -1 : _items.IndexOf(_selected);
    public TContent Active => _selected!.Content;
    public IEnumerable<TContent> Children { get { foreach (var item in _items) yield return item.Content; } }
    public IEnumerable<TContent> VisibleChildren { get { if (_selected is not null) yield return _selected.Content; } }
    public event Action? DisplayChanged;
    public event Action? ActivationChanged;
    public event EventHandler? Disposed;
    public void BeginContent() { _position = 0; _previous = _selected; }
    public void EnsureContent(ITreeEntry<TItem> entry, Func<TItem, TContent> create, Action<TContent, TItem> apply, bool closable) {
        var slot = Find(entry);
        if (slot is not null && !(typeof(TItem).IsValueType ? EqualityComparer<TItem>.Default.Equals(slot.Item, entry.Item) : ReferenceEquals(slot.Item, entry.Item))) {
            Release(slot);
            slot = null;
        }
        if (slot is null) {
            var content = create(entry.Item);
            try {
                slot = new(this, entry, content, apply, closable);
                AttachContent(content);
                _items.Insert(_position, slot);
                _added = slot;
            }
            catch { slot?.Detach(); DetachContent(content); content.Dispose(); throw; }
        }
        else {
            int index = _items.IndexOf(slot);
            if (index != _position) { _items.RemoveAt(index); _items.Insert(_position, slot); }
        }
        _position++;
        slot.Apply = apply;
        slot.Refresh();
    }
    public void EndContent() {
        while (_items.Count > _position) Release(_items[^1]);
        if (_added is not null) _selected = _added;
        else if (_selected is null || !_items.Contains(_selected)) _selected = _items.Count == 0 ? null : _items[0];
        bool changed = !ReferenceEquals(_previous, _selected);
        _previous = _added = null;
        if (changed) ActivationChanged?.Invoke();
        NotifyDisplayChanged();
    }
    public void RefreshContent(ITreeEntry<TItem> entry) { Find(entry)?.Refresh(); NotifyDisplayChanged(); }
    public TContent Display(ITreeEntry<TItem> entry) => Find(entry)!.Content;
    public void ActivateContent(ITreeEntry<TItem> entry) {
        if (Find(entry) is not { } slot) return;
        _selected = slot;
        ActivationChanged?.Invoke();
        NotifyDisplayChanged();
    }
    public void Select(int index) => ActivateContent(_items[index].Entry);
    public Task<bool> CloseAsync(int index) => _items[index].Closable && _close is not null ? _close(_items[index].Entry) : Task.FromResult(false);
    public void OnCloseContent(Func<ITreeEntry<TItem>, Task<bool>> close) => _close = close;
    public void OnDisposed(EventHandler handler) { Disposed -= handler; Disposed += handler; }
    protected virtual void AttachContent(TContent content) { }
    protected virtual void DetachContent(TContent content) { }
    protected void NotifyDisplayChanged() => DisplayChanged?.Invoke();
    private Slot? Find(ITreeEntry<TItem> entry) {
        foreach (var item in _items) if (ReferenceEquals(item.Entry, entry)) return item;
        return null;
    }
    private void Release(Slot slot) { _items.Remove(slot); slot.Detach(); DetachContent(slot.Content); slot.Content.Dispose(); }
    public virtual void Dispose() {
        if (_disposed) return;
        _disposed = true;
        Disposed?.Invoke(this, EventArgs.Empty);
        while (_items.Count != 0) Release(_items[^1]);
        _selected = _previous = _added = null;
        _close = null;
        Disposed = null;
        DisplayChanged = ActivationChanged = null;
    }
    private sealed class Slot : IContentSlot<TItem, TContent> {
        private readonly ContentCollection<TItem, TContent> _owner;
        private readonly System.ComponentModel.PropertyChangedEventHandler? _changed;
        internal TItem Item { get; }
        public ITreeEntry<TItem> Entry { get; }
        public TContent Content { get; }
        public bool Closable { get; }
        internal Action<TContent, TItem> Apply;
        internal Slot(ContentCollection<TItem, TContent> owner, ITreeEntry<TItem> entry, TContent content, Action<TContent, TItem> apply, bool closable) {
            _owner = owner;
            Entry = entry; Item = entry.Item; Content = content; Apply = apply; Closable = closable;
            if (Item is System.ComponentModel.INotifyPropertyChanged source) source.PropertyChanged += _changed = Changed;
        }
        private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args) { Refresh(); _owner.NotifyDisplayChanged(); }
        internal void Detach() { if (Item is System.ComponentModel.INotifyPropertyChanged source) source.PropertyChanged -= _changed; }
        internal void Refresh() => Apply(Content, Item);
    }
}
