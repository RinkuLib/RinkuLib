using RinkuLib.Commands;
using RinkuLib.Queries;
using Xunit;
namespace RinkuLib.Tests.Queries;
/// <summary>
/// Building a <see cref="QueryCommand"/> from plain SQL must leave the SQL unchanged. These cover the
/// shapes that once tripped the parser, most of all BEGIN...END blocks. A shape that throws while
/// building, or comes back altered, is a regression.
/// </summary>
public class QueryParenInvestigationTests {
    private static readonly DummyConnection DummyCnn = new();
    private static void AssertRoundTrips(string sql) {
        var query = new QueryCommand(sql);
        var builder = query.StartBuilder();
        var cmd = DummyCnn.CreateDummyCommand();
        builder.QueryCommand.SetCommand(cmd, builder.Variables);
        Assert.Empty(cmd.ParametersList);
        Assert.Equal(sql, cmd.CommandText);
    }
    [Fact] public void Control_SelectStar() => AssertRoundTrips("SELECT * FROM Posts");
    [Fact] public void Control_CountStar() => AssertRoundTrips("SELECT COUNT(*) FROM Posts");
    [Fact] public void Control_TwoStatements() => AssertRoundTrips("SELECT * FROM Posts WHERE Id = @a; SELECT * FROM Posts WHERE Id = @b");
    [Fact] public void Select_FunctionWithArgs() => AssertRoundTrips("SELECT REPLICATE('x', 2000) AS T FROM Posts");
    [Fact] public void Select_EmptyParenFunction() => AssertRoundTrips("SELECT GETDATE() AS T FROM Posts");
    [Fact] public void Select_NestedParensPredicate() => AssertRoundTrips("SELECT * FROM Posts WHERE (Id = 1 AND (Age > 2))");
    [Fact] public void Select_SubqueryWithParam() => AssertRoundTrips("SELECT * FROM Posts WHERE Id IN (SELECT Id FROM Others WHERE X = @a)");
    [Fact] public void Select_StringLiteralWithParens() => AssertRoundTrips("SELECT '(' + Name + ')' AS T FROM Posts");
    [Fact] public void Insert_LiteralValues() => AssertRoundTrips("INSERT INTO Posts (Id, Text) VALUES (1, 'abc')");
    [Fact] public void Insert_EmptyParenFunction() => AssertRoundTrips("INSERT INTO Posts (CreationDate) VALUES (GETDATE())");
    [Fact] public void Insert_SingleNestedFunction() => AssertRoundTrips("INSERT INTO Posts (Text) VALUES (REPLICATE('x', 2000))");
    [Fact] public void Insert_NestedFunctionsInValues() => AssertRoundTrips("INSERT INTO Posts (Text, CreationDate, LastChangeDate) VALUES (REPLICATE('x', 2000), GETDATE(), GETDATE())");
    [Fact] public void Batch_DeclareAndSet() => AssertRoundTrips("DECLARE @i INT = 0; SET @i = @i + 1;");
    [Fact] public void Batch_BeginEndSetOnly() => AssertRoundTrips("BEGIN SET NOCOUNT ON; END");
    [Fact] public void Batch_BeginEndInsert() => AssertRoundTrips("BEGIN INSERT INTO Posts (Text) VALUES (REPLICATE('x', 10)); END");
    [Fact] public void Batch_WhileSetOnly() => AssertRoundTrips("DECLARE @i INT = 0; WHILE @i < 5 BEGIN SET @i = @i + 1; END");
    [Fact] public void Batch_WhileInsertNoNestedParen() => AssertRoundTrips("DECLARE @i INT = 0; WHILE @i < 5 BEGIN INSERT INTO Posts (Id) VALUES (@i); SET @i = @i + 1; END");
    [Fact] public void Batch_WhileInsert() => AssertRoundTrips("SET NOCOUNT ON; DECLARE @i INT = 0; WHILE @i < 5 BEGIN INSERT INTO Posts (Text) VALUES (REPLICATE('x', 10)); SET @i = @i + 1; END");
}
