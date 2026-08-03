using System.Data.Common;
using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Building;

public sealed class UseWithTests {
    private const string Template =
        "SELECT ID FROM Users WHERE IsActive = ?@Active AND Status = ?@Status AND /*ShowEmail*/Email = Email";

    [Fact]
    public void UseWith_copies_values_and_conditions_into_an_in_memory_builder() {
        var builder = new QueryCommand(Template).StartBuilder();
        builder.UseWith(new Filter(1, "On", true));

        Assert.Equal(1, builder["@Active"]);
        Assert.Equal("On", builder["@Status"]);
        Assert.Same(QueryBuilder.Used, builder["ShowEmail"]);
    }

    [Fact]
    public void UseWith_writes_null_for_members_that_are_not_usable() {
        var builder = new QueryCommand(Template).StartBuilder();
        builder.Use("@Active", 9);
        builder.Use("@Status", "old");
        builder.Use("ShowEmail");

        builder.UseWith(new Filter(null, null, false));

        Assert.All(builder.Variables, Assert.Null);
    }

    [Fact]
    public void UseWith_supports_structs_and_by_ref_structs() {
        var query = new QueryCommand("SELECT * FROM Users WHERE Id = ?@Id");
        var builder = query.StartBuilder();
        var filter = new StructFilter(7);

        builder.UseWith(ref filter);

        Assert.Equal(7, builder["@Id"]);
    }

    [Fact]
    public void UseWith_supports_the_object_entry_point_for_a_boxed_struct() {
        var builder = new QueryCommand("SELECT * FROM Users WHERE Id = ?@Id").StartBuilder();

        builder.UseWith((object)new StructFilter(11));

        Assert.Equal(11, builder["@Id"]);
    }

    [Fact]
    public void UseWith_leaves_special_handler_values_raw_until_command_setup() {
        var query = new QueryCommand("SELECT * FROM Users WHERE Id IN (?@Ids_X)");
        var builder = query.StartBuilder();
        var filter = new SpreadFilter([2, 4, 8]);

        builder.UseWith(filter);

        Assert.Equal([2, 4, 8], Assert.IsType<int[]>(builder["@Ids"]));
        Render.Expect(builder, "SELECT * FROM Users WHERE Id IN (@Ids_1, @Ids_2, @Ids_3)",
            ("@Ids_1", 2), ("@Ids_2", 4), ("@Ids_3", 8));
    }

    [Fact]
    public void Bound_UseWith_resets_removed_values_and_processes_handlers() {
        var cmd = new FakeCommand { Connection = new FakeConnection() };
        var builder = new QueryCommand("SELECT * FROM Users WHERE Id IN (?@Ids_X) AND Name = ?@Name")
            .StartBuilder((DbCommand)cmd);

        builder.UseWith(new BoundFilter([1, 2], "first"));
        builder.UseWith(new BoundFilter([], null));

        Assert.Empty(cmd.BoundParameters);
        Assert.Equal("SELECT * FROM Users", cmd.CommandText);
    }

    [Fact]
    public void Bound_UseWith_reuses_one_command_for_a_batch_of_parameter_objects() {
        var cmd = new FakeCommand { Connection = new FakeConnection() };
        var builder = new QueryCommand("UPDATE Users SET Name = @Name WHERE Id = @Id")
            .StartBuilder((DbCommand)cmd);

        foreach (var item in new[] { new BatchItem(1, "first"), new BatchItem(2, "second"), new BatchItem(3, "third") })
            builder.UseWith(item);

        Assert.Equal(["@Name", "@Id"], cmd.BoundParameters.Select(x => x.ParameterName));
        Assert.Equal(["third", 3], cmd.BoundParameters.Select(x => x.Value));
    }

    private sealed record Filter(int? Active, string? Status, [property: ForBoolCond] bool ShowEmail);
    private readonly record struct StructFilter(int Id);
    private sealed record SpreadFilter(int[] Ids);
    private sealed record BoundFilter(int[] Ids, string? Name);
    private sealed record BatchItem(int Id, string Name);
}
