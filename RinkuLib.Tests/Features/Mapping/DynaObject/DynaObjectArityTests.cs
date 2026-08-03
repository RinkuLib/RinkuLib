using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>
/// Each column count is backed by its own generated shape; this sweep exercises get, set, typed
/// access, and JSON writing across the whole range, including the fallback shape past the widest
/// generated one.
/// </summary>
public class DynaObjectArityTests {
    private static (ColumnInfo[] Cols, object[] Row) Make(int arity) {
        var cols = new ColumnInfo[arity];
        var row = new object[arity];
        for (int i = 0; i < arity; i++) {
            cols[i] = new($"Col{i + 1}", typeof(int), i % 2 == 1);
            row[i] = (i + 1) * 10;
        }
        return (cols, row);
    }

    public static TheoryData<int> Arities => [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];

    [Theory]
    [MemberData(nameof(Arities))]
    public void Values_read_back_by_index_and_name(int arity) {
        var (cols, row) = Make(arity);
        var dyna = Rows.ParseOne<DynaObject>(cols, row);
        Assert.Equal(arity, dyna.Count);
        for (int i = 0; i < arity; i++) {
            Assert.Equal((i + 1) * 10, dyna.Get<int>(i));
            Assert.Equal((i + 1) * 10, dyna.Get<int>($"Col{i + 1}"));
        }
    }

    [Theory]
    [MemberData(nameof(Arities))]
    public void Every_index_can_be_set(int arity) {
        var (cols, row) = Make(arity);
        var dyna = Rows.ParseOne<DynaObject>(cols, row);
        for (int i = 0; i < arity; i++)
            Assert.True(dyna.Set(i, 1000 + i));
        for (int i = 0; i < arity; i++)
            Assert.Equal(1000 + i, dyna.Get<int>(i));
    }

    [Theory]
    [MemberData(nameof(Arities))]
    public void Json_writes_every_column(int arity) {
        var (cols, row) = Make(arity);
        var dyna = Rows.ParseOne<DynaObject>(cols, row);
        var options = new System.Text.Json.JsonSerializerOptions { Converters = { new DynaObjectConverter() } };
        var json = System.Text.Json.JsonSerializer.Serialize(dyna, options);
        for (int i = 0; i < arity; i++)
            Assert.Contains($"\"Col{i + 1}\":{(i + 1) * 10}", json);
    }

    [Theory]
    [MemberData(nameof(Arities))]
    public void Nullable_columns_read_null(int arity) {
        var (cols, row) = Make(arity);
        for (int i = 1; i < arity; i += 2)
            row[i] = DBNull.Value;
        var dyna = Rows.ParseOne<DynaObject>(cols, row);
        for (int i = 0; i < arity; i++) {
            if (i % 2 == 1)
                Assert.Null(dyna[i]);
            else
                Assert.Equal((i + 1) * 10, dyna.Get<int>(i));
        }
    }

    [Theory]
    [MemberData(nameof(Arities))]
    public void Typed_get_converts_each_slot(int arity) {
        var (cols, row) = Make(arity);
        var dyna = Rows.ParseOne<DynaObject>(cols, row);
        for (int i = 0; i < arity; i++)
            Assert.Equal((long)((i + 1) * 10), dyna.Get<long>(i));
    }

    [Fact]
    public void Typed_get_into_a_nullable_target_keeps_the_value() {
        var (cols, row) = Make(4);
        var dyna = Rows.ParseOne<DynaObject>(cols, row);
        Assert.Equal(10, dyna.Get<int?>(0));
    }

    [Theory]
    [MemberData(nameof(Arities))]
    public void A_mismatched_type_is_refused_on_every_slot(int arity) {
        var (cols, row) = Make(arity);
        var dyna = Rows.ParseOne<DynaObject>(cols, row);
        for (int i = 0; i < arity; i++) {
            Assert.False(dyna.TryGet<Version>(i, out _));
            if (i < 12)
                Assert.False(dyna.Set(i, new Version(1, 0)));
            else {
                Assert.True(dyna.Set(i, new Version(1, 0)));
                Assert.Equal(new Version(1, 0), dyna.Get<Version>(i));
            }
        }
        Assert.False(dyna.TryGet<int>(arity, out _));
        Assert.False(dyna.Set(arity, 1));
        Assert.False(dyna.TryGet<int>(-1, out _));
        Assert.False(dyna.Set(-1, 1));
    }
}
