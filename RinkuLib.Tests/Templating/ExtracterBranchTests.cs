using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Templating;

/// <summary>
/// Targets the scanner branches the broader suites leave half-covered: dynamic-projection column-name
/// extraction, the always-used <c>!</c> bookkeeping, section comments inside parentheses, and the factory
/// error paths. Expected values follow the documented behaviour, never the current output.
/// </summary>
public class ExtracterBranchTests {
    static QueryBuilder Build(string sql) => new QueryCommand(sql).StartBuilder();

    [Fact]
    public void Projection_key_ignores_space_before_the_comma() {
        var q = new QueryCommand("?SELECT Name , Email FROM Users");
        var b = q.StartBuilder();
        b.Use("Name");
        Render.Expect(b, "SELECT Name FROM Users");
    }

    [Fact]
    public void Projection_key_stops_at_a_dot() {
        var q = new QueryCommand("?SELECT u.Name, u.Email FROM Users u");
        var b = q.StartBuilder();
        b.Use("Name");
        Render.Expect(b, "SELECT u.Name FROM Users u");
    }

    [Fact]
    public void Projection_key_strips_bracket_quotes() {
        var q = new QueryCommand("?SELECT [Full Name], Email FROM Users");
        var b = q.StartBuilder();
        b.Use("Full Name");
        Render.Expect(b, "SELECT [Full Name] FROM Users");
    }

    [Fact]
    public void Projection_key_strips_single_quotes_on_an_alias() {
        var q = new QueryCommand("?SELECT Name AS 'Nick', Email FROM Users");
        var b = q.StartBuilder();
        b.Use("Nick");
        Render.Expect(b, "SELECT Name AS 'Nick' FROM Users");
    }

    [Fact]
    public void Projection_key_strips_double_quotes_on_an_alias() {
        var q = new QueryCommand("?SELECT Name AS \"Nick\", Email FROM Users");
        var b = q.StartBuilder();
        b.Use("Nick");
        Render.Expect(b, "SELECT Name AS \"Nick\" FROM Users");
    }

    [Fact]
    public void Always_column_with_a_finished_variable_before_it() {
        var q = new QueryCommand("?SELECT (?@V) AS X!, Name FROM Users");
        var withVar = q.StartBuilder();
        withVar.Use("@V", 1);
        Render.Expect(withVar, "SELECT (@V) AS X FROM Users", ("@V", 1));
        var nameOnly = q.StartBuilder();
        nameOnly.Use("Name");
        Render.Expect(nameOnly, "SELECT Name FROM Users");
    }

    [Fact]
    public void Always_column_with_a_marker_and_a_pending_condition_behind_it() {
        var q = new QueryCommand("?SELECT /*K*/(?@V) AS X!, Name FROM Users");
        var b = q.StartBuilder();
        b.Use("K");
        b.Use("@V", 1);
        Render.Expect(b, "SELECT (@V) AS X FROM Users", ("@V", 1));
        var nameOnly = q.StartBuilder();
        nameOnly.Use("Name");
        Render.Expect(nameOnly, "SELECT Name FROM Users");
    }

    [Fact]
    public void Always_column_with_a_marker_finished_inside_parens() {
        var q = new QueryCommand("?SELECT (a /*K*/+ b) AS X!, Name FROM Users");
        var on = q.StartBuilder();
        on.Use("K");
        Render.Expect(on, "SELECT (a + b) AS X FROM Users");
        Render.Expect(q.StartBuilder(), "SELECT () AS X FROM Users");
    }

    [Fact]
    public void CondInfo_debug_text_shows_both_type_forms() {
        var intForm = CondInfo.NewRequired("@a", CondInfo.None, 0).ToString();
        Assert.Contains("@a, 3", intForm);
        var charForm = CondInfo.NewRequired("@a", 'N', 0).ToString();
        Assert.Contains("@a, N", charForm);
    }

    [Fact]
    public void Always_marker_inside_deeper_parens_is_rejected() {
        Assert.Throws<Exception>(() => new QueryCommand("?SELECT a, fn(x!, y) AS B FROM t"));
    }

    [Fact]
    public void Marker_on_an_always_column_replaces_always() {
        var q = new QueryCommand("?SELECT /*Manual*/Id!, Username FROM users");
        var manual = q.StartBuilder();
        manual.Use("Manual");
        Render.Expect(manual, "SELECT Id FROM users");
        var userOnly = q.StartBuilder();
        userOnly.Use("Username");
        Render.Expect(userOnly, "SELECT Username FROM users");
    }

