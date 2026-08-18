using System.Data;
using System.Data.Common;
using Rinku.Mapping;
using Rinku.Mapping.Parsers;

namespace Rinku;

internal sealed class RuntimeParserLinker(QueryCommand command, Type type, bool[] usageMap) : ICacheGivingParser {
    public CommandBehavior Behavior => command.TryGetCachedParser(type, usageMap, out var parser) ? parser.Behavior : CommandBehavior.SingleResult;

    public ITypeParser UpdateCache(IDbCommand cmd, DbDataReader reader) {
        var parser = command.TryGetCachedParser(type, usageMap, out var cached)
            ? cached
            : TypeParser.GetTypeParser(type, reader.GetColumnsFast());
        command.UpdateParseCache(usageMap, parser);
        command.UpdateCache(cmd);
        return parser;
    }

    public async ValueTask<ITypeParser> UpdateCacheAsync(IDbCommand cmd, DbDataReader reader, CancellationToken ct = default) {
        await Task.CompletedTask.ConfigureAwait(false);
        return UpdateCache(cmd, reader);
    }
}
