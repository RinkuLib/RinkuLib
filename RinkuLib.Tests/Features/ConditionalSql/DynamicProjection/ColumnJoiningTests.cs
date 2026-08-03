using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Templating;

/// <summary>
/// The <c>&amp;,</c> marker chains columns under one condition, and the <c>?SELECT</c> form turns every
/// projected column into its own condition, with <c>!</c> forcing a column on.
/// </summary>
public class ColumnJoiningTests {
    [Fact]
    public void Chained_columns_render_together() {
        var query = new QueryCommand("SELECT ID, Username, /*IncludeAddress*/City&, Street&, ZipCode FROM Users");
        var builder = query.StartBuilder();
        builder.Use("IncludeAddress");
        Render.Expect(builder, "SELECT ID, Username, City, Street, ZipCode FROM Users");
    }

    [Fact]
    public void Chained_columns_drop_together() {
        var query = new QueryCommand("SELECT ID, Username, /*IncludeAddress*/City&, Street&, ZipCode FROM Users");
        Render.Expect(query.StartBuilder(), "SELECT ID, Username FROM Users");
    }

    [Fact]
    public void Insert_chains_columns_and_values_under_one_condition() {
        var template = "INSERT INTO Profiles (UserID, /*Details*/Bio&, Website&, AvatarURL) VALUES (@UID, /*Details*/@Bio&, @Web&, @Img)";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("Details");
        builder.Use("@UID", 5);
        builder.Use("@Bio", "Hi");
        builder.Use("@Web", "site.com");
        builder.Use("@Img", "pic.png");
        Render.Expect(builder, "INSERT INTO Profiles (UserID, Bio, Website, AvatarURL) VALUES (@UID, @Bio, @Web, @Img)",
            ("@UID", 5), ("@Bio", "Hi"), ("@Web", "site.com"), ("@Img", "pic.png"));
    }

    [Fact]
    public void Insert_chained_on_variables_drops_when_one_is_missing() {
        var template = "INSERT INTO Profiles (UserID, /*@Bio&@Web&@Img*/Bio&, Website&, AvatarURL) VALUES (@UID, ?@Bio&, ?@Web&, ?@Img)";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@Bio", "Hi");
        builder.Use("@Web", "site.com");
        builder.Use("@UID", 5);
        Render.Expect(builder, "INSERT INTO Profiles (UserID) VALUES (@UID)",
            ("@UID", 5), ("@Bio", "Hi"), ("@Web", "site.com"));
    }

    [Fact]
    public void Insert_chained_on_variables_renders_when_all_are_set() {
        var template = "INSERT INTO Profiles (UserID, /*@Bio&@Web&@Img*/Bio&, Website&, AvatarURL) VALUES (@UID, ?@Bio&, ?@Web&, ?@Img)";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@Bio", "Hi");
        builder.Use("@Web", "site.com");
        builder.Use("@Img", "pic.png");
        builder.Use("@UID", 5);
        Render.Expect(builder, "INSERT INTO Profiles (UserID, Bio, Website, AvatarURL) VALUES (@UID, @Bio, @Web, @Img)",
            ("@UID", 5), ("@Bio", "Hi"), ("@Web", "site.com"), ("@Img", "pic.png"));
    }

    /// <summary>
    /// Every column being off leaves the <c>SELECT</c> introducing nothing, and a keyword left with nothing
    /// goes the same way an emptied <c>WHERE</c> does. The engine tells no clause from another, so there is
    /// no case here that a dangling keyword would be kept for, and keeping it would not parse either.
    /// Pin a column with <c>!</c> to keep the projection from emptying.
    /// </summary>
    [Fact]
    public void Extraction_select_with_nothing_used_drops_the_whole_projection() {
        var query = new QueryCommand("?SELECT ID, Username, Email&, Test FROM Users WHERE IsActive = 1");
        Render.Expect(query.StartBuilder(), " FROM Users WHERE IsActive = 1");
    }

    /// <summary>
    /// A projection that empties leaves room for a second <c>SELECT</c> beside it, so one template serves a
    /// count and a row read. Each key drops what the other keeps, and neither knows about the other.
    /// </summary>
    [Fact]
    public void A_marked_select_sits_beside_a_projection() {
        const string sql = "/*Count*/SELECT COUNT(*) ?SELECT ID, Name, Age FROM Users";

        var counting = new QueryCommand(sql).StartBuilder();
        counting.Use("Count");
        Render.Expect(counting, "SELECT COUNT(*) FROM Users");

        var picked = new QueryCommand(sql).StartBuilder();
        picked.Use("ID");
        picked.Use("Name");
        Render.Expect(picked, " SELECT ID, Name FROM Users");
    }

