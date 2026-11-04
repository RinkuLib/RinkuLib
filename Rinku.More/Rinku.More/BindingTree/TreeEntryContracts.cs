namespace BindingTree;

public interface ITreeEntry
{
    object? Item { get; }
    ITreeEntry? Parent { get; }
    int Count { get; }
    [System.Runtime.CompilerServices.IndexerName("Child")]
    ITreeEntry this[int index] { get; }
    IEnumerable<ITreeEntry> Children { get; }
    bool Detach();
    bool Remove();
    void Rebuild();
    void Refresh();
}

/// <summary>A covariant view of a tree node. Consumers cannot insert a value of the wrong type.</summary>
public interface ITreeEntry<out T> : ITreeEntry
{
    new T Item { get; }
    new ITreeEntry<T>? Parent { get; }
    [System.Runtime.CompilerServices.IndexerName("Child")]
    new ITreeEntry<T> this[int index] { get; }
    new IEnumerable<ITreeEntry<T>> Children { get; }
}

/// <summary>Observes changes without requiring the default mutable node implementation.</summary>
public interface IBindingTreeSource<T> : ITreeEntry<T> { event Action<TreeChange<T>>? Changed; }

/// <summary>A mutable tree contract. Read-only consumers can still use the covariant entry view.</summary>
public interface IBindingTree<T> : IBindingTreeSource<T>
{
    new T Item { get; set; }
    IBindingTree<T> Add(T item);
    IBindingTree<T> Insert(int index, T item);
    IBindingTree<T> Add(IBindingTree<T> branch);
    IBindingTree<T> Insert(int index, IBindingTree<T> branch);
}

/// <summary>Optional, item-type-independent change notifications for consumers that only need to refresh.</summary>
public interface ITreeNotifications { event Action? Updated; }

/// <summary>A covariant, read-only tree with item-independent change notifications.</summary>
public interface IBindingTreeView<out T> : ITreeEntry<T>, ITreeNotifications;

public interface INeedTreeEntry { ITreeEntry TreeEntry { set; } }

public interface INeedTreeEntry<T> : INeedTreeEntry
{
    new IBindingTree<T> TreeEntry { set; }
    ITreeEntry INeedTreeEntry.TreeEntry { set => TreeEntry = (IBindingTree<T>)value; }
}

public interface ITreeActivation
{
    void Activate();
}

public interface ITreeActivationSource<T>
{
    event Action<ITreeEntry<T>>? ActivationRequested;
}

public interface ITreeRemovalGuard
{
    bool CanRemove();
}

public interface ITreeRemovalObserver
{
    void Removed();
}

public static class TreeTraversal
{
    public static IEnumerable<ITreeEntry<T>> DepthFirst<T>(this ITreeEntry<T> entry, Func<ITreeEntry<T>, bool>? include = null)
    {
        for (int index = 0; index < entry.Count; index++)
        {
            var child = entry[index];
            if (include?.Invoke(child) == false) continue;
            yield return child;
            foreach (var descendant in child.DepthFirst(include)) yield return descendant;
        }
    }
    public static ITreeEntry<T>[] SharedOrder<T>(this ITreeEntry<T> entry, Func<ITreeEntry<T>, bool>? include = null)
    {
        List<(ITreeEntry<T> Entry, int Index, int Order)> entries = [];
        Queue<(ITreeEntry<T> Entry, int Index)> pending = [];
        for (int index = 0; index < entry.Count; index++) pending.Enqueue((entry[index], index));
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (include?.Invoke(current.Entry) == false) continue;
            entries.Add((current.Entry, current.Index, entries.Count));
            for (int index = 0; index < current.Entry.Count; index++) pending.Enqueue((current.Entry[index], Math.Max(current.Index, index)));
        }
        entries.Sort(static (left, right) => { int order = left.Index.CompareTo(right.Index); return order != 0 ? order : left.Order.CompareTo(right.Order); });
        var result = new ITreeEntry<T>[entries.Count];
        for (int index = 0; index < result.Length; index++) result[index] = entries[index].Entry;
        return result;
    }
    public static ITreeEntry<T>? At<T>(this ITreeEntry<T> parent, Predicate<T> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        for (int index = 0; index < parent.Count; index++) if (predicate(parent[index].Item)) return parent[index];
        return null;
    }
    public static int IndexOf<T>(this ITreeEntry<T> parent, ITreeEntry<T> entry)
    {
        for (int index = 0; index < parent.Count; index++) if (ReferenceEquals(parent[index], entry)) return index;
        return -1;
    }
}
