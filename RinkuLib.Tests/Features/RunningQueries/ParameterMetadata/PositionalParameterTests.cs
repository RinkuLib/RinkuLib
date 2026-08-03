using System.Data;
using System.Data.Common;
using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Templating;

public class PositionalParameterTests {
    private const string Sql = "SELECT * FROM Users WHERE Id = ? AND Status = ?";

    [Fact]
    public void Manual_slots_keep_positional_sql_and_external_parameter_setup_controls_the_parameters() {
        var query = new QueryCommand(Sql, ["0", "1"], CommandType.Text);
        Assert.True(query.UpdateParamCache("@0", new PositionalParamInfo()));
        Assert.True(query.UpdateParamCache("@1", new PositionalParamInfo()));

        var builder = query.StartBuilder();
        builder.Use("@0", 7);
        builder.Use("@1", "active");

        var command = Render.From(builder);

        Assert.Equal(Sql, command.CommandText);
        Assert.Equal(["?", "?"], command.BoundParameters.Select(parameter => parameter.ParameterName));
        Assert.Equal([7, "active"], command.BoundParameters.Select(parameter => parameter.Value));
    }

    [Fact]
    public void The_same_external_setup_works_when_reusing_a_bound_builder() {
        var query = new QueryCommand(Sql, ["0", "1"], CommandType.Text);
        Assert.True(query.UpdateParamCache("@0", new PositionalParamInfo()));
        Assert.True(query.UpdateParamCache("@1", new PositionalParamInfo()));
        var command = new FakeCommand { Connection = new FakeConnection() };
        var builder = query.StartBuilder((DbCommand)command);

        builder.Use("@0", 9);
        builder.Use("@1", "queued");
        builder.Execute();

        Assert.Equal(Sql, command.CommandText);
        Assert.Equal(["?", "?"], command.BoundParameters.Select(parameter => parameter.ParameterName));
        Assert.Equal([9, "queued"], command.BoundParameters.Select(parameter => parameter.Value));
    }
}

sealed class PositionalParamInfo : DbParamInfo {
    public PositionalParamInfo() : base(true) { }

    public override bool SaveUse(string paramName, IDbCommand cmd, ref object value) {
        value = Add(cmd, value);
        return true;
    }

    public override bool Use(string paramName, IDbCommand cmd, object value) {
        Add(cmd, value);
        return true;
    }

    public override bool Use(string paramName, DbCommand cmd, object value) {
        Add(cmd, value);
        return true;
    }

    public override bool Update(IDbCommand cmd, ref object? currentValue, object? newValue) {
        if (currentValue is not IDbDataParameter parameter)
            return false;
        if (newValue is null) {
            cmd.Parameters.Remove(currentValue);
            currentValue = null;
            return true;
        }
        parameter.Value = newValue;
        return true;
    }

    public override void Remove(IDbCommand cmd, object currentValue)
        => cmd.Parameters.Remove(currentValue);

    private static IDbDataParameter Add(IDbCommand cmd, object value) {
        var parameter = (IDbDataParameter)cmd.CreateParameter();
        parameter.ParameterName = "?";
        parameter.Value = value;
        cmd.Parameters.Add(parameter);
        return parameter;
    }
}
