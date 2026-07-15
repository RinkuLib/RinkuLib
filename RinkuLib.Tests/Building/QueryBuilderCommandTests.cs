using System.Data;
using System.Data.Common;
using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Building;

/// <summary>
/// The live-command builder: every value change is synced onto the bound command's parameters on the
/// spot, so one command can be reused across runs.
/// </summary>
public class QueryBuilderCommandTests {
    private const string Template = "SELECT ID FROM Users WHERE IsActive = ?@Active AND Status = ?@Status";
    private const string SpreadTemplate = "SELECT * FROM Tasks WHERE CategoryID IN (?@Cats_X)";

    private static (QueryBuilderCommand<DbCommand> Builder, FakeCommand Cmd) StartBuilder(string template) {
        var cmd = new FakeCommand();
        var builder = new QueryCommand(template).StartBuilder((DbCommand)cmd);
        return (builder, cmd);
    }

    [Fact]
    public void Setting_a_variable_adds_its_parameter_immediately() {
        var (builder, cmd) = StartBuilder(Template);
        builder.Use("@Active", 1);
        var p = Assert.Single(cmd.BoundParameters);
        Assert.Equal("@Active", p.ParameterName);
        Assert.Equal(1, p.Value);
    }

    [Fact]
    public void Setting_the_same_variable_updates_the_parameter_in_place() {
        var (builder, cmd) = StartBuilder(Template);
        builder.Use("@Active", 1);
        builder.Use("@Active", 2);
        var p = Assert.Single(cmd.BoundParameters);
        Assert.Equal(2, p.Value);
    }

    [Fact]
    public void Setting_a_variable_to_null_removes_the_parameter() {
        var (builder, cmd) = StartBuilder(Template);
        builder.Use("@Active", 1);
        builder.Use("@Active", null);
        Assert.Empty(cmd.BoundParameters);
        Assert.Null(builder["@Active"]);
    }

    [Fact]
    public void Setting_an_unset_variable_to_null_is_a_no_op() {
        var (builder, cmd) = StartBuilder(Template);
        Assert.True(builder.Use("@Active", null));
        Assert.Empty(cmd.BoundParameters);
    }

    [Fact]
    public void Remove_takes_the_parameter_off_the_command() {
        var (builder, cmd) = StartBuilder(Template);
        builder.Use("@Active", 1);
        builder.Remove("@Active");
        Assert.Empty(cmd.BoundParameters);
    }

    [Fact]
    public void Remove_by_span_takes_the_parameter_off_the_command() {
        var (builder, cmd) = StartBuilder(Template);
        builder.Use("@Active", 1);
        builder.Remove("@Active".AsSpan());
        Assert.Empty(cmd.BoundParameters);
    }

    [Fact]
    public void Remove_with_a_negative_index_is_ignored() {
        var (builder, cmd) = StartBuilder(Template);
        builder.Use("@Active", 1);
        builder.Remove(-1);
        Assert.Single(cmd.BoundParameters);
    }

    [Fact]
    public void Reset_removes_every_parameter() {
        var (builder, cmd) = StartBuilder(Template);
        builder.Use("@Active", 1);
        builder.Use("@Status", "On");
        builder.Reset();
        Assert.Empty(cmd.BoundParameters);
        Assert.All(builder.Variables, Assert.Null);
    }

    [Fact]
    public void Unknown_names_are_rejected() {
        var (builder, _) = StartBuilder(Template);
        Assert.False(builder.Use("Nope"));
        Assert.False(builder.Use("@Nope", 1));
        Assert.False(builder.UnUse("Nope"));
    }

    [Fact]
    public void GetQueryText_reflects_the_current_values() {
        var (builder, _) = StartBuilder(Template);
        builder.Use("@Active", 1);
        Assert.Equal("SELECT ID FROM Users WHERE IsActive = @Active", builder.GetQueryText());
    }

