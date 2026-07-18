using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Building;

/// <summary>
/// <c>UseWith</c> maps an object's members onto the builder by name: a null member leaves its variable
/// off, attributes refine what counts as used, and bool members can drive conditions.
/// </summary>
public class UseWithTests {
    private const string Template =
        "SELECT EmployeeId, FirstName, Salary, /*Year*/Year FROM Employees WHERE Salary >= ?@MinSalary AND Department = ?@DeptName AND Status = ?@EmployeeStatus ORDER BY Salary DESC";

    private static QueryBuilder StartBuilder() => new QueryCommand(Template).StartBuilder();

    [Fact]
    public void Null_members_leave_their_variables_off() {
        var builder = StartBuilder();
        builder.UseWith(new EmployeeFilterStruct(10, null, "P"));
        Render.Expect(builder, "SELECT EmployeeId, FirstName, Salary FROM Employees WHERE Salary >= @MinSalary AND Status = @EmployeeStatus ORDER BY Salary DESC",
            ("@MinSalary", 10), ("@EmployeeStatus", "P"));
    }

    [Fact]
    public void Struct_passed_boxed_maps_the_same_as_typed() {
        var builder = StartBuilder();
        builder.UseWith((object)new EmployeeFilterStruct(10, null, "P"));
        Render.Expect(builder, "SELECT EmployeeId, FirstName, Salary FROM Employees WHERE Salary >= @MinSalary AND Status = @EmployeeStatus ORDER BY Salary DESC",
            ("@MinSalary", 10), ("@EmployeeStatus", "P"));
    }

    [Fact]
    public void Struct_passed_by_ref_maps_the_same_as_by_value() {
        var builder = StartBuilder();
        var filter = new EmployeeFilterStruct(null, "Marketing", "Employed");
        builder.UseWith(ref filter);
        Render.Expect(builder, "SELECT EmployeeId, FirstName, Salary FROM Employees WHERE Department = @DeptName AND Status = @EmployeeStatus ORDER BY Salary DESC",
            ("@DeptName", "Marketing"), ("@EmployeeStatus", "Employed"));
    }

    [Fact]
    public void Bool_member_marked_ForBoolCond_drives_its_condition() {
        var builder = StartBuilder();
        builder.UseWith(new EmployeeFilterClass(null, null, null) { Year = true });
        Render.Expect(builder, "SELECT EmployeeId, FirstName, Salary, Year FROM Employees ORDER BY Salary DESC");
    }

    [Fact]
    public void Bool_member_left_false_keeps_the_condition_off() {
        var builder = StartBuilder();
        builder.UseWith(new EmployeeFilterClass(23, "Marketing", null));
        Render.Expect(builder, "SELECT EmployeeId, FirstName, Salary FROM Employees WHERE Salary >= @MinSalary AND Department = @DeptName ORDER BY Salary DESC",
            ("@MinSalary", 23), ("@DeptName", "Marketing"));
    }

    [Fact]
    public void Class_passed_boxed_maps_all_members() {
        var builder = StartBuilder();
        var filter = new EmployeeFilterClass(22, "Marketing", "Employed") { Year = true };
        builder.UseWith(ref filter);
        Render.Expect(builder, "SELECT EmployeeId, FirstName, Salary, Year FROM Employees WHERE Salary >= @MinSalary AND Department = @DeptName AND Status = @EmployeeStatus ORDER BY Salary DESC",
            ("@MinSalary", 22), ("@DeptName", "Marketing"), ("@EmployeeStatus", "Employed"));
    }

    [Fact]
    public void NotNullOrWhitespace_treats_blank_strings_as_unused() {
        var builder = StartBuilder();
        builder.UseWith(new EmployeeFilterClass(23, "Marketing", "  ") { Year = true });
        Render.Expect(builder, "SELECT EmployeeId, FirstName, Salary, Year FROM Employees WHERE Salary >= @MinSalary AND Department = @DeptName ORDER BY Salary DESC",
            ("@MinSalary", 23), ("@DeptName", "Marketing"));
    }

    [Fact]
    public void UsesBoolConds_on_the_type_turns_conditions_on() {
        var builder = StartBuilder();
        builder.UseWith(new EmployeeFilterAlwaysYear(23, "Marketing", "  "));
        Render.Expect(builder, "SELECT EmployeeId, FirstName, Salary, Year FROM Employees WHERE Salary >= @MinSalary AND Department = @DeptName ORDER BY Salary DESC",
            ("@MinSalary", 23), ("@DeptName", "Marketing"));
    }

