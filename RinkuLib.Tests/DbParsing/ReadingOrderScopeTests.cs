using System.Data;
using RinkuLib.DbParsing;
using RinkuLib.Tools;
using Xunit;
namespace RinkuLib.Tests.DbParsing;
public class ReadingOrderScopeTests {
    private static DataTableReader Reader(ColumnInfo[] columns, object[] row) {
        DataTable table = new();
        foreach (var col in columns)
            table.Columns.Add(new DataColumn(col.Name, col.Type) { AllowDBNull = col.IsNullable });
        table.Rows.Add(row);
        return table.CreateDataReader();
    }
    private static T Parse<T>(ColumnInfo[] cols, object[] row) {
        using var r = Reader(cols, row);
        var p = TypeParser.GetTypeParser<T>(ref cols);
        r.Read();
        return p.Parse(r).Result;
    }
    [Fact]
    public void Free_complex_subtree_skips_gap() {
        var v = Parse<RosOuterFree>(
            [new("Id", typeof(int), false), new("SubA", typeof(int), false), new("Gap", typeof(int), false), new("SubB", typeof(int), true)],
            [1, 10, 99, 20]);
        Assert.Equal(10, v.Sub.A);
        Assert.Equal(20, v.Sub.B);
    }
    [Fact]
    public void CanNotLookAnywhereSubtree_makes_whole_subtree_sequential() {
        var v = Parse<RosOuterSubSeq>(
            [new("Id", typeof(int), false), new("SubA", typeof(int), false), new("Gap", typeof(int), false), new("SubB", typeof(int), true)],
            [1, 10, 99, 20]);
        Assert.Equal(10, v.Sub.A);
        Assert.Null(v.Sub.B);
    }
    [Fact]
    public void CanLookAnywhere_slot_frees_only_first_column() {
        ColumnInfo[] cols = [
            new("X", typeof(int), false), new("Key", typeof(int), false),
            new("Junk", typeof(int), false), new("DataA", typeof(int), false),
            new("Gap", typeof(int), false), new("DataB", typeof(int), true)
            ];
        var (x, h) = Parse<(int, RosHolderSlot)>(cols, [1, 2, 99, 10, 88, 20]);
        Assert.Equal(1, x);
        Assert.Equal(2, h.Key);
        Assert.Equal(10, h.Data.A);
        Assert.Null(h.Data.B);
    }
    [Fact]
    public void CanLookAnywhereSubtree_frees_whole_subtree() {
        ColumnInfo[] cols = [
            new("X", typeof(int), false), new("Key", typeof(int), false),
            new("Junk", typeof(int), false), new("DataA", typeof(int), false),
            new("Gap", typeof(int), false), new("DataB", typeof(int), true)
            ];
        var (x, h) = Parse<(int, RosHolderSubtree)>(cols, [1, 2, 99, 10, 88, 20]);
        Assert.Equal(10, h.Data.A);
        Assert.Equal(20, h.Data.B);
    }
}
public record RosInner(int A, int? B = null) : IDbReadable;
public record RosOuterFree(int Id, RosInner Sub);
public record RosOuterSubSeq(int Id, [CanNotLookAnywhereSubtree] RosInner Sub);
public record RosHolderSlot(int Key, [CanLookAnywhere] RosInner Data) : IDbReadable;
public record RosHolderSubtree(int Key, [CanLookAnywhereSubtree] RosInner Data) : IDbReadable;
