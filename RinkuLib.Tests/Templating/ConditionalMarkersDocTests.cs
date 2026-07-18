using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Templating;

/// <summary>
/// The worked examples from <c>docs/articles/conditional-sql/conditional-markers.md</c>, kept in sync so the
/// documented behaviour stays covered.
/// </summary>
public class ConditionalMarkersDocTests {
    static QueryBuilder Build(string sql) => new QueryCommand(sql).StartBuilder();

    [Fact]
    public void Written_out_marker_matches_the_shorthand() {
        Render.Expect(Build("SELECT * FROM tracks WHERE Name = ?@Name"), "SELECT * FROM tracks");
        Render.Expect(Build("SELECT * FROM tracks WHERE Name = /*@Name*/@Name"), "SELECT * FROM tracks");
    }

    [Fact]
    public void Custom_key_prunes_a_predicate() {
        var sql = "SELECT TrackId, Name FROM tracks WHERE AlbumId = @albumId AND /*HasComposer*/Composer IS NOT NULL";
        Render.Expect(Build(sql), "SELECT TrackId, Name FROM tracks WHERE AlbumId = @albumId");
        var b = Build(sql);
        b.Use("HasComposer");
        Render.Expect(b, "SELECT TrackId, Name FROM tracks WHERE AlbumId = @albumId AND Composer IS NOT NULL");
    }

    [Fact]
    public void Custom_key_prunes_a_column() {
        Render.Expect(Build("SELECT TrackId, Name, /*ShowPrice*/UnitPrice FROM tracks"), "SELECT TrackId, Name FROM tracks");
    }

    [Theory]
    [InlineData("SELECT TrackId, Name FROM tracks WHERE /*Long*/Milliseconds > @ms")]
    [InlineData("SELECT TrackId, Name FROM tracks WHERE Milliseconds /*Long*/> @ms")]
    [InlineData("SELECT TrackId, Name FROM tracks WHERE Milliseconds > /*Long*/@ms")]
    public void The_marker_sits_anywhere_between_boundaries(string sql) {
        Render.Expect(Build(sql), "SELECT TrackId, Name FROM tracks");
        var b = Build(sql);
        b.Use("Long");
        b.Use("@ms", 1);
        Render.Expect(b, "SELECT TrackId, Name FROM tracks WHERE Milliseconds > @ms", ("@ms", 1));
    }

    [Fact]
    public void The_connector_after_a_footprint_belongs_to_it() {
        var sql = "SELECT * FROM tracks WHERE Composer = @composer /*Extra*/AND Milliseconds > @ms";
        Render.Expect(Build(sql), "SELECT * FROM tracks WHERE Milliseconds > @ms");
    }

    [Fact]
    public void A_subquery_in_the_footprint_comes_along() {
        Render.Expect(Build("SELECT * FROM tracks WHERE /*@AlbumId*/AlbumId = (SELECT AlbumId FROM albums WHERE AlbumId = @AlbumId)"),
            "SELECT * FROM tracks");
    }

    [Fact]
    public void A_parenthesis_bounds_the_footprint() {
        var sql = "SELECT * FROM invoices WHERE Total > @MinTotal AND (Country = @Country OR /*@City*/City = @City)";
        var b = Build(sql);
        b.Use("@MinTotal", 1);
        b.Use("@Country", "US");
        Render.Expect(b, "SELECT * FROM invoices WHERE Total > @MinTotal AND (Country = @Country)", ("@MinTotal", 1), ("@Country", "US"));
    }

    [Fact]
    public void A_clause_marker_takes_the_whole_clause() {
        var sql = "SELECT i.InvoiceId FROM invoices i /*@Country*/INNER JOIN customers c ON i.CustomerId = c.CustomerId WHERE c.Country = ?@Country";
        Render.Expect(Build(sql), "SELECT i.InvoiceId FROM invoices i");
        var b = Build(sql);
        b.Use("@Country", "US");
        Render.Expect(b, "SELECT i.InvoiceId FROM invoices i INNER JOIN customers c ON i.CustomerId = c.CustomerId WHERE c.Country = @Country", ("@Country", "US"));
    }

    [Fact]
    public void Dropping_group_by_strands_the_having() {
        Render.Expect(Build("SELECT Country, COUNT(*) FROM customers /*Grouped*/GROUP BY Country HAVING COUNT(*) > 1"),
            "SELECT Country, COUNT(*) FROM customers HAVING COUNT(*) > 1");
    }

    [Fact]
    public void Pairing_the_dependent_clause_drops_both() {
        Render.Expect(Build("SELECT Country, COUNT(*) FROM customers /*Grouped*/GROUP BY Country /*Grouped*/HAVING COUNT(*) > 1"),
            "SELECT Country, COUNT(*) FROM customers");
    }

    [Fact]
    public void Several_conditions_share_one_footprint() {
        var sql = "SELECT * FROM tracks WHERE /*Cheap*//*InCatalog*/UnitPrice > @minPrice";
        var b = Build(sql);
        b.Use("Cheap");
        b.Use("InCatalog");
        b.Use("@minPrice", 1);
        Render.Expect(b, "SELECT * FROM tracks WHERE UnitPrice > @minPrice", ("@minPrice", 1));
        var b2 = Build(sql);
        b2.Use("Cheap");
        Render.Expect(b2, "SELECT * FROM tracks");
    }

