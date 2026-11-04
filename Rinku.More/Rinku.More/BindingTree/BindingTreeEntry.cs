namespace BindingTree;

public enum TreeChangeKind
{
    Add,
    Remove,
    Rebuild,
    Refresh
}

public readonly record struct TreeChange<T>(TreeChangeKind Kind, ITreeEntry<T> Entry, int Index);

public sealed class BindingTreeEntry<T> : IBindingTree<T>, IBindingTreeView<T>, ITreeActivation, ITreeActivationSource<T>
{
    private BindingTreeEntry<T>[] _children = [];
    private int _count;
    private T _item;

    public BindingTreeEntry(T item)
    {
        _item = item;
        AssignEntryToItem();
    }

    public T Item
    {
        get => _item;
        set
        {
            _item = value;
            AssignEntryToItem();
            Rebuild();
        }
    }
    object? ITreeEntry.Item => Item;
    public BindingTreeEntry<T>? Parent { get; private set; }
    ITreeEntry? ITreeEntry.Parent => Parent;
    ITreeEntry<T>? ITreeEntry<T>.Parent => Parent;
    public int Count => _count;
    public ReadOnlySpan<BindingTreeEntry<T>> Children => _children.AsSpan(0, _count);
    IEnumerable<ITreeEntry> ITreeEntry.Children => EnumerateChildren();
    IEnumerable<ITreeEntry<T>> ITreeEntry<T>.Children => EnumerateTypedChildren();
    [System.Runtime.CompilerServices.IndexerName("Child")]
    public BindingTreeEntry<T> this[int index] => (uint)index < (uint)_count ? _children[index] : throw new ArgumentOutOfRangeException(nameof(index));
    ITreeEntry ITreeEntry.this[int index] => this[index];
    ITreeEntry<T> ITreeEntry<T>.this[int index] => this[index];

    public event Action<TreeChange<T>>? Changed;
    public event Action? Updated;
    public event Action<ITreeEntry<T>>? ActivationRequested;
    public void Activate()
    {
        for (BindingTreeEntry<T>? current = this; current is not null; current = current.Parent)
        {
            current.ActivationRequested?.Invoke(this);
        }
    }


    public BindingTreeEntry<T> Add(T item) => Insert(_count, item);
    IBindingTree<T> IBindingTree<T>.Add(T item) => Add(item);
    IBindingTree<T> IBindingTree<T>.Insert(int index, T item) => Insert(index, item);
    IBindingTree<T> IBindingTree<T>.Add(IBindingTree<T> branch) => Insert(_count, RequireBranch(branch));
    IBindingTree<T> IBindingTree<T>.Insert(int index, IBindingTree<T> branch) => Insert(index, RequireBranch(branch));
    private static BindingTreeEntry<T> RequireBranch(IBindingTree<T> branch) => branch as BindingTreeEntry<T> ?? throw new ArgumentException("Branches must belong to the same tree implementation.", nameof(branch));
    public BindingTreeEntry<T> Add(BindingTreeEntry<T> entry) => Insert(_count, entry);

    public BindingTreeEntry<T> Insert(int index, T item)
    {
        BindingTreeEntry<T> entry = new(item);
        Insert(index, entry);
        return entry;
    }

    public BindingTreeEntry<T> Insert(int index, BindingTreeEntry<T> entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if ((uint)index > (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(index));

        ValidateAttach(entry);

        BindingTreeEntry<T>? oldParent = entry.Parent;
        int oldIndex = oldParent?.IndexOf(entry) ?? -1;
        if (oldIndex >= 0)
        {
            oldParent!.Detach(entry, oldIndex);
            if (ReferenceEquals(oldParent, this) && oldIndex < index)
                index--;
        }

        EnsureCapacity(_count + 1);

        if (index < _count)
            Array.Copy(_children, index, _children, index + 1, _count - index);

        _children[index] = entry;
        _count++;
        entry.Parent = this;

        Publish(new(TreeChangeKind.Add, entry, index));
        return entry;
    }

    public BindingTreeEntry<T>? At(Predicate<T> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        for (int i = 0; i < _count; i++)
            if (predicate(_children[i].Item))
                return _children[i];

        return null;
    }

    public int IndexOf(BindingTreeEntry<T> entry)
    {
        for (int i = 0; i < _count; i++)
            if (ReferenceEquals(_children[i], entry))
                return i;

        return -1;
    }

    public bool Detach()
    {
        if (Parent is not { } parent)
            return false;
        int index = parent.IndexOf(this);
        if (index < 0)
            return false;
        parent.Detach(this, index);
        return true;
    }

    public bool Remove() => Parent is not null && Parent.Remove(this);

    public void Rebuild() => Publish(new(TreeChangeKind.Rebuild, this, Parent?.IndexOf(this) ?? 0));

    public void Refresh() => Publish(new(TreeChangeKind.Refresh, this, Parent?.IndexOf(this) ?? 0));

    public bool Remove(BindingTreeEntry<T> entry)
    {
        int index = IndexOf(entry);

        if (index < 0 || !entry.CanRemove())
            return false;

        try
        {
            Detach(entry, index);
        }
        finally
        {
            entry.NotifyRemoved();
        }
        return true;
    }

    private bool CanRemove()
    {
        return Item is not ITreeRemovalGuard guard || guard.CanRemove();
    }

    private void NotifyRemoved()
    {
        if (Item is ITreeRemovalObserver observer)
        {
            observer.Removed();
        }
        else if (Item is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void Detach(BindingTreeEntry<T> entry, int index)
    {

        if (index < _count - 1)
            Array.Copy(_children, index + 1, _children, index, _count - index - 1);

        _count--;
        Array.Clear(_children, _count, 1);

        try
        {
            Publish(new(TreeChangeKind.Remove, entry, index));
        }
        finally
        {
            entry.Parent = null;
        }
    }

    public IEnumerable<BindingTreeEntry<T>> DepthFirst()
    {
        for (int i = 0; i < _count; i++)
        {
            BindingTreeEntry<T> child = _children[i];
            yield return child;

            foreach (BindingTreeEntry<T> descendant in child.DepthFirst())
                yield return descendant;
        }
    }

    private IEnumerable<ITreeEntry> EnumerateChildren()
    {
        for (int i = 0; i < _count; i++)
            yield return _children[i];
    }

    private IEnumerable<ITreeEntry<T>> EnumerateTypedChildren()
    {
        for (int index = 0; index < _count; index++)
            yield return _children[index];
    }

    private void AssignEntryToItem()
    {
        if (Item is INeedTreeEntry consumer)
            consumer.TreeEntry = this;
    }

    private void ValidateAttach(BindingTreeEntry<T> entry)
    {
        for (BindingTreeEntry<T>? current = this; current is not null; current = current.Parent)
            if (ReferenceEquals(current, entry))
                throw new InvalidOperationException("A tree entry cannot be attached below itself or one of its descendants.");
    }

    private void EnsureCapacity(int capacity)
    {
        if (_children.Length >= capacity)
            return;

        int newCapacity = _children.Length == 0 ? 4 : _children.Length * 2;
        if (newCapacity < capacity)
            newCapacity = capacity;

        Array.Resize(ref _children, newCapacity);
    }

    private void Publish(TreeChange<T> change)
    {
        for (BindingTreeEntry<T>? current = this; current is not null; current = current.Parent)
        {
            current.Changed?.Invoke(change);
            current.Updated?.Invoke();
        }
    }


}
