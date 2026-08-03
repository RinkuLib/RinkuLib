using RinkuLib.DbParsing;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>
/// The comparer chain behind name matching: single names, alternatives, joins, and the helpers that
/// grow or shrink them.
/// </summary>
public class NameComparerTests {
    private static bool Match(INameComparer comparer, string colName)
        => comparer.Match(colName, []);

    [Fact]
    public void NameComparer_matches_its_name_case_insensitively() {
        var comparer = new NameComparer("Total");
        Assert.True(Match(comparer, "Total"));
        Assert.True(Match(comparer, "TOTAL"));
        Assert.False(Match(comparer, "Totals"));
        Assert.False(Match(comparer, "Tot"));
        Assert.Equal("Total", comparer.GetDefaultName());
        Assert.True(comparer.Contains("total"));
        Assert.False(comparer.Contains("other"));
    }

    [Fact]
    public void NoNameComparer_matches_everything_and_names_nothing() {
        var comparer = NoNameComparer.Instance;
        Assert.True(Match(comparer, "Anything"));
        Assert.False(comparer.Contains("Anything"));
    }

    [Fact]
    public void NameTwo_matches_either_name() {
        var comparer = new NameTwo("Name", "Label");
        Assert.True(Match(comparer, "name"));
        Assert.True(Match(comparer, "LABEL"));
        Assert.False(Match(comparer, "Title"));
        Assert.Equal("Name", comparer.GetDefaultName());
        Assert.True(comparer.Contains("Label"));
    }

    [Fact]
    public void NameArray_matches_any_of_its_names() {
        var comparer = new NameArray(["A", "B", "C"]);
        Assert.True(Match(comparer, "a"));
        Assert.True(Match(comparer, "C"));
        Assert.False(Match(comparer, "D"));
        Assert.Equal("A", comparer.GetDefaultName());
    }

    [Fact]
    public void AddAltName_grows_the_structure_step_by_step() {
        INameComparer comparer = NoNameComparer.Instance;
        comparer = comparer.AddAltName("First");
        Assert.IsType<NameComparer>(comparer);
        comparer = comparer.AddAltName("Second");
        Assert.True(Match(comparer, "First"));
        Assert.True(Match(comparer, "Second"));
        comparer = comparer.AddAltName("Third");
        Assert.True(Match(comparer, "Third"));
        Assert.True(Match(comparer, "First"));
    }

    [Fact]
    public void AddAltName_ignores_a_name_already_present() {
        INameComparer comparer = new NameComparer("Name");
        Assert.Same(comparer, comparer.AddAltName("NAME"));
    }

    [Fact]
    public void RemoveName_shrinks_the_structure() {
        INameComparer comparer = new NameTwo("Name", "Label");
        comparer = comparer.RemoveName("Label");
        Assert.True(Match(comparer, "Name"));
        Assert.False(Match(comparer, "Label"));
    }

    [Fact]
    public void RemoveName_without_support_returns_the_same_comparer() {
        INameComparer comparer = new NameMultiSpan("Deep", 2);
        var afterMiss = comparer.RemoveName("Other");
        Assert.Same(comparer, afterMiss);
    }

    [Fact]
    public void JoinedNameComparer_matches_both_sides() {
        var joined = new JoinedNameComparer(new NameComparer("A"), new NameComparer("B"));
        Assert.True(Match(joined, "A"));
        Assert.True(Match(joined, "B"));
        Assert.False(Match(joined, "C"));
        Assert.True(joined.Contains("B"));
    }

    [Fact]
    public void AddComparer_combines_two_structures() {
        INameComparer comparer = new NameComparer("A");
        comparer = comparer.AddComparer(new NameComparer("B"));
        Assert.True(Match(comparer, "A"));
        Assert.True(Match(comparer, "B"));
    }

    [Fact]
    public void AddComparer_ignores_NoName_and_self() {
        INameComparer comparer = new NameComparer("A");
        Assert.Same(comparer, comparer.AddComparer(NoNameComparer.Instance));
        Assert.Same(comparer, comparer.AddComparer(comparer));
        Assert.Same(comparer, NoNameComparer.Instance.AddComparer(comparer));
    }

