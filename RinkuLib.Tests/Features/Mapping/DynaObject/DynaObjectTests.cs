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
    public void Every_accessor_form_reads_and_writes_the_same_slot() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        var row = Rows.ParseOne<DynaObject>(cols, 5, "first");

        Assert.Equal(5, row.Get<int>(0));
        Assert.Equal(5, row.Get<int>("Id"));
        Assert.Equal(5, row.Get<int>("Id".AsSpan()));
        Assert.Equal(5, row[0]);
        Assert.Equal(5, row["Id"]);
        Assert.Equal(5, row["Id".AsSpan()]);

        row[0] = 6;
        Assert.Equal(6, row[0]);
        row["Id"] = 7;
        Assert.Equal(7, row["Id"]);
        row["Id".AsSpan()] = 8;
        Assert.Equal(8, row["Id".AsSpan()]);
        Assert.True(row.Set("Id".AsSpan(), 9));
        Assert.Equal(9, row.Get<int>("Id".AsSpan()));
        Assert.False(row.Set("Nope", 1));
        Assert.False(row.Set("Nope".AsSpan(), 1));

        Assert.True(row.TryGetValue("Name", out string? name));
        Assert.Equal("first", name);
        Assert.True(row.TryGetValue("Name".AsSpan(), out object? boxedName));
        Assert.Equal("first", boxedName);
        Assert.True(row.TryGetValue("Name".AsSpan(), out string? spanName));
        Assert.Equal("first", spanName);
        Assert.False(row.TryGetValue("Nope", out string? _));
        Assert.False(row.TryGetValue("Nope".AsSpan(), out object? _));
        Assert.False(row.TryGetValue("Nope".AsSpan(), out string? _));
        Assert.True(row.TryGetValue("Name", out object? boxedByName));
        Assert.Equal("first", boxedByName);
        Assert.False(row.TryGetValue("Nope", out object? _));

        Assert.True(row.ContainsKey("Name"));
        Assert.True(row.ContainsKey("Name".AsSpan()));
        Assert.False(row.ContainsKey("Nope"));
        Assert.Equal(["Id", "Name"], row.Keys.ToArray());
        Assert.Equal([9, "first"], row.Values);
    }

    [Fact]
    public void A_backing_shape_refuses_a_mapper_of_the_wrong_width() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        var row = Rows.ParseOne<DynaObject>(cols, 5, "first");
        Refusals.Raises(ErrorCodes.InternalInvariant, () => new DynaObject<int>(5, row.Mapper));
    }

    [Fact]
    public void Misses_throw_with_the_key_or_index() {
        ColumnInfo[] cols = [new("Id", typeof(int), false)];
        var row = Rows.ParseOne<DynaObject>(cols, 5);
        Assert.Throws<KeyNotFoundException>(() => row["Nope"]);
        Assert.Throws<KeyNotFoundException>(() => row["Nope".AsSpan()]);
        Assert.Throws<KeyNotFoundException>(() => row["Nope"] = 1);
        Assert.Throws<KeyNotFoundException>(() => { var span = "Nope".AsSpan(); row[span] = 1; });
        Assert.Throws<KeyNotFoundException>(() => row.Get<int>("Nope"));
        Assert.Throws<KeyNotFoundException>(() => row.Get<int>("Nope".AsSpan()));
        Assert.Throws<IndexOutOfRangeException>(() => row[5]);
        Assert.Throws<IndexOutOfRangeException>(() => row[-1] = 1);
        Refusals.Raises(ErrorCodes.CannotReadColumn, () => row.Get<Version>(0));
        Refusals.Raises(ErrorCodes.CannotReadColumn, () => row.Get<Version>("Id"));
        Refusals.Raises(ErrorCodes.CannotReadColumn, () => { var span = "Id".AsSpan(); row.Get<Version>(span); });
    }

    [Fact]
    public void Both_dictionary_views_enumerate_the_row() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        var row = Rows.ParseOne<DynaObject>(cols, 5, "first");

        var asValues = (IReadOnlyDictionary<string, object?>)row;
        Assert.Equal(["Id", "Name"], asValues.Keys);
        Assert.Equal([5, "first"], asValues.Values);
        Assert.Equal([new("Id", 5), new KeyValuePair<string, object?>("Name", "first")], asValues.ToList());

        var asIndexes = (IReadOnlyDictionary<string, int>)row;
        Assert.Equal(1, asIndexes["Name"]);
        Assert.Equal(["Id", "Name"], asIndexes.Keys);
        Assert.Equal([0, 1], asIndexes.Values);
        Assert.Equal([new("Id", 0), new KeyValuePair<string, int>("Name", 1)], asIndexes.ToList());
        Assert.True(row.TryGetValue("Name", out int index));
        Assert.Equal(1, index);
        Assert.False(row.TryGetValue("Nope", out int _));

        var untyped = ((System.Collections.IEnumerable)row).GetEnumerator();
        Assert.True(untyped.MoveNext());
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
        Refusals.Raises(ErrorCodes.OperationNotSupportedForType,
            () => System.Text.Json.JsonSerializer.Deserialize<DynaObject>("{}", options));
    }

    /// <summary>
    /// The sibling slot is free to match its column wherever it sits, so a <see cref="DynaObject"/> beside it
    /// takes the columns from both sides of it and no longer spans one unbroken run.
    /// </summary>
    [Fact]
    public void A_split_run_still_fills_the_dyna_object() {
        ColumnInfo[] cols = [
            new("BadgeId", typeof(Guid), false),
            new("Department", typeof(string), false),
            new("IdAnywhere", typeof(int), false),
            new("JoinedAt", typeof(DateTime), true),
        ];
        var badge = Guid.NewGuid();
        var joined = new DateTime(2023, 5, 10);

        var (id, rest) = Rows.ParseOne<DynaSplit<int>>(cols, badge, "Engineering", 7, joined);

        Assert.Equal(7, id);
        Assert.Equal(badge, rest.Get<Guid>("BadgeId"));
        Assert.Equal("Engineering", rest.Get<string>("Department"));
        Assert.Equal(joined, rest.Get<DateTime>("JoinedAt"));
    }
}

public record struct DynaHolder<T>([CanNotLookAnywhere] T ID, [NoName] DynaObject Rest);
public record struct DynaSplit<T>(T IdAnywhere, [NoName] DynaObject Rest);
