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
        // the chain models nesting: the outer comparer consumes "Address", the inner one "City"
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
        // chain [Root]: the key is found at the chain bottom, so the flat name must consume everything
        Span<INameComparer> chain = [new NameComparer("Root")];
        Assert.True(upTo.Match("Flat", chain));
        Assert.False(upTo.Match("XFlat", chain));
        // chain without the key never matches
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
}
