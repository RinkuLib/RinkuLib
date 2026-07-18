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
}
