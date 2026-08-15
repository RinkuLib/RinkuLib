using RinkuLib.Tests.Documentation;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>Executable examples for docs/articles/mapping/reading-order.md.</summary>
public class AdvancedReadingOrderDocumentationTests {
    private sealed record FreePerson(int Id, string Name, string? Email = null);
    private sealed record TuplePerson(int Id, string Name, string? Email = null) : IDbReadable;
    private sealed record Entry(int Id, [CanNotLookAnywhere] int? Code = null);
    private readonly record struct PairPerson(int Id, string Name) : IDbReadable;
    private readonly record struct Address([CanLookAnywhere] int Zip, string City) : IDbReadable;
    private sealed record Money([Alt("Amount"), MayReuseCol] int Copy, int Amount);
    private sealed record Inner(int A, int? B = null) : IDbReadable;
    private sealed record Holder(int Key, [CanLookAnywhereSubtree] Inner Data) : IDbReadable;
    private sealed record RuntimePerson(int Id, int? Code = null) : IDbReadable;
    private sealed class CustomFallbackSlot : ParamInfo {
        public CustomFallbackSlot() : base(typeof(int), NotNullHandle.Instance, new NameComparer("Value")) { }
        public override DbItemPlan? FallbackTryGetParser(Type type) => DefaultValueFallback.Instance.FallbackTryGetParser(type);
    }

    [Fact]
    [DocumentationExample("reading-order.md", "free-reading")]
    public void Normal_objects_find_named_columns_across_order_and_gaps() {
        var value = Rows.ParseOne<FreePerson>([
            new("Name", typeof(string), false),
            new("Note", typeof(string), false),
            new("Id", typeof(int), false)
        ], "Name", "ignored", 1);

        Assert.Equal(new FreePerson(1, "Name", null), value);
    }

    [Fact]
    [DocumentationExample("reading-order.md", "sequential-tuple")]
    public void Tuple_elements_read_consecutive_runs() {
        var value = Rows.ParseOne<(TuplePerson, TuplePerson)>([
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
            new("ID", typeof(int), false),
            new("NAME", typeof(string), false),
            new("Email", typeof(string), false)
        ], 1, "One", 2, "Two", "two@example.com");

        Assert.Equal(new TuplePerson(1, "One", null), value.Item1);
        Assert.Equal(new TuplePerson(2, "Two", "two@example.com"), value.Item2);
    }

    [Fact]
    [DocumentationExample("reading-order.md", "sequential-slot")]
    public void Sequential_slot_only_checks_the_next_column() {
        var value = Rows.ParseOne<Entry>([
            new("Id", typeof(int), false),
            new("Other", typeof(int), false),
            new("Code", typeof(int), false)
        ], 1, 8, 9);

        Assert.Equal(new Entry(1, null), value);
    }

    [Fact]
    [DocumentationExample("reading-order.md", "free-slot")]
    public void Free_slot_can_anchor_after_a_gap() {
        var value = Rows.ParseOne<(PairPerson, Address)>([
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
            new("Note", typeof(string), false),
            new("Zip", typeof(int), false),
            new("City", typeof(string), false)
        ], 1, "Name", "ignored", 90210, "City");

        Assert.Equal(new PairPerson(1, "Name"), value.Item1);
        Assert.Equal(new Address(90210, "City"), value.Item2);
    }

    [Fact]
    [DocumentationExample("reading-order.md", "reusable-slot")]
    public void Reusable_slot_does_not_block_a_normal_slot_after_it() {
        var value = Rows.ParseOne<Money>([new("Amount", typeof(int), false)], 12);

        Assert.Equal(new Money(12, 12), value);
    }

    [Fact]
    [DocumentationExample("reading-order.md", "subtree-reading")]
    public void Subtree_rule_reaches_every_nested_slot() {
        var value = Rows.ParseOne<(int, Holder)>([
            new("X", typeof(int), false),
            new("Key", typeof(int), false),
            new("Junk", typeof(int), false),
            new("DataA", typeof(int), false),
            new("Gap", typeof(int), false),
            new("DataB", typeof(int), false)
        ], 0, 1, 8, 2, 9, 3);

        Assert.Equal(new Holder(1, new Inner(2, 3)), value.Item2);
    }

    [Fact]
    [DocumentationExample("reading-order.md", "runtime-reading")]
    public void Runtime_rule_replaces_the_selected_slot() {
        var info = Assert.IsAssignableFrom<ICanProvideConstructions>(TypeParsingInfo.GetOrAdd<RuntimePerson>());
        MethodCtorInfo path = info.PossibleConstructors[0];
        ParamInfo original = path.Parameters[1];
        path.Parameters[1] = original.WithColModifier(FlagUpdater.SequentialRead);

        var changed = Assert.IsType<ParamInfoPlus>(path.Parameters[1]);
        Assert.Same(original.NameComparer, changed.NameComparer);
        Assert.Same(original.NullColHandler, changed.NullColHandler);
        Assert.Same(DefaultValueFallback.Instance, changed.FallbackParserGetter);

        var value = Rows.ParseOne<RuntimePerson>([
            new("Id", typeof(int), false),
            new("Other", typeof(int), false),
            new("Code", typeof(int), false)
        ], 1, 8, 9);

        Assert.Equal(new RuntimePerson(1, null), value);
    }

    [Fact]
    public void Changing_a_custom_slot_modifier_preserves_its_fallback_contract() {
        var original = new CustomFallbackSlot();

        ParamInfo changed = original.WithColModifier(FlagUpdater.SequentialRead);

        Assert.NotNull(changed.FallbackTryGetParser(typeof(int)));
        var modifier = new ColModifier();
        changed.UpdateColModifier(ref modifier);
        Assert.True(modifier.Flags.HasFlag(UsageFlags.SequentialRead));
    }
}
