using Rinku.Mapping;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>
/// Dictionary mapping deliberately reads the current reader schema per row: one root parser accepts changing
/// projections, while a nested dictionary takes every column not claimed by its typed siblings.
/// </summary>
public class DictionaryTests {
    [Fact]
    public void One_root_parser_reads_unrelated_runtime_schemas() {
        ColumnInfo[] firstSchema = [
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
        ];
        ColumnInfo[] secondSchema = [
            new("When", typeof(DateTime), false),
            new("Score", typeof(decimal), false),
            new("Note", typeof(string), true),
        ];

        var firstParser = TypeParser.GetTypeParser<Dictionary<string, object>>(firstSchema);
        var secondParser = TypeParser.GetTypeParser<Dictionary<string, object>>(secondSchema);

        Assert.Same(firstParser, secondParser);
        using var firstReader = Rows.Reader(firstSchema, [7, "seven"]);
        Assert.True(firstReader.Read());
        var first = firstParser.Parse(firstReader).Result;
        Assert.Equal(["Id", "Name"], first.Keys);
        Assert.Equal(7, first["id"]);
        Assert.Equal("seven", first["NAME"]);

        var when = new DateTime(2026, 8, 8, 12, 30, 0);
        using var secondReader = Rows.Reader(secondSchema, [when, 12.5m, DBNull.Value]);
        Assert.True(secondReader.Read());
        var second = firstParser.Parse(secondReader).Result;
        Assert.Equal(["When", "Score", "Note"], second.Keys);
        Assert.Equal(when, second["When"]);
        Assert.Equal(12.5m, second["Score"]);
        Assert.Null(second["Note"]);
    }

    [Fact]
    public void A_nested_dictionary_takes_the_runtime_remainder() {
        ColumnInfo[] firstSchema = [
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
        ];
        ColumnInfo[] secondSchema = [
            new("Id", typeof(int), false),
            new("Email", typeof(string), false),
            new("Score", typeof(long), false),
        ];

        var firstParser = TypeParser.GetTypeParser<(int Id, Dictionary<string, object> Remaining)>(firstSchema);
        var secondParser = TypeParser.GetTypeParser<(int Id, Dictionary<string, object> Remaining)>(secondSchema);

        Assert.Same(firstParser, secondParser);
        using var reader = Rows.Reader(secondSchema, [3, "three@example.test", 42L]);
        Assert.True(reader.Read());
        var value = firstParser.Parse(reader).Result;
        Assert.Equal(3, value.Id);
        Assert.Equal(["Email", "Score"], value.Remaining.Keys);
        Assert.Equal("three@example.test", value.Remaining["Email"]);
        Assert.Equal(42L, value.Remaining["Score"]);
    }
}