    [Fact]
    public void Clause_marker_on_a_subquery_select_is_rejected() {
        var ex = Assert.Throws<Exception>(() => new QueryCommand("SELECT * FROM t WHERE id IN (/*K*/SELECT id FROM u)"));
        Assert.Contains("not finished", ex.Message);
    }


    [Theory]
    [InlineData("SELECT * FROM t WHERE /*K*/?x = 1")]
    [InlineData("SELECT * FROM t WHERE /*K*/?sx = 1")]
    [InlineData("SELECT * FROM t WHERE /*K*/?sex = 1")]
    [InlineData("SELECT * FROM t WHERE /*K*/?selx = 1")]
    [InlineData("SELECT * FROM t WHERE /*K*/?selex = 1")]
    [InlineData("SELECT * FROM t WHERE /*K*/?selecx = 1")]
    public void A_question_mark_word_that_is_not_select_stays_a_plain_footprint(string sql) {
        var b = Build(sql);
        b.Use("K");
        Render.Expect(b, sql.Replace("/*K*/", ""));
        Render.Expect(Build(sql), "SELECT * FROM t");
    }


    [Fact]
    public void Footprint_ending_on_a_semicolon_keeps_the_semicolon() {
        var sql = "SELECT a FROM t WHERE /*K*/x = 1;SELECT b FROM u";
        Render.Expect(Build(sql), "SELECT a FROM t;SELECT b FROM u");
        var b = Build(sql);
        b.Use("K");
        Render.Expect(b, "SELECT a FROM t WHERE x = 1;SELECT b FROM u");
    }

    [Theory]
    [InlineData("SELECT a, /*K*/b;SELECT c FROM u", "SELECT a;SELECT c FROM u", "SELECT a, b;SELECT c FROM u")]
    [InlineData("SELECT a, /*K*/b ;SELECT c FROM u", "SELECT a;SELECT c FROM u", "SELECT a, b ;SELECT c FROM u")]
    [InlineData("UPDATE t SET x = 1, /*K*/y = 2;SELECT 1", "UPDATE t SET x = 1;SELECT 1", "UPDATE t SET x = 1, y = 2;SELECT 1")]
    public void Footprint_just_before_a_statement_separator(string sql, string pruned, string kept) {
        Render.Expect(Build(sql), pruned);
        var b = Build(sql);
        b.Use("K");
        Render.Expect(b, kept);
    }

    [Fact]
    public void Marker_on_a_variable_that_does_not_exist_is_rejected() {
        var ex = Assert.Throws<Exception>(() => new QueryCommand("SELECT a FROM t WHERE /*@Nope*/x = 1"));
        Assert.Contains("must exist", ex.Message);
    }

    [Fact]
    public void A_variable_inside_a_string_literal_is_not_a_variable() {
        var q = new QueryCommand("SELECT * FROM t WHERE a = 'mail@example.com'");
        Assert.Equal(0, q.Mapper.Count);
        Render.Expect(q.StartBuilder(), "SELECT * FROM t WHERE a = 'mail@example.com'");
    }

    [Fact]
    public void A_marker_inside_a_string_literal_is_not_a_marker() {
        var q = new QueryCommand("SELECT * FROM t WHERE a = 'not /*K*/ a marker'");
        Assert.Equal(0, q.Mapper.Count);
        Render.Expect(q.StartBuilder(), "SELECT * FROM t WHERE a = 'not /*K*/ a marker'");
    }

    [Fact]
    public void A_variable_inside_a_bracket_identifier_is_not_a_variable() {
        var q = new QueryCommand("SELECT [a@b] FROM t");
        Assert.Equal(0, q.Mapper.Count);
        Render.Expect(q.StartBuilder(), "SELECT [a@b] FROM t");
    }

    [Fact]
    public void A_doubled_quote_stays_inside_the_literal() {
        var q = new QueryCommand("SELECT * FROM t WHERE a = 'don''t @x'");
        Assert.Equal(0, q.Mapper.Count);
        Render.Expect(q.StartBuilder(), "SELECT * FROM t WHERE a = 'don''t @x'");
    }
}