    [Fact]
    public void RemoveComparer_takes_a_side_out_of_a_join() {
        var b = new NameComparer("B");
        INameComparer joined = new JoinedNameComparer(new NameComparer("A"), b);
        var trimmed = joined.RemoveComparer(b);
        Assert.True(Match(trimmed, "A"));
        Assert.False(Match(trimmed, "B"));
    }

    [Fact]
    public void RemoveComparer_on_itself_leaves_NoName() {
        INameComparer comparer = new NameMultiSpan("Deep", 2);
        Assert.Same(NoNameComparer.Instance, comparer.RemoveComparer(comparer));
    }

    [Fact]
    public void Chained_match_consumes_the_prefix_first() {
        var outer = new NameComparer("Address");
        var inner = new NameComparer("City");
        Span<INameComparer> chain = [outer];
        Assert.True(inner.Match("AddressCity", chain));
        Assert.False(inner.Match("OtherCity", chain));
        Assert.False(inner.Match("City", chain));
    }

    [Fact]
    public void NameArray_removal_shrinks_through_each_shape() {
        INameComparer comparer = new NameArray(["A", "B", "C", "D"]);
        comparer = comparer.RemoveName("D");
        Assert.IsType<NameArray>(comparer);
        comparer = comparer.RemoveName("B");
        Assert.IsType<NameTwo>(comparer);
        Assert.True(Match(comparer, "A"));
        Assert.True(Match(comparer, "C"));
        comparer = comparer.RemoveName("A");
        Assert.IsType<NameComparer>(comparer);
        comparer = comparer.RemoveName("C");
        Assert.IsType<NoNameComparer>(comparer);
    }

    [Fact]
    public void NameArray_removal_of_a_middle_name_keeps_the_rest() {
        INameComparer comparer = new NameArray(["A", "B", "C", "D", "E"]);
        comparer = comparer.RemoveName("C");
        Assert.True(Match(comparer, "A"));
        Assert.False(Match(comparer, "C"));
        Assert.True(Match(comparer, "E"));
    }

    [Fact]
    public void NameComparerGroup_matches_any_child() {
        var group = new NameComparerGroup([new NameComparer("A"), new NameComparer("B"), new NameComparer("C")]);
        Assert.True(Match(group, "b"));
        Assert.False(Match(group, "D"));
        Assert.True(group.Contains("C"));
        Assert.Equal("A", group.GetDefaultName());
    }

    [Fact]
    public void NameMultiSpanKey_matches_only_within_its_key_scope() {
        var upTo = new NameMultiSpanKey("Flat", "Root");
        Assert.Equal("Flat", upTo.GetDefaultName());
        Assert.True(upTo.Contains("flat"));
        Span<INameComparer> chain = [new NameComparer("Root")];
        Assert.True(upTo.Match("Flat", chain));
        Assert.False(upTo.Match("XFlat", chain));
        Span<INameComparer> other = [new NameComparer("Other")];
        Assert.False(upTo.Match("Flat", other));
    }

    [Fact]
    public void NameTwo_removal_keeps_the_other_name() {
        INameComparer comparer = new NameTwo("Name", "Label");
        var afterFirst = ((NameTwo)comparer).TryRemove("Name");
        Assert.NotNull(afterFirst);
        Assert.True(Match(afterFirst, "Label"));
        Assert.False(Match(afterFirst, "Name"));
    }

    [Fact]
    public void Every_simple_comparer_grows_and_shrinks_through_TryAdd_TryRemove() {
        var none = NoNameComparer.Instance;
        Assert.IsType<NameComparer>(none.TryAdd("A"));
        var other = new NameComparer("X");
        Assert.Same(other, none.TryAdd(other));
        Assert.Null(none.TryRemove("A"));
        Assert.Same(NoNameComparer.Instance, none.TryRemove(other));

        var single = new NameComparer("A");
        Assert.Same(single, single.TryAdd("a"));
        Assert.IsType<NameTwo>(single.TryAdd("B"));
        Assert.IsType<JoinedNameComparer>(single.TryAdd(other));
        Assert.Same(NoNameComparer.Instance, single.TryRemove("A"));
        Assert.Null(single.TryRemove("B"));
        Assert.Same(NoNameComparer.Instance, single.TryRemove(single));
        Assert.Null(single.TryRemove(other));

        var two = new NameTwo("A", "B");
        Assert.Same(two, two.TryAdd("b"));
        Assert.IsType<NameArray>(two.TryAdd("C"));
        Assert.IsType<JoinedNameComparer>(two.TryAdd(other));
        Assert.Null(two.TryRemove("C"));
        Assert.Same(NoNameComparer.Instance, two.TryRemove(two));
        Assert.Null(two.TryRemove(other));

        var arr = new NameArray(["A", "B"]);
        Assert.Same(arr, arr.TryAdd("a"));
        var grown = arr.TryAdd("C");
        Assert.True(grown!.Contains("C"));
        Assert.Null(arr.TryAdd(other));                      
        Assert.Null(arr.TryRemove("Z"));
        Assert.IsType<NameComparer>(new NameArray(["A", "B"]).TryRemove("B"));
        Assert.Same(NoNameComparer.Instance, new NameArray(["A"]).TryRemove("A"));
        Assert.Same(NoNameComparer.Instance, arr.TryRemove(arr));
        Assert.Null(arr.TryRemove(other));
        Assert.True(arr.Contains("b"));
        Assert.False(arr.Contains("z"));
    }

