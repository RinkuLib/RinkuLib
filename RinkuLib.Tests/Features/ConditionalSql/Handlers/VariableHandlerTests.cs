using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Templating;

/// <summary>
/// Suffix letters route a variable through a handler instead of a plain parameter: <c>_S</c> injects a
/// quoted string, <c>_R</c> raw text, <c>_N</c> a number, and <c>_X</c> spreads a collection into
/// numbered parameters.
/// </summary>
public class VariableHandlerTests {
    [Fact]
    public void String_handler_injects_quoted_literal() {
        var query = new QueryCommand("SELECT ID FROM Users WHERE Name = @Name_S");
        var builder = query.StartBuilder();
        builder.Use("@Name", "Bob");
        Render.Expect(builder, "SELECT ID FROM Users WHERE Name = 'Bob'");
    }

    [Fact]
    public void String_handler_stringifies_non_string_values() {
        var query = new QueryCommand("SELECT ID FROM Users WHERE Name = @Name_S");
        var builder = query.StartBuilder();
        builder.Use("@Name", 42);
        Render.Expect(builder, "SELECT ID FROM Users WHERE Name = '42'");
    }

    [Fact]
    public void Raw_handler_injects_text_verbatim() {
        var query = new QueryCommand("SELECT ID, Name FROM @Table_R WHERE IsActive = 1");
        var builder = query.StartBuilder();
        builder.Use("@Table", "Logs");
        Render.Expect(builder, "SELECT ID, Name FROM Logs WHERE IsActive = 1");
    }

    [Fact]
    public void Raw_and_string_handlers_combine() {
        var query = new QueryCommand("SELECT ID, Name FROM @Table_R WHERE IsActive = 1 AND Name = @Name_S");
        var builder = query.StartBuilder();
        builder.Use("@Table", "Logs");
        builder.Use("@Name", "Name");
        Render.Expect(builder, "SELECT ID, Name FROM Logs WHERE IsActive = 1 AND Name = 'Name'");
    }

    [Fact]
    public void Number_handler_injects_integer() {
        var query = new QueryCommand("SELECT Name FROM Products ORDER BY ID OFFSET ?@Skip_N ROWS FETCH NEXT @Take_N ROWS ONLY");
        var builder = query.StartBuilder();
        builder.Use("@Skip", 50);
        builder.Use("@Take", 50);
        Render.Expect(builder, "SELECT Name FROM Products ORDER BY ID OFFSET 50 ROWS FETCH NEXT 50 ROWS ONLY");
    }

    [Fact]
    public void Handler_clause_drops_when_nothing_is_provided() {
        var query = new QueryCommand("SELECT Name FROM Products ORDER BY ID OFFSET ?@Skip_N ROWS FETCH NEXT @Take_N ROWS ONLY");
        Render.Expect(query.StartBuilder(), "SELECT Name FROM Products ORDER BY ID");
    }

    [Fact]
    public void Required_handler_variable_missing_inside_activated_segment_throws() {
        var query = new QueryCommand("SELECT Name FROM Products ORDER BY ID OFFSET ?@Skip_N ROWS FETCH NEXT @Take_N ROWS ONLY");
        var builder = query.StartBuilder();
        builder.Use("@Skip", 50);
        var cmd = new FakeCommand();
        Assert.Throws<RequiredHandlerValueException>(() => builder.QueryCommand.SetCommand((System.Data.Common.DbCommand)cmd, builder.Variables));
    }

    [Fact]
    public void Order_by_with_raw_handlers_drops_when_one_is_missing() {
        var query = new QueryCommand("SELECT * FROM Products WHERE IsActive = 1 /*@Sort*/ORDER BY @Sort_R ?@Dir_R");
        var builder = query.StartBuilder();
        builder.Use("@Sort", "Price");
        Render.Expect(builder, "SELECT * FROM Products WHERE IsActive = 1");
    }

    [Fact]
    public void Order_by_with_raw_handlers_renders_when_all_are_set() {
        var query = new QueryCommand("SELECT * FROM Products WHERE IsActive = 1 /*@Sort*/ORDER BY @Sort_R ?@Dir_R");
        var builder = query.StartBuilder();
        builder.Use("@Sort", "Price");
        builder.Use("@Dir", "DESC");
        Render.Expect(builder, "SELECT * FROM Products WHERE IsActive = 1 ORDER BY Price DESC");
    }