    [Fact]
    public void Ampersand_combines_keys_as_and() {
        var sql = "SELECT * FROM tracks WHERE /*Cheap&InCatalog*/UnitPrice > @minPrice";
        var b = Build(sql);
        b.Use("Cheap");
        b.Use("InCatalog");
        b.Use("@minPrice", 1);
        Render.Expect(b, "SELECT * FROM tracks WHERE UnitPrice > @minPrice", ("@minPrice", 1));
    }

    [Fact]
    public void Keys_read_left_to_right_no_precedence() {
        var sql = "SELECT * FROM tracks WHERE /*Cheap|Pricey&InCatalog*/UnitPrice > @minPrice";
        var b = Build(sql);
        b.Use("Cheap");
        b.Use("InCatalog");
        b.Use("@minPrice", 1);
        Render.Expect(b, "SELECT * FROM tracks WHERE UnitPrice > @minPrice", ("@minPrice", 1));
        var b2 = Build(sql);
        b2.Use("Cheap");
        Render.Expect(b2, "SELECT * FROM tracks");
    }

    [Fact]
    public void Negation_keeps_a_footprint_only_when_the_key_is_absent() {
        Render.Expect(Build("SELECT * FROM products WHERE /*!All*/IsActive = 1"), "SELECT * FROM products WHERE IsActive = 1");
        var b = Build("SELECT * FROM products WHERE /*!All*/IsActive = 1");
        b.Use("All");
        Render.Expect(b, "SELECT * FROM products");
    }

    [Fact]
    public void Negation_must_touch_its_key() {
        var b = Build("SELECT * FROM products WHERE /*! All*/IsActive = 1");
        b.Use("All");
        Render.Expect(b, "SELECT * FROM products WHERE IsActive = 1");
    }

    [Fact]
    public void Merge_welds_two_optional_predicates() {
        var sql = "SELECT * FROM invoices WHERE InvoiceDate > ?@MinDate &AND InvoiceDate < ?@MaxDate";
        var b = Build(sql);
        b.Use("@MinDate", 1);
        Render.Expect(b, "SELECT * FROM invoices", ("@MinDate", 1));
        var b2 = Build(sql);
        b2.Use("@MinDate", 1);
        b2.Use("@MaxDate", 2);
        Render.Expect(b2, "SELECT * FROM invoices WHERE InvoiceDate > @MinDate AND InvoiceDate < @MaxDate", ("@MinDate", 1), ("@MaxDate", 2));
    }

    [Fact]
    public void Merge_or_folds_a_static_condition_into_an_optional_one() {
        Render.Expect(Build("SELECT * FROM customers WHERE Country = 'USA' &OR Country = ?@Country"), "SELECT * FROM customers");
    }

    [Fact]
    public void Merge_comma_welds_projected_columns() {
        var sql = "SELECT Id, Username, /*IncludeAddress*/City&, Street&, ZipCode FROM users";
        Render.Expect(Build(sql), "SELECT Id, Username FROM users");
        var b = Build(sql);
        b.Use("IncludeAddress");
        Render.Expect(b, "SELECT Id, Username, City, Street, ZipCode FROM users");
    }

    [Fact]
    public void The_wall_stops_a_footprint_from_sweeping_distinct() {
        Render.Expect(Build("SELECT DISTINCT /*ShowId*/TrackId, Name FROM tracks"), "SELECT Name FROM tracks");
        Render.Expect(Build("SELECT DISTINCT ??? /*ShowId*/TrackId, Name FROM tracks"), "SELECT DISTINCT  Name FROM tracks");
    }

    [Fact]
    public void The_wall_makes_a_modifier_conditional() {
        Render.Expect(Build("SELECT /*UseDistinct*/DISTINCT ??? Id, Name FROM users"), "SELECT Id, Name FROM users");
    }

    [Fact]
    public void A_literal_comment_passes_through() {
        Render.Expect(Build("/*~ join hint */SELECT TrackId FROM tracks"), "/* join hint */SELECT TrackId FROM tracks");
    }

    [Fact]
    public void Sections_stop_a_footprint_at_then() {
        Render.Expect(Build("CASE WHEN Role = ?@Special /*@Special*/THEN 'S' WHEN Role = 'Admin' THEN 'A' ELSE 'U' END"),
            "CASE WHEN Role = 'Admin' THEN 'A' ELSE 'U' END");
    }

    [Fact]
    public void Insert_pairs_a_column_with_its_value() {
        Render.Expect(Build("INSERT INTO users (Username, /*@Email*/Email) VALUES (@Username, ?@Email)"),
            "INSERT INTO users (Username) VALUES (@Username)");
    }

    [Fact]
    public void Insert_group_welds_a_trio_on_each_side() {
        Render.Expect(Build("INSERT INTO profiles (UserId, /*@Bio*/Bio&, Website&, AvatarUrl) VALUES (@Uid, ?@Bio&, @Web&, @Img)"),
            "INSERT INTO profiles (UserId) VALUES (@Uid)");
    }
}