    [Fact]
    public void Joined_TryAdd_keeps_both_branches() {
        var joined = new JoinedNameComparer(new NameComparer("A"), new NameComparer("B"));
        var grown = joined.TryAdd("C");
        Assert.NotNull(grown);
        Assert.True(Match(grown, "A"));
        Assert.True(Match(grown, "B"));
        Assert.True(Match(grown, "C"));

        var rigid = new JoinedNameComparer(new NameMultiSpan("M", 1), new NameMultiSpan("N", 1));
        var widened = rigid.TryAdd("C");
        Assert.IsType<NameComparerGroup>(widened);
        Assert.True(Match(widened!, "C"));

        Assert.IsType<NameComparerGroup>(joined.TryAdd(new NameComparer("D")));
    }

    [Fact]
    public void Joined_TryRemove_collapses_to_the_surviving_branch() {
        var a = new NameComparer("A");
        var b = new NameComparer("B");
        var joined = new JoinedNameComparer(a, b);
        Assert.Same(b, joined.TryRemove("A"));
        Assert.Same(a, joined.TryRemove("B"));
        Assert.Null(joined.TryRemove("Z"));
        Assert.Same(b, joined.TryRemove(a));
        Assert.Null(joined.TryRemove(new NameComparer("X")));

        var nested = new JoinedNameComparer(new NameTwo("A", "B"), new NameComparer("C"));
        var trimmed = nested.TryRemove("B");
        Assert.IsType<JoinedNameComparer>(trimmed);
        Assert.True(Match(trimmed!, "A"));
        Assert.False(Match(trimmed!, "B"));
        Assert.True(Match(trimmed!, "C"));
    }

    [Fact]
    public void Group_TryAdd_prefers_growing_a_child_then_appends() {
        var group = new NameComparerGroup([new NameComparer("A"), new NameComparer("B")]);
        Assert.Same(group, group.TryAdd("C"));  
        Assert.True(Match(group, "C"));

        var rigid = new NameComparerGroup([new NameMultiSpan("M", 1)]);
        var appended = rigid.TryAdd("C");
        Assert.IsType<NameComparerGroup>(appended);
        Assert.NotSame(rigid, appended);
        Assert.True(Match(appended!, "C"));

        var mergedGroup = group.TryAdd(new NameComparerGroup([new NameComparer("X")]));
        Assert.True(Match(mergedGroup!, "X"));
        var mergedJoin = group.TryAdd(new JoinedNameComparer(new NameComparer("Y"), new NameComparer("Z")));
        Assert.True(Match(mergedJoin!, "Y"));
        var appendedOne = group.TryAdd(new NameMultiSpan("W", 1));
        Assert.IsType<NameComparerGroup>(appendedOne);
    }

