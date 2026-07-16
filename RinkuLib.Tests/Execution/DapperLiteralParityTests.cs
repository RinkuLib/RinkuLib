using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// Rinku equivalents of Dapper's <c>LiteralTests</c> (the <c>{=x}</c> literal replacement), in Rinku's
/// <c>_N</c>/<c>_S</c> syntax. Expected values follow the intended inlining (enum to its number, decimal to
/// its value, string quoted) rather than the current output, so a missing case shows up as a failure.
/// </summary>
public class DapperLiteralParityTests(SqliteDb Db) : IClassFixture<SqliteDb> {
    enum AnEnum { A = 0, B = 1 }

    [Fact]
    public void Enum_number_literal_inlines_its_numeric_value() {
        var q = new QueryCommand("SELECT @x_N AS V");
        Assert.Equal("SELECT 1 AS V", Render.From(q, new { x = AnEnum.B }).CommandText);
        using var cnn = Db.GetConnection();
        Assert.Equal(1, q.Query<int>(cnn, new { x = AnEnum.B }));
    }

    // GAP: currently FAILS. NumberVariableHandler.Handle does (int)value, so a decimal literal throws
    // InvalidCastException. Dapper's {=y} inlines any numeric literal; _N should handle decimal too.
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

    // GAP: currently FAILS. NumberVariableHandler.Handle does (int)value, so a bool literal throws
    // InvalidCastException. Dapper's LiteralReplacementBoolean inlines a bool; _N should handle it.
    [Fact]
    public void Boolean_number_literal_gates_a_row() {
        // Dapper's LiteralReplacementBoolean: "select 42 where 1 = {=val}" returns the row only when true.
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
    public void Literal_and_spread_combine_like_dapper_LiteralReplacementWithIn() {
        var q = new QueryCommand("SELECT COUNT(*) FROM Users WHERE ID IN (?@ids_X) AND 1 = @a_N");
        using var cnn = Db.GetConnection();
        Assert.Equal(2, q.Query<int>(cnn, new { ids = new[] { 1, 2, 4 }, a = 1 }));
    }
}
