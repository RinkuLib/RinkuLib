using System.Data;
using System.Data.Common;
using Rinku.Mapping;
using Rinku.Querying;
using Rinku.Internal;
using Rinku.Mapping.Parsers;

namespace Rinku;

internal sealed class LinkerQueryCommandWithParser<T>(QueryCommand command, bool[] usageMap) : ICacheGivingParser<T> {
    private readonly QueryCommand Command = command;
    private readonly bool[] UsageMap = usageMap;
    public CommandBehavior Behavior => CommandBehavior.Default;
    public ITypeParser<T> UpdateCache(IDbCommand cmd, DbDataReader reader) {
        var schema = reader.GetColumns();
        var parser = TypeParser.GetTypeParser<T>(schema);
        Command.UpdateParseCache(UsageMap, parser);
        Command.UpdateCache(cmd);
        return parser;
    }
    public ValueTask<ITypeParser<T>> UpdateCacheAsync(IDbCommand cmd, DbDataReader reader, CancellationToken ct = default)
        => new(UpdateCache(cmd, reader));
}
