using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Templating;

/// <summary>
/// The templates the error reference names, run as written, so the code each is filed under stays true.
/// The behaviour behind a code is covered with its own concept, this holds the pairing of template to code
/// and the figures the reference quotes.
/// </summary>
public class ErrorDocExampleTests {
    [Theory]
    [InlineData("SELECT /*IncludeEmail FROM Users", ErrorCodes.UnclosedComment)]
    [InlineData("SELECT /*~ index hint FROM Users", ErrorCodes.UnclosedComment)]
    [InlineData("SELECT a) FROM Users", ErrorCodes.UnbalancedScope)]
    [InlineData("SELECT (a)) FROM Users", ErrorCodes.UnbalancedScope)]
    [InlineData("SELECT CASE WHEN a = 1 THEN 1 ELSE 0 END END FROM t", ErrorCodes.UnbalancedScope)]
    [InlineData("SELECT a END FROM t", ErrorCodes.UnbalancedScope)]
    [InlineData("SELECT /**/Col FROM Users", ErrorCodes.EmptyConditionKey)]
    [InlineData("SELECT /*   */Col FROM Users", ErrorCodes.EmptyConditionKey)]
    [InlineData("SELECT /*IsAdmin&*/Col FROM Users", ErrorCodes.EmptyConditionKey)]
    [InlineData("SELECT /*&IsAdmin*/Col FROM Users", ErrorCodes.EmptyConditionKey)]
    [InlineData("SELECT /*IsAdmin|*/Col FROM Users", ErrorCodes.EmptyConditionKey)]
    [InlineData("SELECT /*|IsAdmin*/Col FROM Users", ErrorCodes.EmptyConditionKey)]
    [InlineData("SELECT /*IsAdmin&&IsOwner*/Col FROM Users", ErrorCodes.EmptyConditionKey)]
    [InlineData("SELECT * FROM tracks WHERE GenreId IN (@genreIds_Q)", ErrorCodes.UnknownHandlerSuffix)]
    [InlineData("SELECT TrackId!, Name FROM tracks", ErrorCodes.ProjectionOnlyConstruct)]
    [InlineData("SELECT a FROM t WHERE /*@Nope*/x = 1", ErrorCodes.ConditionVariableNotInQuery)]
    public void The_broken_example_raises_the_code_it_is_filed_under(string sql, string code)
        => Refusals.Raises(code, () => new QueryCommand(sql));

    [Theory]
    [InlineData("SELECT /*IncludeEmail*/Email, Name FROM Users")]
    [InlineData("SELECT /*~ index hint */Name FROM Users")]
    [InlineData("SELECT a FROM Users")]
    [InlineData("SELECT /*IsAdmin*/Col FROM Users")]
    [InlineData("SELECT /*IsAdmin&IsOwner*/Col FROM Users")]
    [InlineData("SELECT * FROM tracks WHERE GenreId IN (@genreIds_X)")]
    [InlineData("?SELECT TrackId!, Name FROM tracks")]
    [InlineData("SELECT TrackId, Name FROM tracks")]
    [InlineData("SELECT CASE WHEN a IN (SELECT x FROM u) THEN 1 ELSE 0 END FROM t")]
    [InlineData("SELECT CASE WHEN a IN (SELECT x FROM (SELECT y FROM t) i) THEN 1 ELSE 0 END FROM u")]
    [InlineData("SELECT a FROM t WHERE /*@Min*/x >= @Min")]
    [InlineData("SELECT a FROM t WHERE /*Recent*/x = 1")]
    public void The_corrected_example_parses(string sql)
        => Assert.NotNull(new QueryCommand(sql));

    static string Nested(int n) => "SELECT " + new string('(', n) + "1" + new string(')', n) + " x";

    static string NestedCase(int n)
        => "SELECT " + string.Concat(Enumerable.Repeat("CASE WHEN a = 1 THEN ", n)) + "1"
         + string.Concat(Enumerable.Repeat(" ELSE 0 END", n)) + " x FROM t";

    /// <summary>
    /// The depth the reference quotes, held from both sides so the number cannot drift from the parser.
    /// </summary>
    [Fact]
    public void Nesting_reaches_63_scopes_and_stops_there() {
        Assert.NotNull(new QueryCommand(Nested(63)));
        Refusals.Raises(ErrorCodes.ScopeTooDeep, () => new QueryCommand(Nested(64)));

        Assert.NotNull(new QueryCommand(NestedCase(63)));
        Refusals.Raises(ErrorCodes.ScopeTooDeep, () => new QueryCommand(NestedCase(64)));
    }

    /// <summary>
    /// The reference shows a statement deep in <c>CASE</c> with no parenthesis in it, since counting
    /// parentheses is the assumption that hides this one. Nesting 63 of them parses and 64 does not,
    /// exactly as with <c>(</c>.
    /// </summary>
    [Fact]
    public void Case_nests_with_no_parenthesis_in_the_statement() {
        var three = "SELECT CASE WHEN a = 1 THEN CASE WHEN b = 2 THEN CASE WHEN c = 3 THEN 1 ELSE 0 END ELSE 0 END ELSE 0 END FROM t";
        Assert.DoesNotContain("(", three);
        Assert.NotNull(new QueryCommand(three));

        Assert.DoesNotContain("(", NestedCase(63));
        Assert.NotNull(new QueryCommand(NestedCase(63)));
        Refusals.Raises(ErrorCodes.ScopeTooDeep, () => new QueryCommand(NestedCase(64)));
    }

