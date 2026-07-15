using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Templating;

/// <summary>
/// The factory reads a template into segments, conditions, and the key map that numbers every
/// variable and condition. These tests pin the layout the rest of the engine addresses by index.
/// </summary>
public class QueryFactoryTests {
    private const string MixedTemplate =
        "SELECT /*Cond*/Name FROM @Table_R WHERE ID = @ID AND Cat IN (?@Cats_X) AND Status = ?@Status";

    [Fact]
    public void Keys_are_grouped_normal_special_base_then_conditions() {
        var query = new QueryCommand(MixedTemplate);
        Assert.Equal(0, query.Mapper.GetIndex("@ID"));
        Assert.Equal(1, query.Mapper.GetIndex("@Status"));
        Assert.Equal(2, query.Mapper.GetIndex("@Cats"));
        Assert.Equal(3, query.Mapper.GetIndex("@Table"));
        Assert.Equal(4, query.Mapper.GetIndex("Cond"));
    }

    [Fact]
    public void Start_indexes_split_the_key_ranges() {
        var query = new QueryCommand(MixedTemplate);
        Assert.Equal(2, query.StartSpecialHandlers);
        Assert.Equal(3, query.StartBaseHandlers);
        Assert.Equal(4, query.StartBoolCond);
        Assert.Equal(5, query.Mapper.Count);
    }

    [Fact]
    public void Factory_counts_each_variable_kind() {
        var factory = new QueryFactory(MixedTemplate, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);
        Assert.Equal(2, factory.NbNormalVar);
        Assert.Equal(1, factory.NbSpecialHandlers);
        Assert.Equal(1, factory.NbBaseHandlers);
        Assert.Equal(1, factory.NbNonVarComment);
    }

    [Fact]
    public void Template_without_markers_has_no_keys_and_one_segment() {
        var factory = new QueryFactory("SELECT ID, Name FROM Users", '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);
        Assert.Equal(0, factory.Mapper.Count);
        var segment = Assert.Single(factory.Segments);
        Assert.Equal(factory.Query, factory.Query.Substring(segment.Start, segment.Length));
    }

    [Fact]
    public void Segments_reconstruct_the_stripped_query() {
        var factory = new QueryFactory(MixedTemplate, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);
        var rebuilt = string.Concat(factory.Segments.Select(s => factory.Query.Substring(s.Start, s.Length)));
        Assert.Equal(factory.Query, rebuilt);
    }

    [Fact]
    public void Conditions_end_with_a_sentinel_pointing_past_the_keys() {
        var factory = new QueryFactory(MixedTemplate, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);
        Assert.Equal(factory.Mapper.Count, factory.Conditions[^1].CondIndex);
    }

    [Fact]
    public void Base_handler_letters_respect_the_special_handler_claim() {
        var factory = new QueryFactory("SELECT 1", '@', specialHandlerPresenceMap: 1u << ('x' - 'a'));
        Assert.True(factory.IsBaseHandler('S'));
        Assert.True(factory.IsBaseHandler('s'));
        Assert.True(factory.IsBaseHandler('R'));
        Assert.True(factory.IsBaseHandler('N'));
        Assert.False(factory.IsBaseHandler('X'));
        Assert.False(factory.IsBaseHandler('Z'));
        Assert.False(factory.IsBaseHandler('_'));
    }

    [Fact]
    public void Custom_variable_char_takes_over_marking() {
        var query = new QueryCommand("SELECT ID FROM Users WHERE ID = :ID AND Status = ?:Status", ':');
        var builder = query.StartBuilder();
        Assert.True(builder.Use(":ID", 7));
        Render.Expect(builder, "SELECT ID FROM Users WHERE ID = :ID", (":ID", 7));
    }

    [Fact]
    public void Custom_variable_char_ignores_the_default_marker() {
        var query = new QueryCommand("SELECT ID FROM Users WHERE Email = '@literal' AND ID = :ID", ':');
        Assert.Equal(1, query.Mapper.Count);
        Assert.Equal(0, query.Mapper.GetIndex(":ID"));
    }

    [Fact]
    public void Ignored_comment_produces_no_keys() {
        var factory = new QueryFactory("SELECT /*~ hint */ ID FROM Users", '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);
        Assert.Equal(0, factory.Mapper.Count);
    }

    [Fact]
    public void Variable_names_are_looked_up_case_insensitively() {
        var query = new QueryCommand("SELECT ID FROM Users WHERE IsActive = @Active");
        Assert.Equal(0, query.Mapper.GetIndex("@ACTIVE"));
        Assert.Equal(0, query.Mapper.GetIndex("@active"));
    }

    [Fact]
    public void Repeated_variable_maps_to_one_key() {
        var query = new QueryCommand("SELECT * FROM Users WHERE ID = @ID OR ManagerID = @ID");
        Assert.Equal(1, query.Mapper.Count);
    }

    [Fact]
    public void IsInCondition_tells_optional_variables_from_required_ones() {
        var query = new QueryCommand("SELECT * FROM T WHERE A = @A AND B = ?@B");
        Assert.False(query.QueryText.IsInCondition(query.Mapper.GetIndex("@A")));
        Assert.True(query.QueryText.IsInCondition(query.Mapper.GetIndex("@B")));
    }
}
