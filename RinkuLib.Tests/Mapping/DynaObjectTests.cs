using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>
/// <see cref="DynaObject"/> captures a row without a declared shape: values by name or ordinal,
/// typed access through <c>Get&lt;T&gt;</c>, and generated backing types for any column count.
/// </summary>
public class DynaObjectTests {
    private static readonly ColumnInfo[] EmployeeCols = [
        new("BadgeId", typeof(Guid), false),
        new("Department", typeof(string), false),
        new("Salary", typeof(decimal), true),
        new("JoinedAt", typeof(DateTime), true),
    ];

    [Fact]
    public void Values_read_by_name_with_their_type() {
        var badge = Guid.NewGuid();
        var joined = new DateTime(2023, 5, 10);
        var row = Rows.ParseOne<DynaObject>(EmployeeCols, badge, "Engineering", 95000.50m, joined);
        Assert.Equal(badge, row.Get<Guid>("BadgeId"));
        Assert.Equal("Engineering", row.Get<string>("Department"));
        Assert.Equal(95000.50m, row.Get<decimal>("Salary"));
        Assert.Equal(joined, row.Get<DateTime>("JoinedAt"));
    }

    [Fact]
    public void Null_columns_come_back_null() {
        var badge = Guid.NewGuid();
        var row = Rows.ParseOne<DynaObject>(EmployeeCols, badge, "Engineering", DBNull.Value, DBNull.Value);
        Assert.Equal(badge, row["BadgeId"]);
        Assert.Null(row["Salary"]);
        Assert.Null(row.Get<DateTime?>("JoinedAt"));
    }

    [Fact]
    public void Count_matches_the_column_count() {
        var row = Rows.ParseOne<DynaObject>(EmployeeCols, Guid.NewGuid(), "X", 1m, DateTime.Now);
        Assert.Equal(4, row.Count);
    }

    [Fact]
    public void Duplicate_column_names_get_a_numbered_suffix() {
        var badge = Guid.NewGuid();
        ColumnInfo[] cols = [new("BadgeId", typeof(Guid), false), new("BadgeID", typeof(Guid), true)];
        var row = Rows.ParseOne<DynaObject>(cols, badge, DBNull.Value);
        Assert.Equal(badge, row.Get<Guid>("BadgeId"));
        Assert.Null(row.Get<Guid?>("BadgeID#2"));
    }

    [Fact]
    public void Set_replaces_a_value_in_place() {
        ColumnInfo[] cols = [new("BadgeId", typeof(Guid), false), new("BadgeID", typeof(Guid), true)];
        var row = Rows.ParseOne<DynaObject>(cols, Guid.NewGuid(), DBNull.Value);
        object replacement = Guid.NewGuid();
        row.Set("BadgeID", replacement);
        Assert.Equal(replacement, row[0]);
    }

    [Fact]
    public void Get_converts_via_implicit_operators() {
        ColumnInfo[] cols = [new("Nb1", typeof(int), false), new("Nb2", typeof(long), true)];
        var row = Rows.ParseOne<DynaObject>(cols, 1, 1L);
        Assert.Equal(new WrappedAmount(1), row.Get<WrappedAmount>("Nb1"));
        Assert.Equal(new WrappedAmount(1), row.Get<WrappedAmount>("Nb2"));
    }

    [Fact]
    public void Wide_rows_use_the_generated_fallback_shape() {
        var cols = new ColumnInfo[15];
        var row = new object[15];
        for (int i = 0; i < cols.Length; i++) {
            cols[i] = new($"Col{i + 1}", typeof(int), i is 3 or 4 or 8 or 9 or 13 or 14);
            row[i] = i + 1;
        }
        var dyna = Rows.ParseOne<DynaObject>(cols, row);
        for (int i = 0; i < 13; i++)
            Assert.Equal(i + 1, dyna.Get<int>(i));
        Assert.Equal((sbyte)14, dyna.Get<sbyte>(13));
        Assert.Equal(15L, dyna.Get<long>(14));
    }

    [Fact]
    public void DynaObject_combines_with_a_typed_tuple_item() {
        var badge = Guid.NewGuid();
        ColumnInfo[] cols = [
            new("ID", typeof(int), false),
            new("BadgeId", typeof(Guid), false),
            new("Department", typeof(string), false),
        ];
        var (id, rest) = Rows.ParseOne<(int, DynaObject)>(cols, 1, badge, "Engineering");
        Assert.Equal(1, id);
        Assert.Equal(badge, rest.Get<Guid>("BadgeId"));
        Assert.Equal("Engineering", rest.Get<string>("Department"));
    }

    [Fact]
    public void NoName_member_takes_the_rest_of_the_row() {
        var badge = Guid.NewGuid();
        ColumnInfo[] cols = [
            new("ID", typeof(int), false),
            new("BadgeId", typeof(Guid), false),
            new("Department", typeof(string), false),
        ];
        var pair = Rows.ParseOne<DynaHolder<int>>(cols, 7, badge, "Sales");
        Assert.Equal(7, pair.ID);
        Assert.Equal(badge, pair.Rest.Get<Guid>("BadgeId"));
        Assert.Equal("Sales", pair.Rest.Get<string>("Department"));
    }

    [Fact]
    public void Key_lookup_is_case_insensitive() {
        ColumnInfo[] cols = [new("Department", typeof(string), false)];
        var row = Rows.ParseOne<DynaObject>(cols, "Ops");
        Assert.Equal("Ops", row["department"]);
        Assert.Equal("Ops", row["DEPARTMENT"]);
    }

    [Fact]
    public void Dictionary_interface_enumerates_names_and_values() {
        ColumnInfo[] cols = [new("A", typeof(int), false), new("B", typeof(string), false)];
        var row = Rows.ParseOne<DynaObject>(cols, 1, "two");
        IReadOnlyDictionary<string, object?> dict = row;
        Assert.True(dict.ContainsKey("A"));
        Assert.True(dict.TryGetValue("B", out var b));
        Assert.Equal("two", b);
        Assert.Equal(["A", "B"], dict.Keys);
        Assert.Equal([1, "two"], dict.Values.Cast<object>());
    }

    [Fact]
    public void Json_converter_writes_names_and_values() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        var row = Rows.ParseOne<DynaObject>(cols, 5, "Roy");
        var options = new System.Text.Json.JsonSerializerOptions { Converters = { new DynaObjectConverter() } };
        var json = System.Text.Json.JsonSerializer.Serialize(row, options);
        Assert.Contains("\"Id\":5", json);
        Assert.Contains("\"Name\":\"Roy\"", json);
    }

    [Fact]
    public void Json_converter_does_not_read_back() {
        var options = new System.Text.Json.JsonSerializerOptions { Converters = { new DynaObjectConverter() } };
        Assert.Throws<NotImplementedException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<DynaObject>("{}", options));
    }
}

public record struct DynaHolder<T>([CanNotLookAnywhere] T ID, [NoName] DynaObject Rest);
