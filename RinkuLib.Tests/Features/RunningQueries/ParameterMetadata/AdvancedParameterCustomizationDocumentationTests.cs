using System.Data;
using Rinku;
using Rinku.Querying;
using Rinku.Querying.Defaults;
using RinkuLib.Tests.Documentation;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Templating;

/// <summary>Executable examples for docs/articles/customization/parameters.md.</summary>
[Collection("ParamGetterMakers")]
public class AdvancedParameterCustomizationDocumentationTests {
    private readonly record struct Names(IReadOnlyList<string> Items);

    private sealed class NamesParam : ConvertedDbParamInfo<Names> {
        protected override object ConvertValue(Names value) => string.Join(',', value.Items);

        protected override void ConfigureParameter(IDbDataParameter parameter)
            => parameter.DbType = DbType.String;
    }

    private sealed class FixedStringGetter(IDbCommand command) : IDbParamInfoGetter {
        public IEnumerable<KeyValuePair<string, int>> EnumerateParameters()
            => command.Parameters.Cast<IDataParameter>()
                .Select((parameter, index) => KeyValuePair.Create(parameter.ParameterName, index));

        public DbParamInfo MakeInfoAt(int index) => TypedDbParamCache.Get(DbType.String, 200);

        public bool TryGetInfo(string name, out DbParamInfo info) {
            int index = command.Parameters.IndexOf(name);
            info = index < 0 ? null! : MakeInfoAt(index);
            return index >= 0;
        }
    }

    private static bool MakeFixedStringGetter(IDbCommand command, out IDbParamInfoGetter getter) {
        if (command is not Microsoft.Data.SqlClient.SqlCommand) {
            getter = null!;
            return false;
        }

        getter = new FixedStringGetter(command);
        return true;
    }

    private sealed class AppParameterDefaults : IDbParameterDefaults {
        private readonly DefaultDbParameterServices shipped = new();

        public DbParamInfo Inferred => shipped.Inferred;

        public DbParamInfo MakeInfo(IDbDataParameter parameter) {
            if (parameter.DbType == DbType.String && parameter.Size == 0)
                return TypedDbParamCache.Get(DbType.String, 4000);

            return shipped.MakeInfo(parameter);
        }
    }

    [Fact]
    [DocumentationExample("parameters.md", "converted-parameter")]
    public void Converted_parameter_info_changes_the_provider_value_and_metadata() {
        var saveSearch = new QueryCommand("INSERT INTO saved_searches(Names) VALUES (@names)");
        Assert.True(saveSearch.UpdateParamCache("@names", new NamesParam()));

        var command = Render.From(saveSearch, new { names = new Names(["One", "Two"]) });

        Assert.Equal("INSERT INTO saved_searches(Names) VALUES (@names)", command.CommandText);
        var parameter = Assert.Single(command.BoundParameters);
        Assert.Equal("One,Two", parameter.Value);
        Assert.Equal(DbType.String, parameter.DbType);
    }

    [Fact]
    [DocumentationExample("parameters.md", "provider-getter")]
    public void Registered_metadata_getter_supplies_the_parameter_strategy() {
        ParamInfoGetterMaker maker = MakeFixedStringGetter;
        IDbParamInfoGetter.ParamGetterMakers.Add(maker);
        try {
            Assert.False(maker(new FakeCommand(), out _));

            using var providerCommand = new Microsoft.Data.SqlClient.SqlCommand();
            providerCommand.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter { ParameterName = "@value" });

            var getter = new FixedStringGetter(providerCommand);
            Assert.Equal(new KeyValuePair<string, int>("@value", 0), Assert.Single(getter.EnumerateParameters()));
            Assert.True(getter.TryGetInfo("@value", out var directInfo));
            Assert.NotNull(directInfo);
            Assert.False(getter.TryGetInfo("@missing", out _));

            Assert.True(IDbParamInfoGetter.TryGetParamInfo(providerCommand, "@value", out var info));

            var bound = new FakeCommand();
            Assert.True(info.Use("@value", bound, "text"));
            var parameter = Assert.Single(bound.BoundParameters);
            Assert.Equal(DbType.String, parameter.DbType);
            Assert.Equal(200, parameter.Size);
        }
        finally {
            IDbParamInfoGetter.ParamGetterMakers.Remove(maker);
        }
    }

    [Fact]
    [DocumentationExample("parameters.md", "parameter-defaults")]
    public void Replaced_parameter_defaults_control_unmatched_metadata() {
        var previous = DbParameterDefaults.Current;
        try {
            DbParameterDefaults.Current = new AppParameterDefaults();
            using var query = new QueryCommand("SELECT @value");
            var providerCommand = new FakeCommand();
            providerCommand.Parameters.Add(new FakeParameter {
                ParameterName = "@value",
                DbType = DbType.String,
                Size = 0,
                Value = "first"
            });

            query.UpdateCache(providerCommand);
            var command = Render.From(query, new { value = "text" });

            var parameter = Assert.Single(command.BoundParameters);
            Assert.Equal(DbType.String, parameter.DbType);
            Assert.Equal(4000, parameter.Size);
        }
        finally {
            DbParameterDefaults.Current = previous;
        }
    }
}
