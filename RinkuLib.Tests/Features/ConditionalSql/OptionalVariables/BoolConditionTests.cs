using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Templating;

/// <summary>
/// The <c>/*Cond*/</c> marker guards the following footprint behind a named condition, with <c>&amp;</c>,
/// <c>|</c>, and <c>!</c> combining conditions and <c>/*@Var*/</c> tying a section to a variable's presence.
/// </summary>
public class BoolConditionTests {
    [Fact]
    public void Toggle_off_removes_predicate_and_dangling_keyword() {
        var query = new QueryCommand("SELECT ID, Username, Email FROM Users WHERE /*ActiveOnly*/Active = 1 ORDER BY Username");
        Render.Expect(query.StartBuilder(), "SELECT ID, Username, Email FROM Users ORDER BY Username");
    }

    [Fact]
    public void Toggle_on_keeps_predicate() {
        var query = new QueryCommand("SELECT ID, Username, Email FROM Users WHERE /*ActiveOnly*/Active = 1 ORDER BY Username");
        var builder = query.StartBuilder();
        builder.Use("ActiveOnly");
        Render.Expect(builder, "SELECT ID, Username, Email FROM Users WHERE Active = 1 ORDER BY Username");
    }

    [Fact]
    public void Unknown_condition_name_is_rejected_by_the_builder() {
        var query = new QueryCommand("SELECT ID FROM Users WHERE /*ActiveOnly*/Active = 1");
        var builder = query.StartBuilder();
        Assert.False(builder.Use("NotInQuery"));
    }

    [Fact]
    public void And_gate_stays_off_with_only_first_condition() {
        var query = new QueryCommand("SELECT ID, Username, Email, /*Internal&Authorized*/SocialSecurityNumber FROM Users");
        var builder = query.StartBuilder();
        builder.Use("Internal");
        Render.Expect(builder, "SELECT ID, Username, Email FROM Users");
    }

    [Fact]
    public void And_gate_stays_off_with_only_second_condition() {
        var query = new QueryCommand("SELECT ID, Username, Email, /*Internal&Authorized*/SocialSecurityNumber FROM Users");
        var builder = query.StartBuilder();
        builder.Use("Authorized");
        Render.Expect(builder, "SELECT ID, Username, Email FROM Users");
    }

    [Fact]
    public void And_gate_opens_with_both_conditions() {
        var query = new QueryCommand("SELECT ID, Username, Email, /*Internal&Authorized*/SocialSecurityNumber FROM Users");
        var builder = query.StartBuilder();
        builder.Use("Internal");
        builder.Use("Authorized");
        Render.Expect(builder, "SELECT ID, Username, Email, SocialSecurityNumber FROM Users");
    }

    [Fact]
    public void Or_gate_stays_off_with_neither_condition() {
        var query = new QueryCommand("SELECT * FROM Products p /*Cond1|Cond2*/INNER JOIN Commands c ON c.ProductID = p.ID");
        Render.Expect(query.StartBuilder(), "SELECT * FROM Products p");
    }

    [Fact]
    public void Or_gate_opens_with_first_condition() {
        var query = new QueryCommand("SELECT * FROM Products p /*Cond1|Cond2*/INNER JOIN Commands c ON c.ProductID = p.ID");
        var builder = query.StartBuilder();
        builder.Use("Cond1");
        Render.Expect(builder, "SELECT * FROM Products p INNER JOIN Commands c ON c.ProductID = p.ID");
    }

    [Fact]
    public void Or_gate_opens_with_second_condition() {
        var query = new QueryCommand("SELECT * FROM Products p /*Cond1|Cond2*/INNER JOIN Commands c ON c.ProductID = p.ID");
        var builder = query.StartBuilder();
        builder.Use("Cond2");
        Render.Expect(builder, "SELECT * FROM Products p INNER JOIN Commands c ON c.ProductID = p.ID");
    }

    [Fact]
    public void Negated_condition_keeps_footprint_by_default() {
        var query = new QueryCommand("SELECT * FROM Products WHERE /*!All*/IsActive = 1");
        Render.Expect(query.StartBuilder(), "SELECT * FROM Products WHERE IsActive = 1");
    }

