using Rinku.Mapping;
using RinkuLib.Tests.Infrastructure;
using Rinku.Internal;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>
/// A value tuple splits the row left to right, each item consuming the columns its own shape claims.
/// </summary>
public class TupleMappingTests {
    [Fact]
    public void Named_tuple_items_map_by_name() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        var (id, name) = Rows.ParseOne<(int ID, string Name)>(cols, 1, "John Doe");
        Assert.Equal(1, id);
        Assert.Equal("John Doe", name);
    }

    [Fact]
    public void Tuple_items_take_columns_by_position_when_the_column_names_disagree() {
        ColumnInfo[] cols = [new("Item2", typeof(int), false), new("Item1", typeof(string), false)];

        var value = Rows.ParseOne<(int Id, string Name)>(cols, 42, "Fred");

        Assert.Equal((42, "Fred"), value);
    }

    [Fact]
    public void Duplicate_column_names_assign_in_order() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("ID", typeof(int), false)];
        var (first, second) = Rows.ParseOne<(int ID, int ID2)>(cols, 1, 2);
        Assert.Equal(1, first);
        Assert.Equal(2, second);
    }

    [Fact]
    public void Two_objects_share_the_row_left_to_right() {
        ColumnInfo[] cols = [
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
            new("ID", typeof(int), false),
            new("name", typeof(string), false),
            new("Other", typeof(string), true),
        ];
        var (left, right) = Rows.ParseOne<(SplitStop, SplitStop)>(cols, 1, "Test1", 2, "Test2", "Stop2");
        Assert.Equal(1, left.ID);
        Assert.Equal("Test1", left.Name);
        Assert.Null(left.Other);
        Assert.Equal(2, right.ID);
        Assert.Equal("Test2", right.Name);
        Assert.Equal("Stop2", right.Other);
    }

    [Fact]
    public void CanLookAnywhere_lets_an_item_reach_a_later_column() {
        ColumnInfo[] cols = [
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
            new("ID", typeof(int), false),
            new("name", typeof(string), false),
            new("Other", typeof(string), true),
        ];
        var (left, right) = Rows.ParseOne<(SplitStopFreeOther, SplitStopFreeId)>(cols, 1, "Test1", 2, "Test2", "Stop1");
        Assert.Equal(1, left.ID);
        Assert.Equal("Test1", left.Name);
        Assert.Equal("Stop1", left.Other);
        Assert.Equal(2, right.ID);
        Assert.Equal("Test2", right.Name);
        Assert.Null(right.Other);
    }

    [Fact]
    public void Scalar_and_object_combine_in_one_tuple() {
        ColumnInfo[] cols = [
            new("Total", typeof(int), false),
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
        ];
        var (total, user) = Rows.ParseOne<(int, PropUser)>(cols, 9, 1, "Ann");
        Assert.Equal(9, total);
        Assert.Equal(1, user.Id);
        Assert.Equal("Ann", user.Name);
    }

    [Fact]
    public void Eight_tuple_items_map_in_order() {
        ColumnInfo[] cols = [
            new("A", typeof(int), false), new("B", typeof(int), false),
            new("C", typeof(int), false), new("D", typeof(int), false),
            new("E", typeof(int), false), new("F", typeof(int), false),
            new("G", typeof(int), false), new("H", typeof(int), false),
        ];
        var value = Rows.ParseOne<(int A, int B, int C, int D, int E, int F, int G, int H)>(
            cols, 1, 2, 3, 4, 5, 6, 7, 8);
        Assert.Equal((1, 2, 3, 4, 5, 6, 7, 8), value);
    }

    [Fact]
    public void Fifteen_tuple_items_map_through_nested_value_tuple_storage() {
        ColumnInfo[] cols = [
            new("A", typeof(int), false), new("B", typeof(int), false),
            new("C", typeof(int), false), new("D", typeof(int), false),
            new("E", typeof(int), false), new("F", typeof(int), false),
            new("G", typeof(int), false), new("H", typeof(int), false),
            new("I", typeof(int), false), new("J", typeof(int), false),
            new("K", typeof(int), false), new("L", typeof(int), false),
            new("M", typeof(int), false), new("N", typeof(int), false),
            new("O", typeof(int), false),
        ];
        var value = Rows.ParseOne<(
            int A, int B, int C, int D, int E, int F, int G, int H,
            int I, int J, int K, int L, int M, int N, int O)>(
            cols, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);

        Assert.Equal(1, value.A);
        Assert.Equal(8, value.H);
        Assert.Equal(15, value.O);
    }

    [Fact]
    public void Nullable_tuple_uses_abort_on_null_for_a_null_first_item() {
        ColumnInfo[] cols = [new("A", typeof(int), true), new("B", typeof(string), true)];
        Assert.Throws<NullValueAssignmentException>(() =>
            Rows.ParseOne<(int A, string B)?>(cols, DBNull.Value, DBNull.Value));
    }

    [Fact]
    public void Extra_columns_are_ignored_by_a_tuple() {
        ColumnInfo[] cols = [
            new("A", typeof(int), false), new("B", typeof(string), false),
            new("Extra", typeof(int), false),
        ];
        var value = Rows.ParseOne<(int A, string B)>(cols, 42, "Fred", 123);
        Assert.Equal((42, "Fred"), value);
    }

    [Fact]
    public void Missing_later_tuple_columns_refuse_the_tuple_shape() {
        ColumnInfo[] cols = [new("A", typeof(int), false), new("B", typeof(string), false)];

        Assert.Throws<RinkuNoParserException>(() =>
            Rows.ParseOne<(int A, string B, int C)>(cols, 42, "Fred"));
    }
}

public record class SplitStop(int ID, string Name, string? Other = null);
public record class SplitStopFreeOther(int ID, string Name, [CanLookAnywhere] string? Other = null);
public record class SplitStopFreeId([CanLookAnywhere] int ID, string Name, string? Other = null);
