using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
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
}

public record class SplitStop(int ID, string Name, string? Other = null);
public record class SplitStopFreeOther(int ID, string Name, [CanLookAnywhere] string? Other = null);
public record class SplitStopFreeId([CanLookAnywhere] int ID, string Name, string? Other = null);
