using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Templating;

/// <summary>
/// Malformed templates the parser must reject, and quoting/nesting constructs it must scan past without
/// treating their contents as markers.
/// </summary>
public class ParsingEdgeCaseTests {
    [Fact]
    public void Query_shorter_than_two_characters_is_rejected()
        => Assert.Throws<Exception>(() => new QueryCommand("a"));

    [Fact]
    public void Unclosed_condition_comment_is_rejected()
        => Assert.Throws<Exception>(() => new QueryCommand("SELECT /*Cond FROM Users"));

    [Fact]
    public void Whitespace_only_condition_is_rejected()
        => Assert.Throws<Exception>(() => new QueryCommand("SELECT /*A&*/Col FROM Users"));

    [Fact]
    public void Unclosed_literal_comment_is_rejected()
        => Assert.Throws<Exception>(() => new QueryCommand("SELECT /*~ still going FROM Users"));

    [Fact]
    public void Unbalanced_closing_paren_is_rejected()
        => Assert.Throws<Exception>(() => new QueryCommand("SELECT a) FROM Users"));

    [Fact]
    public void Nesting_deeper_than_the_limit_is_rejected() {
        var query = "SELECT " + new string('(', 65) + "1" + new string(')', 65) + " x";
        Assert.Throws<Exception>(() => new QueryCommand(query));
    }

    [Fact]
    public void Always_used_marker_outside_a_projection_is_rejected()
        => Assert.Throws<Exception>(() => new QueryCommand("SELECT A!, B FROM Users"));

    [Fact]
    public void A_literal_comment_is_preserved() {
        var query = new QueryCommand("SELECT /*~ keep me ~*/ Col FROM Users");
        Render.Expect(query.StartBuilder(), "SELECT /* keep me ~*/ Col FROM Users");
    }

    [Fact]
    public void A_section_welded_to_a_condition_footprint_gets_a_separating_space() {
        // "COUNT(b)" ends in ")" and abuts FROM with no space. Kept, it stays valid; pruned, the preceding
        // "a" must not weld onto FROM. The parser inserts a separating space before the section so removing
        // the conditional segment is safe whatever precedes it.
        var query = new QueryCommand("SELECT a, /*Show*/COUNT(b)FROM t");
        var on = query.StartBuilder();
        on.Use("Show");
        Render.Expect(on, "SELECT a, COUNT(b) FROM t");
        Render.Expect(query.StartBuilder(), "SELECT a FROM t");
    }

    // The wall is only removed, so the space before it stays. A space before the wall therefore leaves a
    // double space once the footprint prunes; only a wall tight against the token gives a single space.
    [Theory]
    [InlineData("SELECT DISTINCT ??? /*ShowId*/TrackId, Name FROM tracks", "SELECT DISTINCT  Name FROM tracks")]
    [InlineData("SELECT DISTINCT ???/*ShowId*/TrackId, Name FROM tracks", "SELECT DISTINCT  Name FROM tracks")]
    [InlineData("SELECT DISTINCT??? /*ShowId*/TrackId, Name FROM tracks", "SELECT DISTINCT Name FROM tracks")]
    [InlineData("SELECT DISTINCT???/*ShowId*/TrackId, Name FROM tracks", "SELECT DISTINCT Name FROM tracks")]
    public void The_wall_spacing_combinations_when_pruned(string sql, string expected)
        => Render.Expect(new QueryCommand(sql).StartBuilder(), expected);

    [Fact]
    public void Bracket_quoted_identifiers_pass_through() {
        var query = new QueryCommand("SELECT [My Col] FROM [My Table]");
        Render.Expect(query.StartBuilder(), "SELECT [My Col] FROM [My Table]");
    }

    [Fact]
    public void String_and_backtick_literals_pass_through() {
        var query = new QueryCommand("SELECT 'a value', `col`, \"dq\" FROM Users");
        Render.Expect(query.StartBuilder(), "SELECT 'a value', `col`, \"dq\" FROM Users");
    }

}