    [Fact]
    public void Negated_condition_drops_footprint_when_used() {
        var query = new QueryCommand("SELECT * FROM Products WHERE /*!All*/IsActive = 1");
        var builder = query.StartBuilder();
        builder.Use("All");
        Render.Expect(builder, "SELECT * FROM Products");
    }

    [Fact]
    public void Section_tied_to_variable_drops_without_it() {
        var query = new QueryCommand("SELECT p.ID, p.Name FROM Products p /*@VendorName*/INNER JOIN Vendors v ON v.ID = p.VendorID WHERE p.IsActive = 1 AND v.VendorName = ?@VendorName");
        Render.Expect(query.StartBuilder(), "SELECT p.ID, p.Name FROM Products p WHERE p.IsActive = 1");
    }

    [Fact]
    public void Section_tied_to_variable_renders_with_it() {
        var query = new QueryCommand("SELECT p.ID, p.Name FROM Products p /*@VendorName*/INNER JOIN Vendors v ON v.ID = p.VendorID WHERE p.IsActive = 1 AND v.VendorName = ?@VendorName");
        var builder = query.StartBuilder();
        builder.Use("@VendorName", "Microsoft");
        Render.Expect(builder, "SELECT p.ID, p.Name FROM Products p INNER JOIN Vendors v ON v.ID = p.VendorID WHERE p.IsActive = 1 AND v.VendorName = @VendorName",
            ("@VendorName", "Microsoft"));
    }

    [Fact]
    public void Subquery_tied_to_variable_drops_atomically() {
        var query = new QueryCommand("SELECT ID, Name FROM Users WHERE /*@ActionType*/(SELECT Count(*) FROM Actions WHERE UserID = ID AND Type = @ActionType) > 0");
        Render.Expect(query.StartBuilder(), "SELECT ID, Name FROM Users");
    }

    [Fact]
    public void Subquery_tied_to_variable_renders_atomically() {
        var query = new QueryCommand("SELECT ID, Name FROM Users WHERE /*@ActionType*/(SELECT Count(*) FROM Actions WHERE UserID = ID AND Type = @ActionType) > 0");
        var builder = query.StartBuilder();
        builder.Use("@ActionType", 2);
        Render.Expect(builder, "SELECT ID, Name FROM Users WHERE (SELECT Count(*) FROM Actions WHERE UserID = ID AND Type = @ActionType) > 0",
            ("@ActionType", 2));
    }

    [Fact]
    public void Cte_behind_condition_drops_with_the_leading_keyword() {
        var query = new QueryCommand("WITH/*cte*/ parentTable AS (SELECT column1, column2 FROM table_name WHERE cond = 1) SELECT ID, Username, Email FROM Users WHERE IsActive = @Active");
        Render.Expect(query.StartBuilder(), " SELECT ID, Username, Email FROM Users WHERE IsActive = @Active");
    }

    [Fact]
    public void Cte_with_optional_variable_inside_drops_only_the_predicate() {
        var query = new QueryCommand("WITH parentTable AS (SELECT column1, column2 FROM table_name WHERE cond = ?@inner) SELECT ID, Username, Email FROM Users WHERE IsActive = @Active");
        Render.Expect(query.StartBuilder(), "WITH parentTable AS (SELECT column1, column2 FROM table_name) SELECT ID, Username, Email FROM Users WHERE IsActive = @Active");
    }

    [Fact]
    public void Conditional_first_column_drops_with_forced_boundary() {
        var query = new QueryCommand("SELECT DISTINCT??? /*ShowId*/ID, Name FROM Users");
        Render.Expect(query.StartBuilder(), "SELECT DISTINCT Name FROM Users");
    }

    [Fact]
    public void Conditional_first_column_drops_without_forced_boundary() {
        var query = new QueryCommand("SELECT DISTINCT /*ShowId*/ID, Name FROM Users");
        Render.Expect(query.StartBuilder(), "SELECT Name FROM Users");
    }

