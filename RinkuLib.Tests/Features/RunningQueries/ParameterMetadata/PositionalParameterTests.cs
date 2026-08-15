using System.Data;
using System.Data.Common;
using Rinku;
using Rinku.Querying;
using Rinku.Querying.Defaults;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tests.Documentation;
using Xunit;

namespace RinkuLib.Tests.Templating;

/// <summary>Includes the positional example from docs/articles/customization/parameters.md.</summary>
public class PositionalParameterTests {
    private const string Sql = "SELECT * FROM Users WHERE Id = ? AND Status = ?";

    [Fact]
    [DocumentationExample("parameters.md", "positional-parameter")]
    public void Manual_slots_keep_positional_sql_and_external_parameter_setup_controls_the_parameters() {
        var query = new QueryCommand(Sql, ["param0", "param1"], CommandType.Text);
        Assert.True(query.UpdateParamCache(0, new PositionalDbParamInfo()));
        Assert.True(query.UpdateParamCache(1, new PositionalDbParamInfo()));

        var builder = query.StartBuilder();
        builder.Use(0, 7);
        builder.Use(1, "active");

        var command = Render.From(builder);

        Assert.Equal(Sql, command.CommandText);
        Assert.Equal(["?", "?"], command.BoundParameters.Select(parameter => parameter.ParameterName));
        Assert.Equal([7, "active"], command.BoundParameters.Select(parameter => parameter.Value));
    }

    [Fact]
    public void The_same_external_setup_works_when_reusing_a_bound_builder() {
        var query = new QueryCommand(Sql, ["param0", "param1"], CommandType.Text);
        Assert.True(query.UpdateParamCache(0, new PositionalDbParamInfo()));
        Assert.True(query.UpdateParamCache(1, new PositionalDbParamInfo()));
        var command = new FakeCommand { Connection = new FakeConnection() };
        var builder = query.StartBuilder((DbCommand)command);

        builder.Use(0, 9);
        builder.Use(1, "queued");
        builder.Execute();

        Assert.Equal(Sql, command.CommandText);
        Assert.Equal(["?", "?"], command.BoundParameters.Select(parameter => parameter.ParameterName));
        Assert.Equal([9, "queued"], command.BoundParameters.Select(parameter => parameter.Value));
    }
}
