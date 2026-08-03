using RinkuLib.DbParsing;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Mapping;

public class ConfigurationCacheTests {
    [Fact]
    public void Mapping_changes_do_not_reuse_a_parser_from_the_previous_configuration() {
        TypeParsingInfo info = TypeParsingInfo.GetOrAdd<CacheValue>();
        ColumnInfo[] firstColumns = [new("Value", typeof(int), false)];
        var first = TypeParser.GetTypeParser<CacheValue>(ref firstColumns);

        Assert.True(info.UpdateAltName(names => names.GetDefaultName() == "Value"
            ? names.AddAltName("Other")
            : null));

        ColumnInfo[] secondColumns = [new("Value", typeof(int), false)];
        var second = TypeParser.GetTypeParser<CacheValue>(ref secondColumns);
        Assert.NotSame(first, second);
    }

    private sealed record CacheValue(int Value) : IDbReadable;
}
