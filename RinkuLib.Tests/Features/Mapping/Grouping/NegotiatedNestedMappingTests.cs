using System.Data;
using Rinku.Mapping;
using RinkuLib.Tests.Infrastructure;
using Rinku.Internal;
using Xunit;

namespace RinkuLib.Tests.Mapping;

[Collection("GlobalMappingConfiguration")]
public class NegotiatedNestedMappingTests {
    [Fact]
    public void Registered_nested_types_negotiate_their_columns_without_a_split_marker() {
        _ = TypeParsingInfo.GetOrAdd<RegisteredChild>();
        var row = Rows.ParseOne<RegisteredParent>([
            new("Id", typeof(int), false), new("ChildId", typeof(int), false), new("ChildName", typeof(string), false)], 10, 20, "child");
        Assert.Equal((10, 20, "child"), (row.Id, row.Child.Id, row.Child.Name));
    }

    [Fact]
    public void Negotiation_handles_multiple_registered_child_boundaries() {
        _ = TypeParsingInfo.GetOrAdd<RegisteredChild>();
        var pair = Rows.ParseOne<RegisteredPairParent>([
            new("Id", typeof(int), false), new("FirstId", typeof(int), false), new("FirstName", typeof(string), false), new("SecondId", typeof(int), false), new("SecondName", typeof(string), false)], 10, 20, "first", 30, "second");
        Assert.Equal((20, "first", 30, "second"), (pair.First.Id, pair.First.Name, pair.Second.Id, pair.Second.Name));
    }

    [Fact]
    public void Negotiation_can_collapse_a_registered_child_without_split_configuration() {
        _ = TypeParsingInfo.GetOrAdd<NullableRegisteredChild>();
        var empty = Rows.ParseOne<NullableRegisteredParent>([
            new("Id", typeof(int), false), new("ChildId", typeof(int), true), new("ChildName", typeof(string), true)], 10, DBNull.Value, DBNull.Value);
        Assert.Null(empty.Child);
    }

    [Fact]
    public void Negotiation_scales_to_the_nested_members_the_shape_declares() {
        _ = TypeParsingInfo.GetOrAdd<RegisteredChild>();
        var row = Rows.ParseOne<RegisteredNineParent>([
            new("Id", typeof(int), false),
            new("User1Id", typeof(int), false), new("User1Name", typeof(string), false),
            new("User2Id", typeof(int), false), new("User2Name", typeof(string), false),
            new("User3Id", typeof(int), false), new("User3Name", typeof(string), false),
            new("User4Id", typeof(int), false), new("User4Name", typeof(string), false),
            new("User5Id", typeof(int), false), new("User5Name", typeof(string), false),
            new("User6Id", typeof(int), false), new("User6Name", typeof(string), false),
            new("User7Id", typeof(int), false), new("User7Name", typeof(string), false),
            new("User8Id", typeof(int), false), new("User8Name", typeof(string), false),
            new("User9Id", typeof(int), false), new("User9Name", typeof(string), false)],
            1, 1, "User 1", 2, "User 2", 3, "User 3", 4, "User 4", 5, "User 5", 6, "User 6", 7, "User 7", 8, "User 8", 9, "User 9");
        Assert.Equal((1, "User 1", 9, "User 9"), (row.User1.Id, row.User1.Name, row.User9.Id, row.User9.Name));
    }

    [Fact]
    public void Grouped_nested_collections_associate_children_from_repeated_rows() {
        ColumnInfo[] columns = [new("Id", typeof(int), false), new("Name", typeof(string), false), new("ChildrenId", typeof(int), true), new("ChildrenName", typeof(string), true)];
        var parser = TypeParser.GetTypeParser<List<AssociatedParent>>(columns);
        using var reader = Rows.Reader(columns, [1, "P1", 10, "c10"], [1, "P1", 11, "c11"], [2, "P2", 20, "c20"]);
        reader.Read();
        var grouped = parser.Parse(reader).Result;
        Assert.Equal([new AssociatedChild(10, "c10"), new AssociatedChild(11, "c11")], grouped[0].Children);
        Assert.Equal([new AssociatedChild(20, "c20")], grouped[1].Children);
    }

    [Fact]
    public void Tuples_allow_manual_noncontiguous_association_when_that_is_the_intended_shape() {
        var rows = Rows.ParseAll<(int Id, TupleChild Child)>([
            new("Id", typeof(int), false), new("ChildId", typeof(int), false), new("ChildName", typeof(string), false)], [1, 10, "c10"], [2, 20, "c20"], [1, 11, "c11"]);
        var parents = new Dictionary<int, RegisteredParent>();
        foreach (var row in rows)
            parents[row.Id] = new RegisteredParent(row.Id, new RegisteredChild { Id = row.Child.Id, Name = row.Child.Name });
        Assert.Equal([1, 2], parents.Keys.OrderBy(id => id));
        Assert.Equal((11, "c11"), (parents[1].Child.Id, parents[1].Child.Name));
        Assert.Equal((20, "c20"), (parents[2].Child.Id, parents[2].Child.Name));
    }
}

public class RegisteredChild { public int Id { get; set; } public string Name { get; set; } = null!; }
public record RegisteredParent(int Id, RegisteredChild Child) : IDbReadable;
public record RegisteredPairParent(int Id, RegisteredChild First, RegisteredChild Second) : IDbReadable;
public record RegisteredNineParent(int Id, RegisteredChild User1, RegisteredChild User2, RegisteredChild User3, RegisteredChild User4, RegisteredChild User5, RegisteredChild User6, RegisteredChild User7, RegisteredChild User8, RegisteredChild User9) : IDbReadable;
public record struct NullableRegisteredChild([AbortOnNull] int Id, string? Name);
public record NullableRegisteredParent(int Id, NullableRegisteredChild? Child) : IDbReadable;
public sealed record AssociatedChild([AbortOnNull] int Id, string Name) : IDbReadable;
public sealed record AssociatedParent([property: GroupKey] int Id, string Name, List<AssociatedChild> Children) : IDbReadable;
public sealed record TupleChild([Alt("ChildId")] int Id, [Alt("ChildName")] string Name) : IDbReadable;
