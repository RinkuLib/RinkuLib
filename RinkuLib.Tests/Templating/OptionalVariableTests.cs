using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Templating;

/// <summary>
/// The <c>?@Var</c> marker makes a variable optional: its footprint drops out of the SQL when no value
/// is provided and renders with a bound parameter when one is.
/// </summary>
public class OptionalVariableTests {
    [Fact]
    public void Unset_optional_drops_its_footprint() {
        var query = new QueryCommand("SELECT ID, Username FROM Users WHERE IsActive = 1 AND Status = ?@Status");
        Render.Expect(query.StartBuilder(), "SELECT ID, Username FROM Users WHERE IsActive = 1");
    }

    [Fact]
    public void Set_optional_renders_and_binds() {
        var query = new QueryCommand("SELECT ID, Username FROM Users WHERE IsActive = 1 AND Status = ?@Status");
        var builder = query.StartBuilder();
        builder.Use("@Status", "Active");
        Render.Expect(builder, "SELECT ID, Username FROM Users WHERE IsActive = 1 AND Status = @Status", ("@Status", "Active"));
    }

    [Fact]
    public void Dropping_the_only_predicate_removes_the_WHERE_keyword() {
        var query = new QueryCommand("SELECT ID, u.Name FROM Users u WHERE u.Status = ?@Status");
        Render.Expect(query.StartBuilder(), "SELECT ID, u.Name FROM Users u");
    }

    [Fact]
    public void Function_call_footprint_drops_whole_call() {
        var query = new QueryCommand("SELECT ID, u.Name FROM Users u WHERE u.Name LIKE CONCAT('%', ?@Name, '%')");
        Render.Expect(query.StartBuilder(), "SELECT ID, u.Name FROM Users u");
    }

    [Fact]
    public void Function_call_footprint_renders_whole_call_when_set() {
        var query = new QueryCommand("SELECT ID, u.Name FROM Users u WHERE u.Name LIKE CONCAT('%', ?@Name, '%')");
        var builder = query.StartBuilder();
        builder.Use("@Name", "Dev");
        Render.Expect(builder, "SELECT ID, u.Name FROM Users u WHERE u.Name LIKE CONCAT('%', @Name, '%')", ("@Name", "Dev"));
    }

    [Fact]
    public void Shared_footprint_drops_when_one_variable_is_missing() {
        var query = new QueryCommand("SELECT ID, Name FROM Products WHERE Price * ?@Modifier > ?@Minimum");
        var builder = query.StartBuilder();
        builder.Use("@Modifier", 1.1);
        Render.Expect(builder, "SELECT ID, Name FROM Products", ("@Modifier", 1.1));
    }

    [Fact]
    public void Shared_footprint_renders_when_all_variables_are_set() {
        var query = new QueryCommand("SELECT ID, Name FROM Products WHERE Price * ?@Modifier > ?@Minimum");
        var builder = query.StartBuilder();
        builder.Use("@Modifier", 1.5);
        builder.Use("@Minimum", 10.0);
        Render.Expect(builder, "SELECT ID, Name FROM Products WHERE Price * @Modifier > @Minimum",
            ("@Modifier", 1.5), ("@Minimum", 10.0));
    }

    [Fact]
    public void Context_joining_keeps_left_side_only_with_the_variable() {
        var query = new QueryCommand("SELECT * FROM Products WHERE Price IS NOT NULL &AND Price > ?@MinPrice");
        Render.Expect(query.StartBuilder(), "SELECT * FROM Products");
    }

    [Fact]
    public void Context_joining_renders_both_sides_when_set() {
        var query = new QueryCommand("SELECT * FROM Products WHERE Price IS NOT NULL &AND Price > ?@MinPrice");
        var builder = query.StartBuilder();
        builder.Use("@MinPrice", 50);
        Render.Expect(builder, "SELECT * FROM Products WHERE Price IS NOT NULL AND Price > @MinPrice", ("@MinPrice", 50));
    }

    [Fact]
    public void Update_list_drops_missing_assignment_and_its_comma() {
        var query = new QueryCommand("UPDATE Users SET LastModified = GETDATE(), Username = ?@Username, Email = ?@Email WHERE ID = @ID");
        var builder = query.StartBuilder();
        builder.Use("@Username", "jdoe");
        builder.Use("@ID", 1);
        Render.Expect(builder, "UPDATE Users SET LastModified = GETDATE(), Username = @Username WHERE ID = @ID",
            ("@Username", "jdoe"), ("@ID", 1));
    }

    [Fact]
    public void Update_list_renders_fully_when_all_are_set() {
        var query = new QueryCommand("UPDATE Users SET LastModified = GETDATE(), Username = ?@Username, Email = ?@Email WHERE ID = @ID");
        var builder = query.StartBuilder();
        builder.Use("@Username", "jdoe");
        builder.Use("@Email", "j@doe.com");
        builder.Use("@ID", 1);
        Render.Expect(builder, "UPDATE Users SET LastModified = GETDATE(), Username = @Username, Email = @Email WHERE ID = @ID",
            ("@Username", "jdoe"), ("@Email", "j@doe.com"), ("@ID", 1));
    }

    [Fact]
    public void Required_variable_stays_in_sql_when_optionals_drop() {
        var query = new QueryCommand("UPDATE Users SET LastModified = GETDATE(), Username = ?@Username, Email = ?@Email WHERE ID = @ID");
        var builder = query.StartBuilder();
        builder.Use("@Username", "jdoe");
        Render.Expect(builder, "UPDATE Users SET LastModified = GETDATE(), Username = @Username WHERE ID = @ID",
            ("@Username", "jdoe"));
    }

    [Fact]
    public void Insert_column_and_value_drop_together() {
        var query = new QueryCommand("INSERT INTO Users (Username, /*@Email*/Email) VALUES (@Username, ?@Email)");
        var builder = query.StartBuilder();
        builder.Use("@Username", "alice");
        Render.Expect(builder, "INSERT INTO Users (Username) VALUES (@Username)", ("@Username", "alice"));
    }

    [Fact]
    public void Insert_column_and_value_render_together() {
        var query = new QueryCommand("INSERT INTO Users (Username, /*@Email*/Email) VALUES (@Username, ?@Email)");
        var builder = query.StartBuilder();
        builder.Use("@Username", "alice");
        builder.Use("@Email", "a@a.com");
        Render.Expect(builder, "INSERT INTO Users (Username, Email) VALUES (@Username, @Email)",
            ("@Username", "alice"), ("@Email", "a@a.com"));
    }

    [Fact]
    public void Unknown_variable_name_is_rejected_by_the_builder() {
        var query = new QueryCommand("SELECT ID FROM Users WHERE IsActive = @Active");
        var builder = query.StartBuilder();
        Assert.False(builder.Use("@NotInQuery", true));
    }
}