    static string Mixed(int cases, int parens)
        => "SELECT " + string.Concat(Enumerable.Repeat("CASE WHEN a = 1 THEN ", cases))
         + new string('(', parens) + "1" + new string(')', parens)
         + string.Concat(Enumerable.Repeat(" ELSE 0 END", cases)) + " x FROM t";

    /// <summary>
    /// <c>CASE</c> and <c>(</c> draw on one budget rather than two, which the reference states and this
    /// holds from both sides: 31 around 32 is the last that parses, one more of either does not.
    /// </summary>
    [Fact]
    public void Case_and_parentheses_share_one_depth_budget() {
        Assert.NotNull(new QueryCommand(Mixed(31, 32)));
        Refusals.Raises(ErrorCodes.ScopeTooDeep, () => new QueryCommand(Mixed(32, 32)));
        Refusals.Raises(ErrorCodes.ScopeTooDeep, () => new QueryCommand(Mixed(31, 33)));

        Assert.NotNull(new QueryCommand(Mixed(63, 0)));
        Assert.NotNull(new QueryCommand(Mixed(0, 63)));
    }

    /// <summary>Depth is what is open at once, so siblings stay at depth 1 however many there are.</summary>
    [Fact]
    public void Sibling_scopes_do_not_accumulate_depth() {
        var siblings = "SELECT " + string.Concat(Enumerable.Repeat("(1)", 5000)) + " x";
        Assert.NotNull(new QueryCommand(siblings));
    }

    /// <summary>
    /// A handler renders SQL from its value, so a run that supplies nothing for one has nothing to render.
    /// Marking it optional prunes the footprint instead.
    /// </summary>
    [Fact]
    public void A_handler_variable_needs_a_value_unless_it_is_optional() {
        var required = new QueryCommand("SELECT * FROM tracks WHERE GenreId IN (@genreIds_X)");
        Refusals.Raises(ErrorCodes.RequiredHandlerValue, () => Render.From(required.StartBuilder()));

        var optional = new QueryCommand("SELECT * FROM tracks WHERE GenreId IN (?@genreIds_X)");
        Assert.Equal("SELECT * FROM tracks", Render.From(optional.StartBuilder()).CommandText);

        var supplied = required.StartBuilder();
        supplied.Use("@genreIds", new[] { 7, 8 });
        Assert.Equal("SELECT * FROM tracks WHERE GenreId IN (@genreIds_1, @genreIds_2)",
            Render.From(supplied).CommandText);
    }

    /// <summary>
    /// <c>_N</c> writes a number, so it refuses a value it cannot convert to one, while <c>_S</c> and
    /// <c>_R</c> take any value.
    /// </summary>
    [Fact]
    public void The_number_handler_refuses_a_value_it_cannot_write() {
        var number = new QueryCommand("SELECT * FROM tracks WHERE Milliseconds > @ms_N");

        var ok = number.StartBuilder();
        ok.Use("@ms", 46);
        Assert.Equal("SELECT * FROM tracks WHERE Milliseconds > 46", Render.From(ok).CommandText);

        var converted = number.StartBuilder();
        converted.Use("@ms", "46");
        Assert.Equal("SELECT * FROM tracks WHERE Milliseconds > 46", Render.From(converted).CommandText);

        var unconvertible = number.StartBuilder();
        unconvertible.Use("@ms", "abc");
        Refusals.Raises(ErrorCodes.HandlerValueType, () => Render.From(unconvertible));

        var wrong = number.StartBuilder();
        wrong.Use("@ms", new object());
        Refusals.Raises(ErrorCodes.HandlerValueType, () => Render.From(wrong));

        var text = new QueryCommand("SELECT * FROM tracks WHERE Composer = @name_S");
        var quoted = text.StartBuilder();
        quoted.Use("@name", 46);
        Assert.Equal("SELECT * FROM tracks WHERE Composer = '46'", Render.From(quoted).CommandText);
    }

    /// <summary>A literal comment closes with <c>*/</c>.</summary>
    [Fact]
    public void A_literal_comment_survives_into_the_rendered_sql() {
        var query = new QueryCommand("SELECT /*~ index hint */Name FROM Users");
        Assert.Equal("SELECT /* index hint */Name FROM Users", Render.From(query.StartBuilder()).CommandText);
    }

    /// <summary>Marking the term that owns a subquery prunes the whole condition.</summary>
    [Fact]
    public void Marking_the_term_that_owns_a_subquery_prunes_it_whole() {
        var query = new QueryCommand("SELECT * FROM tracks WHERE /*Recent*/AlbumId IN (SELECT AlbumId FROM albums)");

        var off = query.StartBuilder();
        Assert.Equal("SELECT * FROM tracks", Render.From(off).CommandText);

        var on = query.StartBuilder();
        on.Use("Recent");
        Assert.Equal("SELECT * FROM tracks WHERE AlbumId IN (SELECT AlbumId FROM albums)",
            Render.From(on).CommandText);
    }

    /// <summary>A marker on a term inside a subquery prunes only that term.</summary>
    [Fact]
    public void A_marker_on_a_term_inside_a_subquery_prunes_only_that_term() {
        var query = new QueryCommand(
            "SELECT * FROM tracks WHERE AlbumId IN (SELECT AlbumId FROM albums WHERE /*Recent*/Year > 2020)");

        var off = query.StartBuilder();
        Assert.Equal("SELECT * FROM tracks WHERE AlbumId IN (SELECT AlbumId FROM albums)",
            Render.From(off).CommandText);

        var on = query.StartBuilder();
        on.Use("Recent");
        Assert.Equal("SELECT * FROM tracks WHERE AlbumId IN (SELECT AlbumId FROM albums WHERE Year > 2020)",
            Render.From(on).CommandText);
    }
}
