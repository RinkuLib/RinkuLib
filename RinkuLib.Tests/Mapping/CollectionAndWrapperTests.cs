using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>
/// Collection shapes read the whole result and wrapper shapes change how a missing or null first row
/// is treated, at the parser level.
/// </summary>
public class CollectionAndWrapperTests {
    private static readonly ColumnInfo[] IdName = [new("Id", typeof(int), false), new("Name", typeof(string), false)];

    [Fact]
    public void List_of_scalars_reads_every_row() {
        ColumnInfo[] cols = [new("V", typeof(int), false)];
        using var reader = Rows.Reader(cols, [1], [2], [3]);
        var parser = TypeParser.GetTypeParser<List<int>>(ref cols);
        reader.Read();
        var (canContinue, list) = parser.Parse(reader);
        Assert.False(canContinue);
        Assert.Equal([1, 2, 3], list);
    }

    [Fact]
    public void List_of_objects_reads_every_row() {
        var cols = IdName;
        using var reader = Rows.Reader(cols, [1, "a"], [2, "b"]);
        var parser = TypeParser.GetTypeParser<List<PropUser>>(ref cols);
        reader.Read();
        var list = parser.Parse(reader).Result;
        Assert.Equal(2, list.Count);
        Assert.Equal("a", list[0].Name);
        Assert.Equal("b", list[1].Name);
    }

    [Fact]
    public void Optional_returns_null_when_no_row_exists() {
        ColumnInfo[] cols = [new("V", typeof(string), false)];
        using var reader = Rows.Reader(cols);
        var parser = TypeParser.GetTypeParser<Optional<string>>(ref cols);
        Assert.False(reader.Read());
        string? value = parser.Default();
        Assert.Null(value);
    }

    [Fact]
    public void Optional_wraps_the_value_when_a_row_exists() {
        ColumnInfo[] cols = [new("V", typeof(string), false)];
        using var reader = Rows.Reader(cols, ["here"]);
        var parser = TypeParser.GetTypeParser<Optional<string>>(ref cols);
        reader.Read();
        string? value = parser.Parse(reader).Result;
        Assert.Equal("here", value);
    }

    [Fact]
    public void OptionalStruct_defaults_to_empty() {
        ColumnInfo[] cols = [new("V", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<OptionalStruct<int>>(ref cols);
        int? value = parser.Default();
        Assert.False(value.HasValue);
    }

    [Fact]
    public void OptionalNullable_wraps_null_values_too() {
        ColumnInfo[] cols = [new("V", typeof(string), true)];
        using var reader = Rows.Reader(cols, [DBNull.Value]);
        var parser = TypeParser.GetTypeParser<OptionalNullable<string>>(ref cols);
        reader.Read();
        string? value = parser.Parse(reader).Result;
        Assert.Null(value);
    }

    [Fact]
    public void MaybeNull_reads_value_and_null() {
        ColumnInfo[] cols = [new("V", typeof(string), true)];
        using var reader = Rows.Reader(cols, ["x"], [DBNull.Value]);
        var parser = TypeParser.GetTypeParser<MaybeNull<string>>(ref cols);
        reader.Read();
        string? first = parser.Parse(reader).Result;
        Assert.Equal("x", first);
        string? second = parser.Parse(reader).Result;
        Assert.Null(second);
    }

    [Fact]
    public void Single_throws_when_a_second_row_exists() {
        ColumnInfo[] cols = [new("V", typeof(string), false)];
        using var reader = Rows.Reader(cols, ["one"], ["two"]);
        var parser = TypeParser.GetTypeParser<Single<string>>(ref cols);
        reader.Read();
        Refusals.Raises(ErrorCodes.ShapeRefusedResult, () => parser.Parse(reader));
    }

    [Fact]
    public void Single_returns_the_value_when_alone() {
        ColumnInfo[] cols = [new("V", typeof(string), false)];
        using var reader = Rows.Reader(cols, ["one"]);
        var parser = TypeParser.GetTypeParser<Single<string>>(ref cols);
        reader.Read();
        string value = parser.Parse(reader).Result;
        Assert.Equal("one", value);
    }

    [Fact]
    public void Schema_extraction_from_a_type_uses_its_longest_constructor() {
        var cols = SchemaExtractor.FromType(typeof(PropUserRequired));
        Assert.Equal(2, cols.Length);
        Assert.Equal("Id", cols[0].Name);
        Assert.Equal(typeof(int), cols[0].Type);
        Assert.Equal("Name", cols[1].Name);
        Assert.False(cols[1].IsNullable);
    }

    [Fact]
    public void Schema_extraction_from_a_constructorless_struct_uses_its_members() {
        var cols = SchemaExtractor.FromType(typeof(BareProbe));
        Assert.Equal(2, cols.Length);
        Assert.Contains(cols, c => c.Name == "Id" && c.Type == typeof(int) && !c.IsNullable);
        Assert.Contains(cols, c => c.Name == "Note" && c.Type == typeof(string) && c.IsNullable);
    }

    [Fact]
    public void Schema_extraction_from_a_constructor_lists_its_parameters() {
        var ctor = typeof(PropUserRequired).GetConstructor([typeof(int), typeof(string)])!;
        var cols = SchemaExtractor.FromConstructor(ctor);
        Assert.Equal(2, cols.Length);
        Assert.Equal("Id", cols[0].Name);
        Assert.False(cols[0].IsNullable);
        Assert.Equal("Name", cols[1].Name);
    }

    [Fact]
    public void Schema_extraction_from_a_method_honors_nullability() {
        var method = typeof(SchemaProbe).GetMethod(nameof(SchemaProbe.Probe))!;
        var cols = SchemaExtractor.FromMethod(method);
        Assert.Equal(2, cols.Length);
        Assert.False(cols[0].IsNullable);
        Assert.True(cols[1].IsNullable);
    }

    [Fact]
    public void Parser_from_a_delegate_uses_its_parameters_as_schema() {
        var parser = TypeParser.GetTypeParser<PropUser>((int Id, string Name) => new PropUser { Id = Id, Name = Name }, out var cols);
        Assert.Equal(2, cols.Length);
        using var reader = Rows.Reader(cols, [3, "made"]);
        reader.Read();
        var user = parser.Parse(reader).Result;
        Assert.Equal(3, user.Id);
        Assert.Equal("made", user.Name);
    }

    [Fact]
    public void Column_schemas_compare_by_content() {
        ColumnInfo[] a = [new("Id", typeof(int), false)];
        ColumnInfo[] b = [new("Id", typeof(int), false)];
        Assert.True(a.EquivalentTo(b));
        Assert.True(a.EquivalentTo([new("ID", typeof(int), false)]));
        Assert.False(a.EquivalentTo([new("Other", typeof(int), false)]));
        Assert.False(a.EquivalentTo([new("Id", typeof(long), false)]));
        Assert.False(a.EquivalentTo([new("Id", typeof(int), false), new("More", typeof(int), false)]));
    }

    [Fact]
    public void Nullable_stored_schema_accepts_a_non_nullable_candidate_but_not_the_reverse() {
        ColumnInfo[] notNullable = [new("Id", typeof(int), false)];
        ColumnInfo[] nullable = [new("Id", typeof(int), true)];
        Assert.True(notNullable.EquivalentTo(nullable));
        Assert.False(nullable.EquivalentTo(notNullable));
    }
}

public static class SchemaProbe {
    public static void Probe(int id, string? note) { }
}
public struct BareProbe {
    public int Id { get; set; }
    public string? Note { get; set; }
}
