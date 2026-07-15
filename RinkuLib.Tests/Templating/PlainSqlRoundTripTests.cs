using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Templating;

/// <summary>
/// SQL without conditional markers must come out of the template engine unchanged. Each shape here
/// once tripped the parser or exercises a distinct token kind (parens, literals, batches, blocks).
/// </summary>
public class PlainSqlRoundTripTests {
    private static void AssertRoundTrips(string sql) {
        var query = new QueryCommand(sql);
        Render.Expect(query.StartBuilder(), sql);
    }

    [Fact] public void Select_star() => AssertRoundTrips("SELECT * FROM Posts");
    [Fact] public void Select_count_star() => AssertRoundTrips("SELECT COUNT(*) FROM Posts");
    [Fact] public void Select_constant_filter() => AssertRoundTrips("SELECT ID, Name FROM Users WHERE IsActive = 1");
    [Fact] public void Two_statements() => AssertRoundTrips("SELECT * FROM Posts WHERE Id = @a; SELECT * FROM Posts WHERE Id = @b");
    [Fact] public void Function_with_args() => AssertRoundTrips("SELECT REPLICATE('x', 2000) AS T FROM Posts");
    [Fact] public void Function_without_args() => AssertRoundTrips("SELECT GETDATE() AS T FROM Posts");
    [Fact] public void Nested_parens_predicate() => AssertRoundTrips("SELECT * FROM Posts WHERE (Id = 1 AND (Age > 2))");
    [Fact] public void Subquery_with_required_variable() => AssertRoundTrips("SELECT * FROM Posts WHERE Id IN (SELECT Id FROM Others WHERE X = @a)");
    [Fact] public void String_literal_containing_parens() => AssertRoundTrips("SELECT '(' + Name + ')' AS T FROM Posts");
    [Fact] public void Insert_literal_values() => AssertRoundTrips("INSERT INTO Posts (Id, Text) VALUES (1, 'abc')");
    [Fact] public void Insert_function_value() => AssertRoundTrips("INSERT INTO Posts (CreationDate) VALUES (GETDATE())");
    [Fact] public void Insert_nested_function_values() => AssertRoundTrips("INSERT INTO Posts (Text, CreationDate, LastChangeDate) VALUES (REPLICATE('x', 2000), GETDATE(), GETDATE())");
    [Fact] public void Declare_and_set_batch() => AssertRoundTrips("DECLARE @i INT = 0; SET @i = @i + 1;");
    [Fact] public void Begin_end_set() => AssertRoundTrips("BEGIN SET NOCOUNT ON; END");
    [Fact] public void Begin_end_insert() => AssertRoundTrips("BEGIN INSERT INTO Posts (Text) VALUES (REPLICATE('x', 10)); END");
    [Fact] public void While_loop_set() => AssertRoundTrips("DECLARE @i INT = 0; WHILE @i < 5 BEGIN SET @i = @i + 1; END");
    [Fact] public void While_loop_insert() => AssertRoundTrips("DECLARE @i INT = 0; WHILE @i < 5 BEGIN INSERT INTO Posts (Id) VALUES (@i); SET @i = @i + 1; END");
    [Fact] public void While_loop_insert_with_nested_function() => AssertRoundTrips("SET NOCOUNT ON; DECLARE @i INT = 0; WHILE @i < 5 BEGIN INSERT INTO Posts (Text) VALUES (REPLICATE('x', 10)); SET @i = @i + 1; END");

    [Fact]
    public void Required_variable_stays_in_sql_even_when_no_value_was_set() {
        var query = new QueryCommand("SELECT ID FROM Users WHERE IsActive = @Active");
        Render.Expect(query.StartBuilder(), "SELECT ID FROM Users WHERE IsActive = @Active");
    }

    [Fact]
    public void Required_variable_binds_when_set() {
        var query = new QueryCommand("SELECT ID FROM Users WHERE IsActive = @Active");
        var builder = query.StartBuilder();
        builder.Use("@Active", true);
        Render.Expect(builder, "SELECT ID FROM Users WHERE IsActive = @Active", ("@Active", true));
    }

    [Fact]
    public void Rendering_through_the_IDbCommand_path_matches_the_DbCommand_path() {
        var query = new QueryCommand("SELECT ID FROM Users WHERE IsActive = @Active");
        var builder = query.StartBuilder();
        builder.Use("@Active", 1);
        var cmd = Render.FromInterface(builder);
        Render.AssertCommand(cmd, "SELECT ID FROM Users WHERE IsActive = @Active", ("@Active", 1));
    }
}