    [Fact]
    public void Spread_expands_each_element_into_a_numbered_parameter() {
        var query = new QueryCommand("SELECT * FROM Tasks WHERE CategoryID IN (?@Cats_X)");
        var builder = query.StartBuilder();
        builder.Use("@Cats", new[] { "Test1", "Test2" });
        Render.Expect(builder, "SELECT * FROM Tasks WHERE CategoryID IN (@Cats_1, @Cats_2)",
            ("@Cats_1", "Test1"), ("@Cats_2", "Test2"));
    }

    [Fact]
    public void Spread_handles_names_crossing_digit_count_boundaries() {
        var query = new QueryCommand("SELECT * FROM Tasks WHERE CategoryID IN (?@Cats_X)");
        var builder = query.StartBuilder();
        const int amount = 150;
        builder.Use("@Cats", Enumerable.Range(1, amount));
        var expectedParams = Enumerable.Range(1, amount).Select(i => ("@Cats_" + i, (object?)i)).ToArray();
        Render.Expect(builder, $"SELECT * FROM Tasks WHERE CategoryID IN ({string.Join(", ", expectedParams.Select(t => t.Item1))})", expectedParams);
    }

    [Fact]
    public void Spread_drops_its_clause_when_unset() {
        var query = new QueryCommand("SELECT * FROM Tasks WHERE CategoryID IN (?@Cats_X)");
        Render.Expect(query.StartBuilder(), "SELECT * FROM Tasks");
    }

    [Fact]
    public void Case_expression_drops_entirely_when_nothing_is_provided() {
        var template = "SELECT * FROM Products WHERE CASE WHEN Category = ?@Category THEN 1 WHEN Category = 0 THEN ?@NoCategory_S ELSE 0 END = ?@CatFlag";
        var query = new QueryCommand(template);
        Render.Expect(query.StartBuilder(), "SELECT * FROM Products");
    }

    [Fact]
    public void Case_keeps_structure_when_only_the_comparison_is_provided() {
        var template = "SELECT * FROM Products WHERE CASE WHEN Category = ?@Category THEN 1 WHEN Category = 0 THEN ?@NoCategory_S ELSE 0 END = ?@CatFlag";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@CatFlag", 1);
        Render.Expect(builder, "SELECT * FROM Products WHERE CASE THEN 1 WHEN Category = 0 ELSE 0 END = @CatFlag", ("@CatFlag", 1));
    }

    [Fact]
    public void Case_when_tied_to_its_variable_drops_the_whole_branch() {
        var template = "SELECT * FROM Products WHERE CASE WHEN Category = ?@Category /*@Category*/THEN 1 WHEN Category = 0 THEN ?@NoCategory_S ELSE 0 END = ?@CatFlag";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@CatFlag", 1);
        Render.Expect(builder, "SELECT * FROM Products WHERE CASE WHEN Category = 0 ELSE 0 END = @CatFlag", ("@CatFlag", 1));
    }

    [Fact]
    public void Case_renders_provided_branches_only() {
        var template = "SELECT * FROM Products WHERE CASE WHEN Category = ?@Category THEN 1 WHEN Category = 0 THEN ?@NoCategory_S ELSE 0 END = ?@CatFlag";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@CatFlag", 1);
        builder.Use("@Category", 1);
        Render.Expect(builder, "SELECT * FROM Products WHERE CASE WHEN Category = @Category THEN 1 WHEN Category = 0 ELSE 0 END = @CatFlag",
            ("@Category", 1), ("@CatFlag", 1));
    }

    [Fact]
    public void Case_branch_tied_to_handler_variable_drops_with_it() {
        var template = "SELECT * FROM Products WHERE CASE WHEN Category = ?@Category THEN 1 /*@NoCategory*/WHEN Category = 0 THEN ?@NoCategory_S ELSE 0 END = ?@CatFlag";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@CatFlag", 1);
        builder.Use("@Category", 1);
        Render.Expect(builder, "SELECT * FROM Products WHERE CASE WHEN Category = @Category THEN 1 ELSE 0 END = @CatFlag",
            ("@Category", 1), ("@CatFlag", 1));
    }

    [Fact]
    public void Case_renders_fully_when_everything_is_provided() {
        var template = "SELECT * FROM Products WHERE CASE WHEN Category = ?@Category THEN 1 WHEN Category = 0 THEN ?@NoCategory_N ELSE 0 END = ?@CatFlag";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@CatFlag", 1);
        builder.Use("@Category", 1);
        builder.Use("@NoCategory", -1);
        Render.Expect(builder, "SELECT * FROM Products WHERE CASE WHEN Category = @Category THEN 1 WHEN Category = 0 THEN -1 ELSE 0 END = @CatFlag",
            ("@Category", 1), ("@CatFlag", 1));
    }
}
