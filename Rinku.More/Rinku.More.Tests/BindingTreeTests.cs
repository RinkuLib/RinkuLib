using BindingTree;

namespace Rinku.More.Tests;

/// <summary>Tests binding-tree ordering and change propagation.</summary>
public sealed class BindingTreeTests
{
    /// <summary>Verifies shared order preserves parents before descendants and respects sibling priority.</summary>
    [Fact]
    public void Shared_order_preserves_parent_before_child_and_sibling_priority()
    {
        var root = new BindingTreeEntry<string>("Root");
        var a = root.Add("A");
        var b = root.Add("B");
        a.Add("X");
        a.Add("Y");
        b.Add("Z");

        Assert.Equal(["A", "X", "B", "Y", "Z"], root.SharedOrder().Select(entry => entry.Item));
    }

    /// <summary>Verifies adding and removing a branch publishes the expected changes and parent state.</summary>
    [Fact]
    public void Adding_and_removing_a_branch_publishes_one_change_and_preserves_parent_during_remove()
    {
        var root = new BindingTreeEntry<string>("Root");
        var branch = new BindingTreeEntry<string>("Branch");
        branch.Add("Child");
        List<(TreeChangeKind Kind, ITreeEntry<string> Entry, ITreeEntry<string>? Parent)> changes = [];

        root.Changed += change => changes.Add((change.Kind, change.Entry, change.Entry.Parent));

        root.Add(branch);
        root.Remove(branch);

        Assert.Equal([TreeChangeKind.Add, TreeChangeKind.Remove], changes.Select(change => change.Kind));
        Assert.Same(branch, changes[0].Entry);
        Assert.Same(root, changes[0].Parent);
        Assert.Same(root, changes[1].Parent);
        Assert.Null(branch.Parent);
    }

    /// <summary>Verifies moving an existing sibling keeps the tree contiguous.</summary>
    [Fact]
    public void Moving_an_existing_sibling_to_the_end_keeps_the_tree_contiguous()
    {
        var root = new BindingTreeEntry<string>("Root");
        var a = root.Add("A");
        root.Add("B");
        root.Add("C");

        root.Insert(root.Count, a);

        Assert.Equal(["B", "C", "A"], root.Children.ToArray().Select(entry => entry.Item));
    }

}