    [Fact]
    public void Spread_binds_numbered_parameters_immediately() {
        var (builder, cmd) = StartBuilder(SpreadTemplate);
        builder.Use("@Cats", new[] { 1, 2, 3 });
        Assert.Equal(["@Cats_1", "@Cats_2", "@Cats_3"], cmd.BoundParameters.Select(p => p.ParameterName));
        Assert.Equal([1, 2, 3], cmd.BoundParameters.Select(p => p.Value));
    }

    [Fact]
    public void Spread_update_with_same_count_replaces_values_in_place() {
        var (builder, cmd) = StartBuilder(SpreadTemplate);
        builder.Use("@Cats", new[] { 1, 2 });
        builder.Use("@Cats", new[] { 8, 9 });
        Assert.Equal(2, cmd.BoundParameters.Count);
        Assert.Equal([8, 9], cmd.BoundParameters.Select(p => p.Value));
    }

    [Fact]
    public void Spread_update_with_more_items_grows_the_parameter_list() {
        var (builder, cmd) = StartBuilder(SpreadTemplate);
        builder.Use("@Cats", new[] { 1, 2 });
        builder.Use("@Cats", new[] { 5, 6, 7, 8 });
        Assert.Equal(["@Cats_1", "@Cats_2", "@Cats_3", "@Cats_4"], cmd.BoundParameters.Select(p => p.ParameterName));
        Assert.Equal([5, 6, 7, 8], cmd.BoundParameters.Select(p => p.Value));
    }

    [Fact]
    public void Spread_update_with_fewer_items_prunes_the_parameter_list() {
        var (builder, cmd) = StartBuilder(SpreadTemplate);
        builder.Use("@Cats", new[] { 1, 2, 3 });
        builder.Use("@Cats", new[] { 4 });
        var p = Assert.Single(cmd.BoundParameters);
        Assert.Equal("@Cats_1", p.ParameterName);
        Assert.Equal(4, p.Value);
    }

    [Fact]
    public void Spread_update_to_null_removes_every_numbered_parameter() {
        var (builder, cmd) = StartBuilder(SpreadTemplate);
        builder.Use("@Cats", new[] { 1, 2, 3 });
        builder.Use("@Cats", null);
        Assert.Empty(cmd.BoundParameters);
    }

    [Fact]
    public void Reset_removes_spread_parameters_too() {
        var (builder, cmd) = StartBuilder(SpreadTemplate);
        builder.Use("@Cats", new[] { 1, 2 });
        builder.Reset();
        Assert.Empty(cmd.BoundParameters);
    }

    [Fact]
    public void StartBuilder_with_values_binds_them_immediately() {
        var cmd = new FakeCommand();
        var builder = new QueryCommand(Template).StartBuilder((DbCommand)cmd, [("@Active", 1)]);
        var p = Assert.Single(cmd.BoundParameters);
        Assert.Equal("@Active", p.ParameterName);
        Assert.Equal(1, p.Value);
        Assert.NotNull(builder["@Active"]);
    }

    [Fact]
    public void Interface_command_builder_binds_the_same_way() {
        var cmd = new FakeCommand();
        var builder = new QueryCommand(Template).StartBuilder((IDbCommand)cmd, [("@Active", 1)]);
        builder.Use("@Status", "On");
        Assert.Equal(2, cmd.BoundParameters.Count);
        Assert.Equal("SELECT ID FROM Users WHERE IsActive = @Active AND Status = @Status", builder.GetQueryText());
    }

    [Fact]
    public void Conditions_stay_in_memory_and_never_touch_the_command() {
        var cmd = new FakeCommand();
        var builder = new QueryCommand("SELECT ID, /*More*/Email FROM Users").StartBuilder((DbCommand)cmd);
        Assert.True(builder.Use("More"));
        Assert.Empty(cmd.BoundParameters);
        Assert.Same(QueryBuilder.Used, builder["More"]);
        Assert.True(builder.UnUse("More"));
        Assert.Null(builder["More"]);
    }
}