    /// <summary>
    /// Nothing checks that the run picked one of the two, so a run that picks neither, or both, gets a
    /// statement that will not parse. The template is a set of rules about what to keep, not a promise
    /// about what the result reads like, and choosing between the two sides is the caller's.
    /// </summary>
    [Fact]
    public void Picking_neither_or_both_sides_is_left_to_the_caller() {
        const string sql = "/*Count*/SELECT COUNT(*) ?SELECT ID, Name, Age FROM Users";

        Render.Expect(new QueryCommand(sql).StartBuilder(), " FROM Users");

        var both = new QueryCommand(sql).StartBuilder();
        both.Use("Count");
        both.Use("ID");
        Render.Expect(both, "SELECT COUNT(*) SELECT ID FROM Users");
    }

    [Fact]
    public void Extraction_select_keeps_a_forced_column() {
        var query = new QueryCommand("?SELECT ID!, Username, Email&, Test FROM Users WHERE IsActive = 1");
        Render.Expect(query.StartBuilder(), "SELECT ID FROM Users WHERE IsActive = 1");
    }

    [Fact]
    public void Extraction_select_renders_one_used_column() {
        var query = new QueryCommand("?SELECT ID, Username, Email&, Test FROM Users WHERE IsActive = 1");
        var builder = query.StartBuilder();
        builder.Use("ID");
        Render.Expect(builder, "SELECT ID FROM Users WHERE IsActive = 1");
    }

    [Fact]
    public void Extraction_select_joins_chained_column_with_a_used_one() {
        var query = new QueryCommand("?SELECT ID, Username, Email&, Test FROM Users WHERE IsActive = 1");
        var builder = query.StartBuilder();
        builder.Use("ID");
        builder.Use("Test");
        Render.Expect(builder, "SELECT ID, Email, Test FROM Users WHERE IsActive = 1");
    }

    [Fact]
    public void Extraction_select_renders_all_used_columns() {
        var query = new QueryCommand("?SELECT ID, Username, Email&, Test FROM Users WHERE IsActive = 1");
        var builder = query.StartBuilder();
        builder.Use("Test");
        builder.Use("Username");
        builder.Use("ID");
        Render.Expect(builder, "SELECT ID, Username, Email, Test FROM Users WHERE IsActive = 1");
    }

    [Fact]
    public void Extraction_select_applies_to_each_side_of_a_union() {
        var query = new QueryCommand("?SELECT ID, Username, Email&, Test FROM Users WHERE IsActive = 1 UNION ALL ?SELECT ID, Username, Email&, Test FROM Other");
        var builder = query.StartBuilder();
        builder.Use("ID");
        builder.Use("Test");
        Render.Expect(builder, "SELECT ID, Email, Test FROM Users WHERE IsActive = 1 UNION ALL SELECT ID, Email, Test FROM Other");
    }

    [Fact]
    public void Extraction_select_works_inside_a_cte() {
        var query = new QueryCommand("WITH U AS (?SELECT ID, Name, Salary FROM Users) SELECT * FROM U");
        var builder = query.StartBuilder();
        builder.Use("Name");
        Render.Expect(builder, "WITH U AS (SELECT Name FROM Users) SELECT * FROM U");
    }

    [Fact]
    public void Extraction_column_with_implicit_and_needs_the_extra_condition() {
        var query = new QueryCommand("?SELECT ID, Username, /*Admin*/Email FROM Users");
        var builder = query.StartBuilder();
        builder.Use("ID");
        builder.Use("Username");
        builder.Use("Email");
        Render.Expect(builder, "SELECT ID, Username FROM Users");
    }

    [Fact]
    public void Extraction_column_with_implicit_and_renders_when_both_hold() {
        var query = new QueryCommand("?SELECT ID, Username, /*Admin*/Email FROM Users");
        var builder = query.StartBuilder();
        builder.Use("ID");
        builder.Use("Username");
        builder.Use("Email");
        builder.Use("Admin");
        Render.Expect(builder, "SELECT ID, Username, Email FROM Users");
    }

