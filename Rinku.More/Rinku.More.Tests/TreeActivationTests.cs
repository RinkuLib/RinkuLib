using BindingTree;

namespace Rinku.More.Tests;

public sealed class TreeActivationTests
{
    [Fact]
    public void Activation_reaches_ancestor_listeners_without_changing_the_tree()
    {
        var root = new BindingTreeEntry<string>("Root");
        var branch = root.Add("Branch");
        var leaf = branch.Add("Leaf");
        ITreeEntry<string>? rootRequest = null;
        ITreeEntry<string>? branchRequest = null;
        int changes = 0;
        root.Changed += _ => changes++;
        root.ActivationRequested += entry => rootRequest = entry;
        branch.ActivationRequested += entry => branchRequest = entry;
        ((ITreeActivation)leaf).Activate();
        Assert.Same(leaf, rootRequest);
        Assert.Same(leaf, branchRequest);
        Assert.Equal(0, changes);
        var other = new BindingTreeEntry<string>("Other");
        other.Add(branch);
        rootRequest = null;
        leaf.Activate();
        Assert.Null(rootRequest);
        Assert.Same(leaf, branchRequest);
    }
}
