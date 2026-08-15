using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Rinku.Internal;
using Rinku.Querying.Defaults;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tests.Documentation;
using Xunit;

namespace RinkuLib.Tests.Templating;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlobalConditionalHandlersCollection {
    public const string Name = "Global conditional handlers";
}

/// <summary>Executable examples for docs/articles/customization/conditional-sql.md.</summary>
[Collection(GlobalConditionalHandlersCollection.Name)]
public class AdvancedHandlerDocumentationTests {
    private sealed class DateHandler : IQuerySegmentHandler {
        public void Handle(ref ValueStringBuilder sql, object value) {
            var date = (DateOnly)value;
            sql.Append("DATE '");
            sql.Append(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            sql.Append('\'');
        }
    }

    private sealed class Utf8Handler : SpecialHandler {
        private readonly string name;
        private readonly DbParamInfo parameter = TypedDbParamCache.Get(DbType.Binary);

        public Utf8Handler(string name) {
            this.name = name;
            IsCached = true;
        }

        public override bool CanHandle(ref object? value) => value is string;

        public override bool Use(IDbCommand command, ref object? value) {
            if (value is not string text)
                return false;
            return parameter.Use(name, command, Encoding.UTF8.GetBytes(text));
        }

        public override bool Use(DbCommand command, ref object? value) {
            if (value is not string text)
                return false;
            return parameter.Use(name, command, Encoding.UTF8.GetBytes(text));
        }

        public override bool SaveUse(IDbCommand command, ref object? value) {
            if (value is not string text)
                return false;
            object bytes = Encoding.UTF8.GetBytes(text);
            if (!parameter.SaveUse(name, command, ref bytes))
                return false;
            value = bytes;
            return true;
        }

        public override bool Update(IDbCommand command, ref object? current, object? value) {
            if (value is not null and not string)
                return false;
            object? bytes = value is string text ? Encoding.UTF8.GetBytes(text) : null;
            return parameter.Update(command, ref current, bytes);
        }

        public override void Handle(ref ValueStringBuilder sql, object value) => sql.Append(name);

        public override bool UpdateCache<T>(T getter) => true;
    }

    [Fact]
    [DocumentationExample("conditional-sql.md", "sql-handler")]
    public void Base_handler_writes_the_documented_date_literal() {
        QueryFactory.BaseHandlerMapper['D'] = _ => new DateHandler();
        try {
            var report = new QueryCommand("SELECT * FROM audit WHERE CreatedAt >= @When_D");
            var run = report.StartBuilder();
            Assert.True(run.Use("@When", new DateOnly(2026, 1, 1)));

            Render.Expect(run, "SELECT * FROM audit WHERE CreatedAt >= DATE '2026-01-01'");
        }
        finally {
            QueryFactory.BaseHandlerMapper.Remove('D');
        }
    }

    [Fact]
    [DocumentationExample("conditional-sql.md", "binding-handler")]
    public void Special_handler_writes_its_marker_and_binds_utf8_bytes() {
        SpecialHandler.SpecialHandlerGetter['B'] = name => new Utf8Handler(name);
        try {
            var save = new QueryCommand("INSERT INTO binary_values(Value) VALUES (@Value_B)");
            var command = Render.From(save, new { Value = "plain text" });

            Assert.Equal("INSERT INTO binary_values(Value) VALUES (@Value)", command.CommandText);
            var parameter = Assert.Single(command.BoundParameters);
            Assert.Equal("@Value", parameter.ParameterName);
            Assert.Equal(Encoding.UTF8.GetBytes("plain text"), Assert.IsType<byte[]>(parameter.Value));
        }
        finally {
            SpecialHandler.SpecialHandlerGetter.Remove('B');
        }
    }

    [Fact]
    public void Special_handler_supports_both_command_roads_and_bound_reuse() {
        var handler = new Utf8Handler("@Value");

        object? first = "first";
        IDbCommand interfaceCommand = new FakeCommand();
        Assert.True(handler.Use(interfaceCommand, ref first));
        Assert.Equal(Encoding.UTF8.GetBytes("first"),
            Assert.IsType<byte[]>(((FakeCommand)interfaceCommand).BoundParameters[0].Value));

        var liveCommand = new FakeCommand();
        object? current = "second";
        Assert.True(handler.SaveUse(liveCommand, ref current));
        var retained = Assert.IsType<FakeParameter>(current);
        Assert.Equal(Encoding.UTF8.GetBytes("second"), Assert.IsType<byte[]>(retained.Value));

        Assert.True(handler.Update(liveCommand, ref current, "third"));
        Assert.Same(retained, current);
        Assert.Equal(Encoding.UTF8.GetBytes("third"), Assert.IsType<byte[]>(retained.Value));

        object? rejected = 4;
        Assert.False(handler.CanHandle(ref rejected));
        Assert.True(handler.UpdateCache(new DefaultParamCache(liveCommand)));
    }
}
