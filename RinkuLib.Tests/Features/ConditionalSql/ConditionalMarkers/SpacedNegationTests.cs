using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Templating;

/// <summary>
/// A <c>!</c> takes the rest of the marker as its key, spaces included, so where the space falls decides
/// which key the negation is on.
/// </summary>
public class SpacedNegationTests {
    const string Spaced = "SELECT * FROM products WHERE /*! All*/IsActive = 1";
    const string Tight = "SELECT * FROM products WHERE /*!All*/IsActive = 1";

    [Fact]
    public void A_tight_bang_negates_the_key_it_touches() {
        var query = new QueryCommand(Tight);
        Assert.Equal(["All"], query.Mapper.Keys.ToArray());
        Render.Expect(query.StartBuilder(), "SELECT * FROM products WHERE IsActive = 1");
        var on = query.StartBuilder();
        Assert.True(on.Use("All"));
        Render.Expect(on, "SELECT * FROM products");
    }

    [Fact]
    public void A_spaced_bang_takes_the_space_into_the_key() {
        var query = new QueryCommand(Spaced);
        Assert.Equal([" All"], query.Mapper.Keys.ToArray());
        Assert.False(query.StartBuilder().Use("All"));
        var on = query.StartBuilder();
        Assert.True(on.Use(" All"));
        Render.Expect(on, "SELECT * FROM products");
        Render.Expect(query.StartBuilder(), "SELECT * FROM products WHERE IsActive = 1");
    }

    /// <summary>Away from the negation, spaces around a key are trimmed.</summary>
    [Fact]
    public void Spaces_around_a_plain_key_are_trimmed() {
        Assert.Equal(["Cheap", "Pricey"], new QueryCommand("SELECT * FROM t WHERE /* Cheap | Pricey */a = 1").Mapper.Keys.ToArray());
        Assert.Equal(["Cheap", "Pricey"], new QueryCommand("SELECT * FROM t WHERE /*Cheap|Pricey*/a = 1").Mapper.Keys.ToArray());
    }
}
