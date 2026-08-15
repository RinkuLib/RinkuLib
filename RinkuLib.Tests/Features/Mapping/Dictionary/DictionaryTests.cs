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

    [Fact]
    public void A_named_dictionary_owns_only_columns_with_its_prefix() {
        ColumnInfo[] cols = [
            new("Id", typeof(int), false),
            new("AlbumId", typeof(int), false),
            new("ArtistId", typeof(int), false),
            new("AlbumTitle", typeof(string), false),
            new("ArtistName", typeof(string), false),
        ];

        var row = Rows.ParseOne<DictionaryGroups>(cols, 1, 12, 7, "Absolution", "Muse");

        Assert.Equal(1, row.Id);
        Assert.Equal(["AlbumId", "AlbumTitle"], row.Album.Keys);
        Assert.Equal(["ArtistId", "ArtistName"], row.Artist.Keys);
    }

    [Fact]
    public void Alt_adds_another_prefix_for_a_named_dictionary() {
        ColumnInfo[] cols = [new("RecordId", typeof(int), false), new("RecordTitle", typeof(string), false)];

        var row = Rows.ParseOne<AlternateDictionaryGroup>(cols, 12, "Absolution");

        Assert.Equal(["RecordId", "RecordTitle"], row.Album.Keys);
    }

    [Fact]
    public void A_custom_name_comparer_controls_a_dictionary_group() {
        ColumnInfo[] cols = [new("PayloadCode", typeof(int), false), new("PayloadText", typeof(string), false)];

        var row = Rows.ParseOne<CustomDictionaryGroup>(cols, 4, "ready");

        Assert.Equal(["PayloadCode", "PayloadText"], row.Details.Keys);
    }

    [Fact]
    public void Dictionaries_at_different_levels_keep_their_own_columns() {
        ColumnInfo[] cols = [
            new("HeaderCount", typeof(int), false),
            new("BodyId", typeof(int), false),
            new("BodyAlbumId", typeof(int), false),
            new("BodyAlbumTitle", typeof(string), false),
        ];

        var row = Rows.ParseOne<MixedDictionaryLevels>(cols, 3, 7, 12, "Absolution");

        Assert.Equal(["HeaderCount"], row.Header.Keys);
        Assert.Equal(7, row.Body.Id);
        Assert.Equal(["BodyAlbumId", "BodyAlbumTitle"], row.Body.Album.Keys);
    }
}

public record DictionaryGroups(int Id, Dictionary<string, object> Album, Dictionary<string, object> Artist);
public record AlternateDictionaryGroup([Alt("Record")] Dictionary<string, object> Album);
public record CustomDictionaryGroup([DynamicPrefix("Payload")] Dictionary<string, object> Details);
public record MixedDictionaryLevels(Dictionary<string, object> Header, DictionaryBody Body);
public record DictionaryBody(int Id, Dictionary<string, object> Album) : IDbReadable;
