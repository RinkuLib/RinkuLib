using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Building;

/// <summary>
/// The in-memory builder: values live in <see cref="QueryBuilder.Variables"/> and nothing touches a
/// command until the run.
/// </summary>
public class QueryBuilderTests {
    private static readonly string Template =
        "SELECT ID, /*ShowEmail*/Email FROM Users WHERE IsActive = @Active AND Status = ?@Status";

    [Fact]
    public void Builder_allocates_one_slot_per_key() {
        var builder = new QueryCommand(Template).StartBuilder();
        Assert.Equal(builder.QueryCommand.Mapper.Count, builder.Variables.Length);
        Assert.All(builder.Variables, Assert.Null);
    }

    [Fact]
    public void Setting_a_variable_fills_its_slot() {
        var builder = new QueryCommand(Template).StartBuilder();
        Assert.True(builder.Use("@Active", true));
        Assert.Equal(true, builder["@Active"]);
    }

    [Fact]
    public void Setting_a_variable_by_char_and_name_matches_the_full_name() {
        var builder = new QueryCommand(Template).StartBuilder();
        Assert.True(builder.Use('@', "Active", 5));
        Assert.Equal(5, builder["@Active"]);
    }

    [Fact]
    public void Setting_a_variable_by_span_matches_the_string_form() {
        var builder = new QueryCommand(Template).StartBuilder();
        Assert.True(builder.Use("@Status".AsSpan(), "On"));
        Assert.Equal("On", builder["@Status".AsSpan()]);
    }

    [Fact]
    public void Using_a_condition_stores_the_Used_sentinel() {
        var builder = new QueryCommand(Template).StartBuilder();
        Assert.True(builder.Use("ShowEmail"));
        Assert.Same(QueryBuilder.Used, builder["ShowEmail"]);
    }

    [Fact]
    public void Using_a_condition_by_span_matches_the_string_form() {
        var builder = new QueryCommand(Template).StartBuilder();
        Assert.True(builder.Use("ShowEmail".AsSpan()));
        Assert.Same(QueryBuilder.Used, builder["ShowEmail"]);
    }

    [Fact]
    public void Using_a_variable_name_as_a_condition_is_rejected() {
        var builder = new QueryCommand(Template).StartBuilder();
        Assert.False(builder.Use("@Active"));
    }

    [Fact]
    public void Using_a_condition_name_as_a_variable_is_rejected() {
        var builder = new QueryCommand(Template).StartBuilder();
        Assert.False(builder.Use("ShowEmail", 5));
    }

    [Fact]
    public void Unknown_names_are_rejected() {
        var builder = new QueryCommand(Template).StartBuilder();
        Assert.False(builder.Use("Nope"));
        Assert.False(builder.Use("@Nope", 1));
        Assert.False(builder.Use("Nope".AsSpan()));
        Assert.False(builder.Use("@Nope".AsSpan(), 1));
    }

    [Fact]
    public void UnUse_clears_a_condition() {
        var builder = new QueryCommand(Template).StartBuilder();
        builder.Use("ShowEmail");
        Assert.True(builder.UnUse("ShowEmail"));
        Assert.Null(builder["ShowEmail"]);
    }

    [Fact]
    public void UnUse_by_span_clears_a_condition() {
        var builder = new QueryCommand(Template).StartBuilder();
        builder.Use("ShowEmail");
        Assert.True(builder.UnUse("ShowEmail".AsSpan()));
        Assert.Null(builder["ShowEmail"]);
    }

    [Fact]
    public void UnUse_rejects_variable_names() {
        var builder = new QueryCommand(Template).StartBuilder();
        builder.Use("@Active", 1);
        Assert.False(builder.UnUse("@Active"));
        Assert.Equal(1, builder["@Active"]);
    }

    [Fact]
    public void Remove_clears_any_slot() {
        var builder = new QueryCommand(Template).StartBuilder();
        builder.Use("@Active", 1);
        builder.Remove("@Active");
        Assert.Null(builder["@Active"]);
    }

    [Fact]
    public void Remove_by_span_clears_any_slot() {
        var builder = new QueryCommand(Template).StartBuilder();
        builder.Use("@Active", 1);
        builder.Remove("@Active".AsSpan());
        Assert.Null(builder["@Active"]);
    }

    [Fact]
    public void Reset_clears_every_slot() {
        var builder = new QueryCommand(Template).StartBuilder();
        builder.Use("@Active", 1);
        builder.Use("@Status", "On");
        builder.Use("ShowEmail");
        builder.Reset();
        Assert.All(builder.Variables, Assert.Null);
    }

    [Fact]
    public void Index_based_access_mirrors_name_based_access() {
        var builder = new QueryCommand(Template).StartBuilder();
        var ind = builder.QueryCommand.Mapper.GetIndex("@Active");
        Assert.True(builder.Use(ind, 9));
        Assert.Equal(9, builder[ind]);
        builder.UnUse(ind);
        Assert.Null(builder[ind]);
    }

    [Fact]
    public void Index_based_condition_toggle_stores_the_sentinel() {
        var builder = new QueryCommand(Template).StartBuilder();
        var ind = builder.QueryCommand.Mapper.GetIndex("ShowEmail");
        builder.Use(ind);
        Assert.Same(QueryBuilder.Used, builder[ind]);
    }

    [Fact]
    public void GetQueryText_renders_without_a_command() {
        var builder = new QueryCommand(Template).StartBuilder();
        builder.Use("ShowEmail");
        Assert.Equal("SELECT ID, Email FROM Users WHERE IsActive = @Active", builder.GetQueryText());
    }

    [Fact]
    public void StartBuilder_with_values_preloads_variables() {
        var query = new QueryCommand(Template);
        var builder = query.StartBuilder([("@Active", 0), ("@Status", "On")]);
        Assert.Equal(0, builder["@Active"]);
        Assert.Equal("On", builder["@Status"]);
    }

    [Fact]
    public void StartBuilder_with_values_ignores_condition_names() {
        var query = new QueryCommand(Template);
        var builder = query.StartBuilder([("ShowEmail", QueryBuilder.Used)]);
        Assert.Null(builder["ShowEmail"]);
    }

    [Fact]
    public void Builders_from_one_command_do_not_share_state() {
        var query = new QueryCommand(Template);
        var first = query.StartBuilder();
        first.Use("@Active", 1);
        var second = query.StartBuilder();
        Assert.Null(second["@Active"]);
    }
}