    [Fact]
    public void Conditional_keyword_with_forced_boundary_drops_alone() {
        var query = new QueryCommand("SELECT /*UseDistinct*/DISTINCT??? ID, Name FROM Users");
        Render.Expect(query.StartBuilder(), "SELECT ID, Name FROM Users");
    }

    [Fact]
    public void Trailing_condition_drops_predicate_and_AND() {
        var query = new QueryCommand("DELETE FROM Logs WHERE LogDate < GETDATE() - 30 AND /*PurgeOldOnly*/IsArchived = 1");
        Render.Expect(query.StartBuilder(), "DELETE FROM Logs WHERE LogDate < GETDATE() - 30");
    }

    [Fact]
    public void Trailing_condition_renders_predicate_and_AND() {
        var query = new QueryCommand("DELETE FROM Logs WHERE LogDate < GETDATE() - 30 AND /*PurgeOldOnly*/IsArchived = 1");
        var builder = query.StartBuilder();
        builder.Use("PurgeOldOnly");
        Render.Expect(builder, "DELETE FROM Logs WHERE LogDate < GETDATE() - 30 AND IsArchived = 1");
    }

    [Fact]
    public void Condition_inside_parens_drops_only_its_own_text() {
        var query = new QueryCommand("DELETE FROM Logs WHERE LogDate < GETDATE() - 30 AND (/*PurgeFromAll*/1=1 OR IsArchived = 1)");
        Render.Expect(query.StartBuilder(), "DELETE FROM Logs WHERE LogDate < GETDATE() - 30 AND ( IsArchived = 1)");
    }

    [Fact]
    public void Condition_inside_parens_renders_its_own_text() {
        var query = new QueryCommand("DELETE FROM Logs WHERE LogDate < GETDATE() - 30 AND (/*PurgeFromAll*/1=1 OR IsArchived = 1)");
        var builder = query.StartBuilder();
        builder.Use("PurgeFromAll");
        Render.Expect(builder, "DELETE FROM Logs WHERE LogDate < GETDATE() - 30 AND (1=1 OR IsArchived = 1)");
    }

    [Fact]
    public void Projection_and_grouping_follow_their_conditions() {
        var template = "SELECT /*Agg*/COUNT(*) AS Total&, SUM(Price) AS Revenue, p.CategoryName, /*NotAgg*/p.BrandName&, p.ID FROM Products p WHERE p.IsActive = 1 /*Agg*/GROUP BY p.CategoryName, p.BrandName";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("NotAgg");
        Render.Expect(builder, "SELECT p.CategoryName, p.BrandName, p.ID FROM Products p WHERE p.IsActive = 1");
    }

    [Fact]
    public void Projection_and_grouping_render_for_the_aggregate_side() {
        var template = "SELECT /*Agg*/COUNT(*) AS Total&, SUM(Price) AS Revenue, p.CategoryName, /*NotAgg*/p.BrandName&, p.ID FROM Products p WHERE p.IsActive = 1 /*Agg*/GROUP BY p.CategoryName, p.BrandName";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("Agg");
        Render.Expect(builder, "SELECT COUNT(*) AS Total, SUM(Price) AS Revenue, p.CategoryName FROM Products p WHERE p.IsActive = 1 GROUP BY p.CategoryName, p.BrandName");
    }

    [Fact]
    public void Projection_and_grouping_render_fully_with_both_conditions() {
        var template = "SELECT /*Agg*/COUNT(*) AS Total&, SUM(Price) AS Revenue, p.CategoryName, /*NotAgg*/p.BrandName&, p.ID FROM Products p WHERE p.IsActive = 1 /*Agg*/GROUP BY p.CategoryName, p.BrandName";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("Agg");
        builder.Use("NotAgg");
        Render.Expect(builder, "SELECT COUNT(*) AS Total, SUM(Price) AS Revenue, p.CategoryName, p.BrandName, p.ID FROM Products p WHERE p.IsActive = 1 GROUP BY p.CategoryName, p.BrandName");
    }
}
