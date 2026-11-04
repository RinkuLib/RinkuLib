using BindingTree;

namespace Rinku.More.Tests;

/// <summary>Tests tree-entry injection and non-generic tree-entry access.</summary>
public sealed class TreeEntryInjectionTests
{
    /// <summary>Verifies entries are injected into root and added items.</summary>
    [Fact]
    public void Binding_tree_entry_injects_itself_into_root_and_added_items()
    {
        var rootItem = new EntryAwareItem();
        var root = new BindingTreeEntry<EntryAwareItem>(rootItem);
        var childItem = new EntryAwareItem();
        BindingTreeEntry<EntryAwareItem> child = root.Add(childItem);

        Assert.Same(root, rootItem.TreeEntry);
        Assert.Same(child, childItem.TreeEntry);
        Assert.Same(root, ((ITreeEntry)child).Parent);
        Assert.Same(childItem, ((ITreeEntry)child).Item);
    }

    /// <summary>Verifies the non-generic entry view navigates children and its indexer.</summary>
    [Fact]
    public void Non_generic_entry_view_navigates_children_and_indexer()
    {
        var root = new BindingTreeEntry<string>("root");
        BindingTreeEntry<string> branch = root.Add("branch");
        BindingTreeEntry<string> leaf = branch.Add("leaf");
        ITreeEntry view = root;

        Assert.Equal(1, view.Count);
        Assert.Same(branch, view[0]);
        Assert.Same(leaf, view.Children.Single().Children.Single());
        Assert.Equal("leaf", view[0].Children.Single().Item);
    }

    /// <summary>Verifies an injected item can control its tree lifecycle.</summary>
    [Fact]
    public void Injected_entry_can_control_its_own_tree_lifecycle_without_generic_knowledge()
    {
        var item = new EntryAwareItem();
        var root = new BindingTreeEntry<EntryAwareItem>(new EntryAwareItem());
        BindingTreeEntry<EntryAwareItem> entry = root.Add(item);

        item.RequestRefresh();
        item.RequestRebuild();
        Assert.True(item.RefreshRequested);
        Assert.True(item.RebuildRequested);
        Assert.Same(entry, item.TreeEntry);
    }

    private sealed class EntryAwareItem : INeedTreeEntry
    {
        public ITreeEntry TreeEntry { get; set; } = null!;
        public bool RefreshRequested { get; private set; }
        public bool RebuildRequested { get; private set; }
        public void RequestRefresh() { TreeEntry.Refresh(); RefreshRequested = true; }
        public void RequestRebuild() { TreeEntry.Rebuild(); RebuildRequested = true; }
    }
}