    [Fact]
    public void NotDefault_treats_default_values_as_unused() {
        var query = new QueryCommand("SELECT EmployeeId FROM Employees WHERE Salary >= ?@MinSalary AND Department = ?@DeptName");
        var builder = query.StartBuilder();
        builder.UseWith(new EmployeeFilterNotDefault(0, "Marketing"));
        Render.Expect(builder, "SELECT EmployeeId FROM Employees WHERE Department = @DeptName", ("@DeptName", "Marketing"));
    }

    [Fact]
    public void NotDefault_passes_non_default_values_through() {
        var query = new QueryCommand("SELECT EmployeeId FROM Employees WHERE Salary >= ?@MinSalary AND Department = ?@DeptName");
        var builder = query.StartBuilder();
        builder.UseWith(new EmployeeFilterNotDefault(5, "Marketing"));
        Render.Expect(builder, "SELECT EmployeeId FROM Employees WHERE Salary >= @MinSalary AND Department = @DeptName",
            ("@MinSalary", 5), ("@DeptName", "Marketing"));
    }

    [Fact]
    public void Anonymous_object_members_map_by_name() {
        var query = new QueryCommand("SELECT * FROM u WHERE u.ID = ?@ID AND u.OtherID = ?@OtherID");
        var builder = query.StartBuilder();
        builder.UseWith(new { ID = 1 });
        Render.Expect(builder, "SELECT * FROM u WHERE u.ID = @ID", ("@ID", 1));
    }

    [Fact]
    public void Two_anonymous_types_on_one_command_keep_separate_accessors() {
        var query = new QueryCommand("SELECT * FROM u WHERE u.ID = ?@ID AND u.OtherID = ?@OtherID");

        var first = query.StartBuilder();
        first.UseWith(new { ID = 1 });
        Render.Expect(first, "SELECT * FROM u WHERE u.ID = @ID", ("@ID", 1));

        var second = query.StartBuilder();
        second.UseWith(new { OtherID = 2 });
        Render.Expect(second, "SELECT * FROM u WHERE u.OtherID = @OtherID", ("@OtherID", 2));
    }

    [Fact]
    public void Two_named_types_on_one_command_keep_separate_accessors() {
        var query = new QueryCommand("SELECT * FROM u WHERE u.ID = ?@ID AND u.OtherID = ?@OtherID");

        var first = query.StartBuilder();
        first.UseWith(new IdFilter { ID = 1 });
        Render.Expect(first, "SELECT * FROM u WHERE u.ID = @ID", ("@ID", 1));

        var second = query.StartBuilder();
        second.UseWith(new OtherIdFilter { OtherID = 2 });
        Render.Expect(second, "SELECT * FROM u WHERE u.OtherID = @OtherID", ("@OtherID", 2));
    }

    [Fact]
    public void UseWith_overwrites_values_from_a_previous_object() {
        var query = new QueryCommand("SELECT * FROM u WHERE u.ID = ?@ID AND u.OtherID = ?@OtherID");
        var builder = query.StartBuilder();
        builder.UseWith(new { ID = 1, OtherID = 2 });
        builder.UseWith(new { ID = 3 });
        Render.Expect(builder, "SELECT * FROM u WHERE u.ID = @ID", ("@ID", 3));
    }
}

public record struct EmployeeFilterStruct(int? MinSalary, string? DeptName, string? EmployeeStatus);
public record class EmployeeFilterClass(int? MinSalary, string? DeptName, [property: NotNullOrWhitespace] string? EmployeeStatus) {
    public int OtherField = 32;
    [ForBoolCond] public bool Year;
}
[UsesBoolConds("Year")]
public record class EmployeeFilterAlwaysYear(int? MinSalary, string? DeptName, [property: NotNullOrWhitespace] string? EmployeeStatus) {
    public int OtherField = 32;
}
public record class EmployeeFilterNotDefault([property: NotDefault] int MinSalary, string? DeptName);
public sealed class IdFilter {
    public int ID;
}
public sealed class OtherIdFilter {
    public int OtherID;
}
