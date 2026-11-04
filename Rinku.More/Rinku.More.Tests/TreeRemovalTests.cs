using BindingTree;

namespace Rinku.More.Tests;

public class TreeRemovalTests
{
    [Fact]
    public void Detaching_a_branch_does_not_ask_or_notify_its_descendants()
    {
        var root = new BindingTreeEntry<Item>(new());
        var branch = root.Add(new Item());
        var denied = new Item { Allowed = false };
        var child = branch.Add(denied);
        int changes = 0;
        root.Changed += _ => changes++;

        Assert.True(branch.Remove());
        Assert.Null(branch.Parent);
        Assert.Same(branch, child.Parent);
        Assert.Empty(root.Children.ToArray());
        Assert.Equal(1, changes);
        Assert.Equal(0, denied.Checks);
        Assert.Equal(0, denied.RemovedCount);
    }

    [Fact]
    public void Approved_removal_notifies_items_after_detachment()
    {
        var root = new BindingTreeEntry<Item>(new());
        var item = new Item();
        var branch = root.Add(item);
        var child = new Item();
        branch.Add(child);

        Assert.True(branch.Remove());
        Assert.Null(branch.Parent);
        Assert.Equal(1, item.RemovedCount);
        Assert.Equal(0, child.RemovedCount);
        Assert.False(branch.Remove());
        Assert.Equal(1, item.RemovedCount);
    }

    [Fact]
    public void A_denied_node_stays_attached_without_removal_notifications()
    {
        var root = new BindingTreeEntry<Item>(new());
        var denied = new Item { Allowed = false };
        var branch = root.Add(denied);
        Assert.False(branch.Remove());
        Assert.Same(root, branch.Parent);
        Assert.Equal(1, denied.Checks);
        Assert.Equal(0, denied.RemovedCount);
    }

    [Fact]
    public void Moving_and_reordering_do_not_ask_for_removal_or_notify_destruction()
    {
        var left = new BindingTreeEntry<Item>(new());
        var right = new BindingTreeEntry<Item>(new());
        var item = new Item { Allowed = false };
        var branch = left.Add(item);
        left.Add(new Item());

        left.Add(branch);
        Assert.Same(branch, left[1]);
        right.Add(branch);
        Assert.Same(right, branch.Parent);
        Assert.Equal(0, item.Checks);
        Assert.Equal(0, item.RemovedCount);
    }

    [Fact]
    public void Typed_node_injection_supplies_the_actual_mutable_node()
    {
        var item = new Item();
        var node = new BindingTreeEntry<Item>(item);

        Assert.Same(node, item.Node);
        item.Node!.Add(new Item());
        Assert.Equal(1, node.Count);
    }

    [Fact]
    public void Removal_disposes_only_the_removed_item_when_no_observer_is_present()
    {
        var root = new BindingTreeEntry<object>(new object());
        var item = new DisposableItem();
        var branch = root.Add(item);
        var child = new DisposableItem();
        branch.Add(child);
        Assert.True(branch.Remove());
        Assert.Equal(1, item.Disposals);
        Assert.Equal(0, child.Disposals);
        Assert.False(branch.Remove());
        Assert.Equal(1, item.Disposals);
    }

    [Fact]
    public void Removal_observer_takes_precedence_over_disposal()
    {
        var root = new BindingTreeEntry<object>(new object());
        var item = new ObservedDisposableItem();
        var branch = root.Add(item);
        Assert.True(branch.Remove());
        Assert.Equal(1, item.Removals);
        Assert.Equal(0, item.Disposals);
    }

    [Fact]
    public void Moving_a_disposable_item_does_not_dispose_it()
    {
        var left = new BindingTreeEntry<object>(new object());
        var right = new BindingTreeEntry<object>(new object());
        var item = new DisposableItem();
        var branch = left.Add(item);
        right.Add(branch);
        Assert.Equal(0, item.Disposals);
        Assert.Same(right, branch.Parent);
    }

    [Fact]
    public void Explicit_detach_preserves_content_and_children_without_guard_or_callback()
    {
        var root = new BindingTreeEntry<Item>(new());
        var item = new Item { Allowed = false };
        var branch = root.Add(item);
        var child = branch.Add(new Item());
        int changes = 0;
        root.Changed += _ => changes++;
        Assert.True(branch.Detach());
        Assert.Null(branch.Parent);
        Assert.Same(branch, child.Parent);
        Assert.Equal(0, item.Checks);
        Assert.Equal(0, item.RemovedCount);
        Assert.Equal(1, changes);
        Assert.False(branch.Detach());
        root.Add(branch);
        Assert.Same(branch, root[0]);
    }

    [Fact]
    public void Explicit_detach_does_not_dispose_content()
    {
        var root = new BindingTreeEntry<object>(new object());
        var item = new DisposableItem();
        var branch = root.Add(item);
        Assert.True(branch.Detach());
        Assert.Equal(0, item.Disposals);
    }

    private class DisposableItem : IDisposable
    {
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }

    private sealed class ObservedDisposableItem : DisposableItem, ITreeRemovalObserver
    {
        public int Removals { get; private set; }
        public void Removed() => Removals++;
    }

    public class Item : INeedTreeEntry<Item>, ITreeRemovalGuard, ITreeRemovalObserver
    {
        public bool Allowed { get; set; } = true;
        public int Checks { get; private set; }
        public int RemovedCount { get; private set; }
        public IBindingTree<Item>? Node { get; private set; }
        public IBindingTree<Item> TreeEntry { set => Node = value; }
        public bool CanRemove()
        {
            Checks++;
            return Allowed;
        }
        public void Removed() => RemovedCount++;
    }
}
