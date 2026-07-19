using RinkuLib.Commands;
using RinkuLib.Exceptions;
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
        => Refusals.Raises(ErrorCodes.QueryTooShort, () => new QueryCommand("a"));

    [Theory]
    [InlineData("SELECT Name FROM Users WHERE ID >= @Min /*Old*/AND ID > 0 ORDER BY ID")]
    [InlineData("SELECT Name FROM Users WHERE ID >= @Min/*Old*/ AND ID > 0 ORDER BY ID")]
    public void A_marker_before_a_connector_binds_the_condition_on_its_left(string sql) {
        var query = new QueryCommand(sql);

        var off = query.StartBuilder();
        off.Use("@Min", 2);
        Assert.Equal("SELECT Name FROM Users WHERE ID > 0 ORDER BY ID", Render.From(off).CommandText);

        var on = query.StartBuilder();
        on.Use("@Min", 2);
        on.Use("Old");
        Render.Expect(on, "SELECT Name FROM Users WHERE ID >= @Min AND ID > 0 ORDER BY ID", ("@Min", 2));
    }

    [Fact]
    public void A_marker_before_a_connector_prunes_the_left_condition_from_the_doc_example() {
        var query = new QueryCommand("SELECT * FROM tracks WHERE Composer = @composer /*Extra*/AND Milliseconds > @ms");

        var off = query.StartBuilder();
        off.Use("@composer", "x");
        off.Use("@ms", 1);
        Assert.Equal("SELECT * FROM tracks WHERE Milliseconds > @ms", Render.From(off).CommandText);

        var on = query.StartBuilder();
        on.Use("@composer", "x");
        on.Use("@ms", 1);
        on.Use("Extra");
        Render.Expect(on, "SELECT * FROM tracks WHERE Composer = @composer AND Milliseconds > @ms",
            ("@composer", "x"), ("@ms", 1));
    }

    [Fact]
    public void A_variable_the_sql_no_longer_names_is_still_sent_as_a_parameter() {
        var query = new QueryCommand("SELECT * FROM tracks WHERE Composer = @composer /*Extra*/AND Milliseconds > @ms");
        var off = query.StartBuilder();
        off.Use("@composer", "x");
        off.Use("@ms", 1);
        Render.Expect(off, "SELECT * FROM tracks WHERE Milliseconds > @ms", ("@composer", "x"), ("@ms", 1));
    }

    [Fact]
    public void Unclosed_condition_comment_is_rejected()
        => Refusals.Raises(ErrorCodes.UnclosedComment, () => new QueryCommand("SELECT /*Cond FROM Users"));

    [Fact]
    public void Whitespace_only_condition_is_rejected()
        => Refusals.Raises(ErrorCodes.EmptyConditionKey, () => new QueryCommand("SELECT /*A&*/Col FROM Users"));

    [Fact]
    public void Unclosed_literal_comment_is_rejected()
        => Refusals.Raises(ErrorCodes.UnclosedComment, () => new QueryCommand("SELECT /*~ still going FROM Users"));

    [Fact]
    public void Unbalanced_closing_paren_is_rejected()
        => Refusals.Raises(ErrorCodes.UnbalancedScope, () => new QueryCommand("SELECT a) FROM Users"));

    [Fact]
    public void Nesting_deeper_than_the_limit_is_rejected() {
        var query = "SELECT " + new string('(', 65) + "1" + new string(')', 65) + " x";
        Refusals.Raises(ErrorCodes.ScopeTooDeep, () => new QueryCommand(query));
    }

    [Fact]
    public void Always_used_marker_outside_a_projection_is_rejected()
        => Refusals.Raises(ErrorCodes.ProjectionOnlyConstruct, () => new QueryCommand("SELECT A!, B FROM Users"));

    [Fact]
    public void A_literal_comment_is_preserved() {
        var query = new QueryCommand("SELECT /*~ keep me ~*/ Col FROM Users");
        Render.Expect(query.StartBuilder(), "SELECT /* keep me ~*/ Col FROM Users");
    }

    [Fact]
    public void A_section_welded_to_a_condition_footprint_gets_a_separating_space() {
        var query = new QueryCommand("SELECT a, /*Show*/COUNT(b)FROM t");
        var on = query.StartBuilder();
        on.Use("Show");
        Render.Expect(on, "SELECT a, COUNT(b) FROM t");
        Render.Expect(query.StartBuilder(), "SELECT a FROM t");
    }

    [Theory]
    [InlineData("SELECT a, /*Show*/COUNT(b)\tFROM t", "SELECT a, COUNT(b)\tFROM t", "SELECT a\tFROM t")]
    [InlineData("SELECT a, /*Show*/COUNT(b)\r\nFROM t", "SELECT a, COUNT(b)\r\nFROM t", "SELECT a\r\nFROM t")]
    [InlineData("SELECT a, /*Show*/COUNT(b)  FROM t", "SELECT a, COUNT(b)  FROM t", "SELECT a  FROM t")]
    [InlineData("SELECT a, /*Show*/COUNT(b) \t FROM t", "SELECT a, COUNT(b) \t FROM t", "SELECT a \t FROM t")]
    public void Whitespace_kinds_separate_a_section_without_protection(string sql, string kept, string pruned) {
        var on = new QueryCommand(sql).StartBuilder();
        on.Use("Show");
        Render.Expect(on, kept);
        Render.Expect(new QueryCommand(sql).StartBuilder(), pruned);
    }

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