    [Fact]
    public void Group_TryRemove_shrinks_or_drops_a_child() {
        var group = new NameComparerGroup([new NameTwo("A", "B"), new NameComparer("C")]);
        Assert.Same(group, group.TryRemove("B"));  
        Assert.False(Match(group, "B"));
        Assert.True(Match(group, "A"));

        var dropping = new NameComparerGroup([new NameComparer("A"), new NameComparer("B")]);
        var dropped = dropping.TryRemove("A");
        Assert.IsType<NameComparerGroup>(dropped);
        Assert.False(Match(dropped!, "A"));
        Assert.True(Match(dropped!, "B"));

        Assert.Null(new NameComparerGroup([new NameMultiSpan("M", 1)]).TryRemove("Z"));

        var b = new NameComparer("B");
        var byComparer = new NameComparerGroup([new NameComparer("A"), b]);
        var afterDrop = byComparer.TryRemove(b);
        Assert.False(Match(afterDrop!, "B"));
        Assert.Null(new NameComparerGroup([new NameComparer("A")]).TryRemove(new NameComparer("X")));

        var shrinking = new NameComparerGroup([new JoinedNameComparer(new NameComparer("A"), b), new NameComparer("C")]);
        Assert.Same(shrinking, shrinking.TryRemove(b));
        Assert.False(Match(shrinking, "B"));
        Assert.True(Match(shrinking, "A"));
    }

    [Fact]
    public void MultiSpan_skips_segments_and_MultiSpanKey_removes_by_comparer() {
        var deep = new NameMultiSpan("City", 2);
        Span<INameComparer> chain = [new NameComparer("Root"), new NameComparer("Skip")];
        Assert.True(deep.Match("RootCity", chain));
        Assert.False(deep.Match("RootSkipCity", chain));
        Assert.Same(NoNameComparer.Instance, deep.TryRemove("city"));
        Assert.Null(deep.TryRemove("other"));
        Assert.Null(deep.TryRemove(new NameComparer("City")));
        Assert.Equal("City", deep.GetDefaultName());

        var keyed = new NameMultiSpanKey("Flat", "Root");
        Assert.Same(NoNameComparer.Instance, keyed.TryRemove("flat"));
        Assert.Null(keyed.TryRemove("other"));
        Assert.Same(NoNameComparer.Instance, keyed.TryRemove(new NameMultiSpanKey("Flat", "Other")));
        Assert.Null(keyed.TryRemove(new NameComparer("Flat")));

        Span<INameComparer> tall = [new NameComparer("Base"), new NameComparer("Root"), new NameComparer("Mid")];
        Assert.True(keyed.Match("BaseFlat", tall));
        Assert.False(keyed.Match("OtherFlat", tall));
    }

    [Fact]
    public void NameArray_removal_from_each_position_of_a_wide_array() {
        Assert.True(Match(new NameArray(["A", "B", "C", "D", "E"]).TryRemove("A")!, "E")); 
        Assert.True(Match(new NameArray(["A", "B", "C", "D", "E"]).TryRemove("E")!, "A")); 
        var afterFirst = new NameArray(["A", "B", "C", "D", "E"]).TryRemove("A");
        Assert.False(Match(afterFirst!, "A"));
        var afterLast = new NameArray(["A", "B", "C", "D", "E"]).TryRemove("E");
        Assert.False(Match(afterLast!, "E"));
    }

    [Fact]
    public void NameArray_of_three_shrinks_from_each_position() {
        var middle = new NameArray(["A", "B", "C"]).TryRemove("B");
        Assert.IsType<NameTwo>(middle);
        Assert.True(Match(middle!, "A"));
        Assert.True(Match(middle!, "C"));
        var last = new NameArray(["A", "B", "C"]).TryRemove("C");
        Assert.IsType<NameTwo>(last);
        Assert.True(Match(last!, "B"));
        var first = new NameArray(["A", "B", "C"]).TryRemove("A");
        Assert.IsType<NameTwo>(first);
        Assert.False(Match(first!, "A"));
    }

    sealed record RigidComparer(string Name) : INameComparer {
        public string GetDefaultName() => Name;
        public bool Match(ReadOnlySpan<char> colName, Span<INameComparer> nameComparers)
            => colName.SequenceEqual(Name);
        public bool Contains(string name) => string.Equals(Name, name, StringComparison.OrdinalIgnoreCase);
    }

    sealed record RefusingAdder(string Name) : INameComparerThatCanAdd {
        public string GetDefaultName() => Name;
        public bool Match(ReadOnlySpan<char> colName, Span<INameComparer> nameComparers)
            => colName.SequenceEqual(Name);
        public bool Contains(string name) => string.Equals(Name, name, StringComparison.OrdinalIgnoreCase);
        public INameComparer? TryAdd(string name) => null;
    }

