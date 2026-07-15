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
        // @Img missing: the whole chained block drops, but set values still bind
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

    [Fact]
    public void Extraction_select_with_nothing_used_drops_the_whole_projection() {
        var query = new QueryCommand("?SELECT ID, Username, Email&, Test FROM Users WHERE IsActive = 1");
        Render.Expect(query.StartBuilder(), " FROM Users WHERE IsActive = 1");
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
        var query = new QueryCommand("?SELECT ID, Username, /*#Admin*/Email FROM Users");
        var builder = query.StartBuilder();
        builder.Use("ID");
        builder.Use("Username");
        builder.Use("Email");
        // #Admin was never used, so Email stays out
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

    [Fact]
    public void Ignored_comment_marker_renders_as_a_plain_comment() {
        var query = new QueryCommand("SELECT /*~ optimizer hint */ ID, Username FROM Users WHERE IsActive = @Active");
        Render.Expect(query.StartBuilder(), "SELECT /* optimizer hint */ ID, Username FROM Users WHERE IsActive = @Active");
    }
}
