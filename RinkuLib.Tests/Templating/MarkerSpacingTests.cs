using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Templating;

/// <summary>
/// Pins how each spacing variation around a marker renders. A space on only one side collapses cleanly; a
/// space on both sides (or a marker's leading space meeting a section's) currently leaves a double space.
/// These lock the actual output so a change to the normalizer's spacing is caught.
/// </summary>
public class MarkerSpacingTests {
    static QueryBuilder Build(string sql) => new QueryCommand(sql).StartBuilder();


    [Theory]
    [InlineData("SELECT Id, Name, /*Show*/Price FROM t")]
    [InlineData("SELECT Id, Name,/*Show*/ Price FROM t")]
    [InlineData("SELECT Id, Name, /*Show*/ Price FROM t")]
    [InlineData("SELECT Id, Name,/*Show*/Price FROM t")]
    public void Column_marker_pruned_drops_the_whole_footprint(string sql)
        => Render.Expect(Build(sql), "SELECT Id, Name FROM t");

    [Theory]
    [InlineData("SELECT Id, Name, /*Show*/Price FROM t", "SELECT Id, Name, Price FROM t")]
    [InlineData("SELECT Id, Name,/*Show*/ Price FROM t", "SELECT Id, Name, Price FROM t")] 
    [InlineData("SELECT Id, Name, /*Show*/ Price FROM t", "SELECT Id, Name,  Price FROM t")] 
    [InlineData("SELECT Id, Name,/*Show*/Price FROM t", "SELECT Id, Name,Price FROM t")] 
    public void Column_marker_kept(string sql, string expected) {
        var b = Build(sql);
        b.Use("Show");
        Render.Expect(b, expected);
    }

    [Theory]
    [InlineData("SELECT a, /*Show*/ b FROM t", "SELECT a,  b FROM t")]            
    [InlineData("SELECT a,\t/*Show*/\tb FROM t", "SELECT a,\t\tb FROM t")]          
    [InlineData("SELECT a,\r\n/*Show*/\r\nb FROM t", "SELECT a,\r\n\r\nb FROM t")]   
    [InlineData("SELECT a,   /*Show*/   b FROM t", "SELECT a,      b FROM t")]      
    [InlineData("SELECT a, \t /*Show*/ \t b FROM t", "SELECT a, \t  \t b FROM t")]  
    [InlineData("SELECT a,\t /*Show*/b FROM t", "SELECT a,\t b FROM t")]            
    public void Column_marker_kept_preserves_whitespace_verbatim(string sql, string expected) {
        var b = Build(sql);
        b.Use("Show");
        Render.Expect(b, expected);
    }

    [Theory]
    [InlineData("SELECT a, /*Show*/ b FROM t")]
    [InlineData("SELECT a,\t/*Show*/\tb FROM t")]
    [InlineData("SELECT a,\r\n/*Show*/\r\nb FROM t")]
    [InlineData("SELECT a,   /*Show*/   b FROM t")]
    [InlineData("SELECT a, \t /*Show*/ \t b FROM t")]
    public void Column_marker_pruned_regardless_of_whitespace(string sql)
        => Render.Expect(Build(sql), "SELECT a FROM t");


    [Theory]
    [InlineData("SELECT Id FROM t WHERE /*Long*/Ms > @ms")]
    [InlineData("SELECT Id FROM t WHERE/*Long*/ Ms > @ms")]
    [InlineData("SELECT Id FROM t WHERE /*Long*/ Ms > @ms")]
    [InlineData("SELECT Id FROM t WHERE Ms /*Long*/> @ms")]
    [InlineData("SELECT Id FROM t WHERE Ms > /*Long*/@ms")]
    public void Predicate_marker_pruned_drops_the_whole_footprint(string sql)
        => Render.Expect(Build(sql), "SELECT Id FROM t");

    [Theory]
    [InlineData("SELECT Id FROM t WHERE /*Long*/Ms > @ms", "SELECT Id FROM t WHERE Ms > @ms")]
    [InlineData("SELECT Id FROM t WHERE/*Long*/ Ms > @ms", "SELECT Id FROM t WHERE Ms > @ms")] 
    [InlineData("SELECT Id FROM t WHERE /*Long*/ Ms > @ms", "SELECT Id FROM t WHERE  Ms > @ms")] 
    [InlineData("SELECT Id FROM t WHERE Ms /*Long*/> @ms", "SELECT Id FROM t WHERE Ms > @ms")] 
    [InlineData("SELECT Id FROM t WHERE Ms > /*Long*/@ms", "SELECT Id FROM t WHERE Ms > @ms")]
    public void Predicate_marker_kept(string sql, string expected) {
        var b = Build(sql);
        b.Use("Long");
        Render.Expect(b, expected);
    }

    [Theory]
    [InlineData("SELECT Id FROM t WHERE\t/*Long*/\tMs > @ms", "SELECT Id FROM t WHERE\t\tMs > @ms")]  
    [InlineData("SELECT Id FROM t WHERE\r\n/*Long*/ Ms > @ms", "SELECT Id FROM t WHERE\r\n Ms > @ms")] 
    [InlineData("SELECT Id FROM t WHERE  /*Long*/\tMs > @ms", "SELECT Id FROM t WHERE  \tMs > @ms")]  
    [InlineData("SELECT Id FROM t WHERE/*Long*/\r\nMs > @ms", "SELECT Id FROM t WHERE\r\nMs > @ms")]  
    public void Predicate_marker_kept_preserves_whitespace_verbatim(string sql, string expected) {
        var b = Build(sql);
        b.Use("Long");
        Render.Expect(b, expected);
    }