    [Fact]
    public void Extraction_column_with_implicit_and_needs_its_own_key_too() {
        var query = new QueryCommand("?SELECT ID, Username, /*Admin*/Email FROM Users");
        var builder = query.StartBuilder();
        builder.Use("ID");
        builder.Use("Username");
        builder.Use("Admin");
        Render.Expect(builder, "SELECT ID, Username FROM Users");
    }

    [Fact]
    public void Forced_column_inside_a_dropped_wrapping_condition_drops() {
        var query = new QueryCommand("/*Wrapping*/?SELECT ID!, Username, Email&, Test FROM Users WHERE IsActive = 1");
        Render.Expect(query.StartBuilder(), " FROM Users WHERE IsActive = 1");
    }

    [Fact]
    public void Forced_column_inside_a_used_wrapping_condition_renders() {
        var query = new QueryCommand("/*Wrapping*/?SELECT ID!, Username, Email&, Test FROM Users WHERE IsActive = 1");
        var builder = query.StartBuilder();
        builder.Use("Wrapping");
        Render.Expect(builder, "SELECT ID FROM Users WHERE IsActive = 1");
    }

    [Fact]
    public void Forced_column_with_manual_condition_drops_without_it() {
        var query = new QueryCommand("?SELECT /*Manual*/ID!, Username, Email&, Test FROM Users WHERE IsActive = 1");
        Render.Expect(query.StartBuilder(), " FROM Users WHERE IsActive = 1");
    }

    [Fact]
    public void Forced_column_with_manual_condition_renders_with_it() {
        var query = new QueryCommand("?SELECT /*Manual*/ID!, Username, Email&, Test FROM Users WHERE IsActive = 1");
        var builder = query.StartBuilder();
        builder.Use("Manual");
        Render.Expect(builder, "SELECT ID FROM Users WHERE IsActive = 1");
    }

    /// <summary>
    /// A modifier in front of a dynamic projection is swept into the first column's footprint, so the wall
    /// is what holds it out. The column keys are the columns either way, and the modifier the wall isolates
    /// belongs to no column at all, so it stays however few columns are asked for.
    /// </summary>
    [Fact]
    public void A_wall_holds_a_modifier_out_of_the_first_column() {
        const string walled = "?SELECT DISTINCT??? Title, Composer FROM tracks";
        var query = new QueryCommand(walled);
        Assert.Equal(["Title", "Composer"], query.Mapper.Keys.ToArray());

        var one = query.StartBuilder();
        one.Use("Composer");
        Render.Expect(one, "SELECT DISTINCT Composer FROM tracks");

        var both = query.StartBuilder();
        both.Use("Title");
        both.Use("Composer");
        Render.Expect(both, "SELECT DISTINCT Title, Composer FROM tracks");
    }

    /// <summary>
    /// Without the wall the modifier rides with the first column, so asking for the others leaves it behind.
    /// </summary>
    [Fact]
    public void Without_a_wall_the_modifier_rides_with_the_first_column() {
        const string plain = "?SELECT DISTINCT Title, Composer FROM tracks";
        var query = new QueryCommand(plain);
        Assert.Equal(["Title", "Composer"], query.Mapper.Keys.ToArray());

        var one = query.StartBuilder();
        one.Use("Composer");
        Render.Expect(one, "SELECT Composer FROM tracks");

        var both = query.StartBuilder();
        both.Use("Title");
        both.Use("Composer");
        Render.Expect(both, "SELECT DISTINCT Title, Composer FROM tracks");
    }

    /// <summary>
    /// The wall bounds a footprint without keying anything itself, so a marker can still make the modifier
    /// conditional on its own key beside a projection that keys the columns.
    /// </summary>
    [Fact]
    public void A_walled_modifier_can_carry_its_own_marker() {
        const string sql = "?SELECT /*UseDistinct*/DISTINCT??? Title, Composer FROM tracks";
        var query = new QueryCommand(sql);

        var off = query.StartBuilder();
        off.Use("Composer");
        Render.Expect(off, "SELECT Composer FROM tracks");

        var on = query.StartBuilder();
        on.Use("UseDistinct");
        on.Use("Composer");
        Render.Expect(on, "SELECT DISTINCT Composer FROM tracks");
    }

    [Fact]
    public void Ignored_comment_marker_renders_as_a_plain_comment() {
        var query = new QueryCommand("SELECT /*~ optimizer hint */ ID, Username FROM Users WHERE IsActive = @Active");
        Render.Expect(query.StartBuilder(), "SELECT /* optimizer hint */ ID, Username FROM Users WHERE IsActive = @Active");
    }
}
