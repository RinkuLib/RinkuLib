using RinkuLib.Tests.Documentation;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>Executable examples for docs/articles/mapping/names.md.</summary>
public class AdvancedNameDocumentationTests {
    private sealed record Address(int Zip, string City) : IDbReadable;
    private sealed record NestedPerson(int Id, Address Home);
    private sealed record AlternatePerson(int Id, [Alt("Name")] string Username);
    private sealed record AltAddress([Alt("Postal")] int Zip, string City) : IDbReadable;
    private sealed record AltNestedPerson(int Id, AltAddress Home);
    private sealed record Inner([AltSkippingSegments("Code", 2)] int Code) : IDbReadable;
    private sealed record Middle(Inner Sub) : IDbReadable;
    private sealed record Outer(int Id, Middle Mid);
    private sealed record LayerOne(int First, LayerTwo Two);
    private sealed record LayerTwo([AltUpTo("NotTooDeep", "Two")] int Second, LayerThree Three) : IDbReadable;
    private sealed record LayerThree([AltUpTo("SuperDeep", "Two")] int Third) : IDbReadable;
    private readonly struct Boxed<T>([NoName] T value) {
        public readonly T Value = value;
    }
    private sealed record RuntimeCustomer(string First, string Second) : IDbReadable;

    [Fact]
    [DocumentationExample("names.md", "nested-prefix")]
    public void Nested_members_add_their_name_to_inner_columns() {
        var value = Rows.ParseOne<NestedPerson>([
            new("Id", typeof(int), false),
            new("HomeZip", typeof(int), false),
            new("HomeCity", typeof(string), false)
        ], 1, 90210, "City");

        Assert.Equal(new NestedPerson(1, new Address(90210, "City")), value);
    }

    [Fact]
    [DocumentationExample("names.md", "alternate-name")]
    public void Alt_accepts_the_declared_and_alternate_names() {
        var declared = Rows.ParseOne<AlternatePerson>([
            new("Id", typeof(int), false), new("Username", typeof(string), false)
        ], 1, "first");
        var alternate = Rows.ParseOne<AlternatePerson>([
            new("Id", typeof(int), false), new("Name", typeof(string), false)
        ], 2, "second");

        Assert.Equal(new AlternatePerson(1, "first"), declared);
        Assert.Equal(new AlternatePerson(2, "second"), alternate);
    }

    [Fact]
    [DocumentationExample("names.md", "nested-alternate-name")]
    public void A_nested_alternate_name_keeps_the_outer_prefix() {
        var value = Rows.ParseOne<AltNestedPerson>([
            new("Id", typeof(int), false),
            new("HomePostal", typeof(int), false),
            new("HomeCity", typeof(string), false)
        ], 1, 90210, "City");

        Assert.Equal(new AltNestedPerson(1, new AltAddress(90210, "City")), value);
    }

    [Fact]
    [DocumentationExample("names.md", "skip-prefix-count")]
    public void Alt_skipping_segments_drops_the_inner_prefix() {
        var value = Rows.ParseOne<Outer>([
            new("Id", typeof(int), false), new("MidCode", typeof(int), false)
        ], 1, 9);

        Assert.Equal(9, value.Mid.Sub.Code);
    }

    [Fact]
    [DocumentationExample("names.md", "skip-prefix-name")]
    public void Alt_up_to_stops_at_the_named_path_part() {
        var value = Rows.ParseOne<LayerOne>([
            new("First", typeof(int), false),
            new("NotTooDeep", typeof(int), false),
            new("SuperDeep", typeof(int), false)
        ], 1, 2, 3);

        Assert.Equal(new LayerOne(1, new LayerTwo(2, new LayerThree(3))), value);
    }

    [Fact]
    [DocumentationExample("names.md", "no-name")]
    public void No_name_takes_a_compatible_column() {
        var value = Rows.ParseOne<Boxed<int>>([new("Anything", typeof(int), false)], 5);

        Assert.Equal(5, value.Value);
    }

    [Fact]
    [DocumentationExample("names.md", "runtime-name")]
    public void Runtime_name_rules_can_swap_two_members() {
        var info = TypeParsingInfo.GetOrAdd<RuntimeCustomer>();
        Assert.True(info.UpdateAltName(names => names.GetDefaultName() switch {
            "First" => new NameComparer("Second"),
            "Second" => new NameComparer("First"),
            _ => null
        }));

        var value = Rows.ParseOne<RuntimeCustomer>([
            new("First", typeof(string), false), new("Second", typeof(string), false)
        ], "one", "two");

        Assert.Equal(new RuntimeCustomer("two", "one"), value);
    }
}
