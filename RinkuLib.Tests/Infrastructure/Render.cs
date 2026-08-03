using System.Data;
using System.Data.Common;
using RinkuLib.Commands;
using RinkuLib.Queries;
using Xunit;

namespace RinkuLib.Tests.Infrastructure;

/// <summary>
/// Renders a command through the fake ADO.NET objects and asserts on the SQL text and the
/// parameters that were bound, without touching a database.
/// </summary>
public static class Render {
    public static FakeCommand From(QueryBuilder builder) {
        var cmd = new FakeCommand();
        builder.QueryCommand.SetCommand((DbCommand)cmd, builder.Variables);
        return cmd;
    }
    public static FakeCommand FromInterface(QueryBuilder builder) {
        var cmd = new FakeCommand();
        builder.QueryCommand.SetCommand((IDbCommand)cmd, builder.Variables);
        return cmd;
    }
    public static FakeCommand From(QueryCommand query, object? parameterObj) {
        var cmd = new FakeCommand();
        var usageMap = new bool[query.Mapper.Count];
        query.SetCommand((DbCommand)cmd, parameterObj, usageMap);
        return cmd;
    }
    public static FakeCommand From<T>(QueryCommand query, T parameterObj) where T : notnull {
        var cmd = new FakeCommand();
        var usageMap = new bool[query.Mapper.Count];
        query.SetCommand((DbCommand)cmd, parameterObj, usageMap);
        return cmd;
    }

    public static void Expect(QueryBuilder builder, string expectedSql, params (string Name, object? Value)[] expectedParams)
        => AssertCommand(From(builder), expectedSql, expectedParams);
    public static void Expect(QueryCommand query, object? parameterObj, string expectedSql, params (string Name, object? Value)[] expectedParams)
        => AssertCommand(From(query, parameterObj), expectedSql, expectedParams);
    public static void Expect<T>(QueryCommand query, T parameterObj, string expectedSql, params (string Name, object? Value)[] expectedParams) where T : notnull
        => AssertCommand(From(query, parameterObj), expectedSql, expectedParams);

    public static void AssertCommand(FakeCommand cmd, string expectedSql, params (string Name, object? Value)[] expectedParams) {
        Assert.Equal(expectedSql, cmd.CommandText);
        var bound = cmd.BoundParameters;
        Assert.Equal(expectedParams.Length, bound.Count);
        for (int i = 0; i < expectedParams.Length; i++) {
            Assert.Equal(expectedParams[i].Name, bound[i].ParameterName);
            Assert.Equal(expectedParams[i].Value, bound[i].Value);
        }
    }
}
