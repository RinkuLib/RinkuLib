using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.ConditionalSql.Handlers;

/// <summary>
/// Literal handlers render numbers and strings into the template. The assertions use the intended SQL and
/// result values, so a missing handler case remains visible.
/// </summary>
public class LiteralHandlerTests(SqliteDb Db) : IClassFixture<SqliteDb> {
    enum AnEnum { A = 0, B = 1 }
    enum AnotherEnum : byte { A = 7, B = 8 }

    [Fact]
    public void Enum_decimal_and_second_enum_literals_round_trip_together() {
        var q = new QueryCommand("SELECT @x_N AS X, @y_N AS Y, CAST(@z_N AS INTEGER) AS Z");
        var parameters = new { x = AnEnum.B, y = 123.45M, z = AnotherEnum.A };

        Assert.Equal(
            "SELECT 1 AS X, 123.45 AS Y, CAST(7 AS INTEGER) AS Z",
            Render.From(q, parameters).CommandText);

        using var cnn = Db.GetConnection();
        var row = q.Query<LiteralRow>(cnn, parameters);
        Assert.Equal(AnEnum.B, (AnEnum)row.X);
        Assert.Equal(123.45M, row.Y);
        Assert.Equal(AnotherEnum.A, (AnotherEnum)(byte)row.Z);
    }

    [Fact]
    public void Enum_number_literal_inlines_its_numeric_value() {
        var q = new QueryCommand("SELECT @x_N AS V");
        Assert.Equal("SELECT 1 AS V", Render.From(q, new { x = AnEnum.B }).CommandText);
        using var cnn = Db.GetConnection();
        Assert.Equal(1, q.Query<int>(cnn, new { x = AnEnum.B }));
    }

    [Fact]
    public void Decimal_number_literal_inlines_its_value() {
        var q = new QueryCommand("SELECT @y_N AS V");
        Assert.Equal("SELECT 123.45 AS V", Render.From(q, new { y = 123.45M }).CommandText);
        using var cnn = Db.GetConnection();
        Assert.Equal(123.45M, q.Query<decimal>(cnn, new { y = 123.45M }));
    }

    [Fact]
    public void String_literal_inlines_quoted() {
        var q = new QueryCommand("SELECT @s_S AS V");
        Assert.Equal("SELECT 'Rinku' AS V", Render.From(q, new { s = "Rinku" }).CommandText);
        using var cnn = Db.GetConnection();
        Assert.Equal("Rinku", q.Query<string>(cnn, new { s = "Rinku" }));
    }

    [Fact]
    public void Boolean_number_literal_gates_a_row() {
        var q = new QueryCommand("SELECT COUNT(*) FROM Users WHERE 1 = @val_N");
        using var cnn = Db.GetConnection();
        Assert.Equal(3, q.Query<int>(cnn, new { val = true }));
        Assert.Equal(0, q.Query<int>(cnn, new { val = false }));
    }

    [Fact]
    public void Number_literal_inlined_into_a_predicate_round_trips() {
        var q = new QueryCommand("SELECT COUNT(*) FROM Users WHERE ID = @id_N");
        using var cnn = Db.GetConnection();
        Assert.Equal(1, q.Query<int>(cnn, new { id = 2 }));
    }

    [Fact]
    public void Literal_and_spread_handlers_can_render_one_command_together() {
        var q = new QueryCommand("SELECT COUNT(*) FROM Users WHERE ID IN (?@ids_X) AND 1 = @a_N");
        using var cnn = Db.GetConnection();
        Assert.Equal(2, q.Query<int>(cnn, new { ids = new[] { 1, 2, 4 }, a = 1 }));
    }

    [Fact]
    public void Number_literal_can_be_used_by_each_write_execution() {
        using var cnn = Db.Open();
        var insert = new QueryCommand("INSERT INTO Scratch (Val, Txt) VALUES (@id_N, @txt)");

        foreach (var row in new[] {
            new { id = 1, txt = "first" },
            new { id = 3, txt = "second" },
        })
            Assert.Equal(1, insert.Execute(cnn, row));

        var count = new QueryCommand("SELECT COUNT(*) FROM Scratch WHERE Val = @value_N")
            .Query<int>(cnn, new { value = 3 });
        Assert.Equal(1, count);
    }

    private sealed class LiteralRow {
        public int X { get; set; }
        public decimal Y { get; set; }
        public int Z { get; set; }
    }
}