    [Fact]
    public void The_helpers_fall_back_when_a_comparer_refuses_or_cannot_mutate() {
        var rigid = new RigidComparer("R");
        Assert.IsType<JoinedNameComparer>(rigid.AddAltName("X"));        
        var refusing = new RefusingAdder("R");
        Assert.IsType<JoinedNameComparer>(refusing.AddAltName("X"));      

        var arr = new NameArray(["A", "B"]);
        var joinedViaArray = arr.AddComparer(new RigidComparer("Z"));  
        Assert.IsType<JoinedNameComparer>(joinedViaArray);
        Assert.True(Match(joinedViaArray, "Z"));

        Assert.Same(NoNameComparer.Instance, rigid.RemoveComparer(rigid)); 
        Assert.Same(rigid, rigid.RemoveComparer(new RigidComparer("Q")));
        Assert.Same(rigid, rigid.RemoveName("R"));                       
    }

    [Fact]
    public void Joined_grows_its_main_branch_when_the_alt_cannot() {
        var joined = new JoinedNameComparer(new NameComparer("A"), new NameMultiSpan("M", 1));
        var grown = joined.TryAdd("X");
        Assert.IsType<JoinedNameComparer>(grown);
        Assert.True(Match(grown!, "A"));
        Assert.True(Match(grown!, "X"));
        Assert.True(grown!.Contains("M"));
    }

    [Fact]
    public void Joined_removal_of_a_shared_comparer_trims_both_branches() {
        var b = new NameComparer("B");
        var joined = new JoinedNameComparer(
            new JoinedNameComparer(new NameComparer("A"), b),
            new JoinedNameComparer(b, new NameComparer("C")));
        var trimmed = joined.TryRemove(b);
        Assert.NotNull(trimmed);
        Assert.True(Match(trimmed!, "A"));
        Assert.True(Match(trimmed!, "C"));
        Assert.False(Match(trimmed!, "B"));
    }

    [Fact]
    public void A_group_walks_past_children_that_cannot_do_the_operation() {
        var rigid = new RigidComparer("R");

        var toAdd = new NameComparerGroup([rigid, new NameComparer("A")]);
        Assert.Same(toAdd, toAdd.TryAdd("B"));
        Assert.True(Match(toAdd, "B"));

        var toRemove = new NameComparerGroup([rigid, new NameTwo("A", "B")]);
        Assert.Same(toRemove, toRemove.TryRemove("B"));
        Assert.False(Match(toRemove, "B"));
        Assert.True(Match(toRemove, "A"));

        var target = new NameComparer("C");
        var byComparer = new NameComparerGroup([rigid, new JoinedNameComparer(new NameComparer("D"), target)]);
        var trimmed = byComparer.TryRemove(target);
        Assert.NotNull(trimmed);
        Assert.False(Match(trimmed!, "C"));
        Assert.True(Match(trimmed!, "D"));
    }

    [Fact]
    public void A_group_walks_past_a_child_that_refuses_to_grow() {
        var group = new NameComparerGroup([new RefusingAdder("R"), new NameComparer("A")]);
        Assert.Same(group, group.TryAdd("B"));
        Assert.True(Match(group, "B"));
        Assert.True(Match(group, "A"));
    }

    [Fact]
    public void A_group_walks_past_a_child_that_does_not_hold_the_name() {
        var group = new NameComparerGroup([new NameComparer("A"), new NameComparer("B")]);
        var dropped = group.TryRemove("B");
        Assert.NotNull(dropped);
        Assert.True(Match(dropped!, "A"));
        Assert.False(Match(dropped!, "B"));
    }

    [Fact]
    public void NameMultiSpanKey_refuses_a_name_it_does_not_end_with() {
        var keyed = new NameMultiSpanKey("Flat", "Root");
        Span<INameComparer> chain = [new NameComparer("Root")];
        Assert.False(keyed.Match("Other", chain));
    }

    [Fact]
    public void NoName_and_group_edges_of_the_match_chain() {
        Span<INameComparer> chain = [new NameComparer("Root")];
        Assert.False(NoNameComparer.Instance.Match("Left", chain));
        Assert.True(NoNameComparer.Instance.Match("Root", chain));
        Assert.Same(NoNameComparer.Instance, NoNameComparer.Instance.TryRemove(new NameComparer("X")));
        Assert.Null(((IMutatableNameComparer)NoNameComparer.Instance).TryRemove("X"));
    }
}