    [Theory]
    [InlineData("SELECT Id FROM t WHERE\t/*Long*/\tMs > @ms")]
    [InlineData("SELECT Id FROM t WHERE\r\n/*Long*/\r\nMs > @ms")]
    [InlineData("SELECT Id FROM t WHERE  /*Long*/\tMs > @ms")]
    public void Predicate_marker_pruned_regardless_of_whitespace(string sql)
        => Render.Expect(Build(sql), "SELECT Id FROM t");


    [Theory]
    [InlineData("SELECT i FROM inv i /*J*/JOIN c ON c.Id = i.Cid WHERE 1 = 1", "SELECT i FROM inv i WHERE 1 = 1")]
    [InlineData("SELECT i FROM inv i /*J*/ JOIN c ON c.Id = i.Cid WHERE 1 = 1", "SELECT i FROM inv i WHERE 1 = 1")]
    public void Clause_marker_pruned(string sql, string expected)
        => Render.Expect(Build(sql), expected);

    [Theory]
    [InlineData("SELECT i FROM inv i /*J*/JOIN c ON c.Id = i.Cid WHERE 1 = 1", "SELECT i FROM inv i JOIN c ON c.Id = i.Cid WHERE 1 = 1")]
    [InlineData("SELECT i FROM inv i /*J*/ JOIN c ON c.Id = i.Cid WHERE 1 = 1", "SELECT i FROM inv i  JOIN c ON c.Id = i.Cid WHERE 1 = 1")]
    public void Clause_marker_kept(string sql, string expected) {
        var b = Build(sql);
        b.Use("J");
        Render.Expect(b, expected);
    }

    [Theory]
    [InlineData("SELECT i FROM inv i\t/*J*/\tJOIN c ON c.Id = i.Cid WHERE 1 = 1", "SELECT i FROM inv i\t\tJOIN c ON c.Id = i.Cid WHERE 1 = 1")]
    public void Clause_marker_kept_preserves_whitespace_verbatim(string sql, string expected) {
        var b = Build(sql);
        b.Use("J");
        Render.Expect(b, expected);
    }

    [Theory]
    [InlineData("SELECT i FROM inv i\t/*J*/\tJOIN c ON c.Id = i.Cid WHERE 1 = 1", "SELECT i FROM inv i WHERE 1 = 1")]
    [InlineData("SELECT i FROM inv i /*J*/\tJOIN c ON c.Id = i.Cid WHERE 1 = 1", "SELECT i FROM inv i WHERE 1 = 1")]
    public void Clause_marker_pruned_keeps_the_surrounding_whitespace(string sql, string expected)
        => Render.Expect(Build(sql), expected);


    [Theory]
    [InlineData("SELECT * FROM t WHERE a > ?@Min &AND b < ?@Max")]
    [InlineData("SELECT * FROM t WHERE a > ?@Min&AND b < ?@Max")]
    [InlineData("SELECT * FROM t WHERE a > ?@Min &AND  b < ?@Max")]
    public void Merge_connector_spacing_pruned_is_the_same(string sql) {
        var b = Build(sql);
        b.Use("@Min", 1);
        Render.Expect(b, "SELECT * FROM t", ("@Min", 1));
    }

    /// <summary>
    /// A footprint starts just past the boundary that opens it, so the space after a comma is the next
    /// entry's and goes when that entry goes. Pruning the first entry of a list therefore takes the space
    /// in front of it, the one following the keyword that opened the list.
    /// </summary>
    [Theory]
    [InlineData("SELECT /*K*/a, b, c FROM t", "SELECT b, c FROM t")]
    [InlineData("SELECT a, /*K*/b, c FROM t", "SELECT a, c FROM t")]
    [InlineData("SELECT a, b, /*K*/c FROM t", "SELECT a, b FROM t")]
    public void A_pruned_list_entry_takes_the_space_that_opened_it(string sql, string expected)
        => Render.Expect(Build(sql), expected);

    /// <summary>
    /// The space before a section keyword belongs to the condition in front of it, so an emptied clause
    /// leaves the keyword sitting one space after whatever now precedes it rather than two.
    /// </summary>
    [Theory]
    [InlineData("SELECT * FROM t WHERE /*K*/a = 1 ORDER BY x", "SELECT * FROM t ORDER BY x")]
    [InlineData("SELECT * FROM t WHERE a = 1 AND /*K*/b = 2 ORDER BY x", "SELECT * FROM t WHERE a = 1 ORDER BY x")]
    [InlineData("SELECT a FROM t /*K*/JOIN u ON u.i = t.i WHERE y = 2", "SELECT a FROM t WHERE y = 2")]
    public void A_section_keeps_one_space_from_what_precedes_it(string sql, string expected)
        => Render.Expect(Build(sql), expected);

    /// <summary>
    /// The only space the engine adds. A section keyword written against the text before it would weld to a
    /// kept condition, so one is put between them. Pruning that condition needs no such help.
    /// </summary>
    [Fact]
    public void A_welded_section_keyword_is_the_one_added_space() {
        const string sql = "SELECT a, /*K*/COUNT(b)FROM t";
        var kept = Build(sql);
        kept.Use("K");
        Render.Expect(kept, "SELECT a, COUNT(b) FROM t");
        Render.Expect(Build(sql), "SELECT a FROM t");
    }

    /// <summary>
    /// The wall emits nothing and the spaces around it are outside the footprint it bounds, so they stay
    /// exactly as written and a spaced wall leaves one more than a welded one.
    /// </summary>
    [Theory]
    [InlineData("SELECT DISTINCT??? /*K*/a, b FROM t", "SELECT DISTINCT b FROM t")]
    [InlineData("SELECT DISTINCT ??? /*K*/a, b FROM t", "SELECT DISTINCT  b FROM t")]
    public void The_wall_leaves_the_spaces_around_it(string sql, string expected)
        => Render.Expect(Build(sql), expected);
}
