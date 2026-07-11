using System.Data;
using RinkuLib.DbParsing;
using RinkuLib.Tools;
using Xunit;
namespace RinkuLib.Tests.DbParsing;
// The runtime equivalent of a `= default` parameter: an IFallbackParserGetter on the slot.
public class ParamFallbackTests {
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
    // Code has no C# default, so it is required. Attaching DefaultValueFallback at runtime makes it optional.
    [Fact]
    public void DefaultValueFallback_makes_required_slot_optional() {
        if (TypeParsingInfo.GetOrAdd<PfTrack>() is ICanProvideConstructions info) {
            var slots = info.PossibleConstructors[0].Parameters;
            var s = slots[2];
            slots[2] = new ParamInfoPlus(s.Type, s.NullColHandler, s.NameComparer,
                IColModifier.Nothing, DefaultValueFallback.Instance);
        }
        var v = Parse<PfTrack>([new("Id", typeof(int), false), new("Name", typeof(string), false)], [1, "x"]);
        Assert.Equal(1, v.Id);
        Assert.Equal("x", v.Name);
        Assert.Equal(0, v.Code);   // no Code column, fell back to the type default
    }
}
public record struct PfTrack(int Id, string Name, int Code);
