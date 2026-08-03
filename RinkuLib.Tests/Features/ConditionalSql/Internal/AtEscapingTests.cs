using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Templating;

public class AtEscapingTests {
    [Fact]
    public void Double_at_keeps_a_sql_server_system_variable_as_text() {
        var query = new QueryCommand("SELECT @@Name");
        var cmd = Render.From(query, null);

        Assert.Equal("SELECT @@Name", cmd.CommandText);
        Assert.Empty(cmd.BoundParameters);
    }
}
