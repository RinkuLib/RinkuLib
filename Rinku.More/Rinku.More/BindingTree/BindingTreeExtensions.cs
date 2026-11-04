using BindingTree;

namespace BindingTree;

public enum ExistingEntryScope
{
    SameLevel,
    Parent,
    Ancestors,
    Descendants,
    Tree
}

public static class BindingTreeExtensions
{
    public static BindingTreeEntry<T> AddIfMissing<T>(this BindingTreeEntry<T> parent, T item, Func<T, T, bool> equivalent, ExistingEntryScope scope = ExistingEntryScope.SameLevel)
        => AddIfMissing(parent, item, equivalent, scope, out _);

    public static BindingTreeEntry<T> AddIfMissing<T>(this BindingTreeEntry<T> parent, T item, Func<T, T, bool> equivalent, ExistingEntryScope scope, out bool added)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(equivalent);

        BindingTreeEntry<T>? existing = parent.FindEquivalent(item, equivalent, scope);
        if (existing is not null)
        {
            added = false;
            return existing;
        }

        added = true;
        return parent.Add(item);
    }

    public static BindingTreeEntry<T> AddIfMissing<T>(this BindingTreeEntry<T> parent, T item, IEqualityComparer<T>? comparer = null, ExistingEntryScope scope = ExistingEntryScope.SameLevel)
    {
        comparer ??= EqualityComparer<T>.Default;
        return AddIfMissing(parent, item, comparer.Equals, scope);
    }

    public static BindingTreeEntry<T> AddIfMissing<T, TKey>(this BindingTreeEntry<T> parent, T item, Func<T, TKey> keySelector, ExistingEntryScope scope = ExistingEntryScope.SameLevel, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        comparer ??= EqualityComparer<TKey>.Default;
        return AddIfMissing(parent, item, (left, right) => comparer.Equals(keySelector(left), keySelector(right)), scope);
    }

    /// <summary>Default-builder convenience retaining its specialized node result.</summary>
    public static BindingTreeEntry<T>? FindEquivalent<T>(this BindingTreeEntry<T> parent, T item, Func<T, T, bool> equivalent, ExistingEntryScope scope = ExistingEntryScope.SameLevel)
        => (BindingTreeEntry<T>?)FindEquivalent((ITreeEntry<T>)parent, item, equivalent, scope);
    public static ITreeEntry<T>? FindEquivalent<T>(this ITreeEntry<T> parent, T item, Func<T, T, bool> equivalent, ExistingEntryScope scope = ExistingEntryScope.SameLevel)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(equivalent);

        return scope switch
        {
            ExistingEntryScope.SameLevel => FindChildren(parent, item, equivalent, false),
            ExistingEntryScope.Parent => equivalent(parent.Item, item) ? parent : null,
            ExistingEntryScope.Ancestors => FindAncestors(parent, item, equivalent),
            ExistingEntryScope.Descendants => FindChildren(parent, item, equivalent, true),
            ExistingEntryScope.Tree => FindTree(parent, item, equivalent),
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
    }

    public static void RefreshBranch<T>(this ITreeEntry<T> entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Refresh();
        foreach (ITreeEntry<T> child in entry.Children)
            child.RefreshBranch();
    }

    private static ITreeEntry<T>? FindChildren<T>(ITreeEntry<T> parent, T item, Func<T, T, bool> equivalent, bool recursive)
    {
        for (int i = 0; i < parent.Count; i++)
        {
            ITreeEntry<T> child = parent[i];
            if (equivalent(child.Item, item))
                return child;
            if (recursive && FindChildren(child, item, equivalent, true) is ITreeEntry<T> descendant)
                return descendant;
        }
        return null;
    }

    private static ITreeEntry<T>? FindAncestors<T>(ITreeEntry<T> parent, T item, Func<T, T, bool> equivalent)
    {
        for (ITreeEntry<T>? current = parent; current is not null; current = current.Parent)
            if (equivalent(current.Item, item))
                return current;
        return null;
    }

    private static ITreeEntry<T>? FindTree<T>(ITreeEntry<T> parent, T item, Func<T, T, bool> equivalent)
    {
        ITreeEntry<T> root = parent;
        while (root.Parent is not null)
            root = root.Parent;
        if (equivalent(root.Item, item))
            return root;
        return FindChildren(root, item, equivalent, true);
    }
}
