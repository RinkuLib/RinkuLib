using System.Data;
using System.Data.Common;
using Rinku.Mapping;
using Rinku.Querying;
using Rinku.Internal;
using Rinku.Mapping.Parsers;

namespace Rinku;

/// <summary>
/// Bridges a <see cref="QueryCommand"/> to <typeparamref name="T"/> the first time it runs, when no parser
/// is cached yet. It builds the parser from the result's columns and stores it, along with the parameter
/// metadata, on the command so later runs with the same shape skip the work. Used by the command's own query
/// methods, not something you construct directly.
/// </summary>
public class LinkerQueryCommandWithParser<T>(QueryCommand command, bool[] usageMap) : ICacheGivingParser<T> {
    private readonly QueryCommand Command = command;
    private readonly bool[] UsageMap = usageMap;
    /// <inheritdoc/>
    public CommandBehavior Behavior => CommandBehavior.Default;
    /// <inheritdoc/>
    public ITypeParser<T> UpdateCache(IDbCommand cmd, DbDataReader reader) {
        var schema = reader.GetColumns();
        var parser = TypeParser.GetTypeParser<T>(schema);
        Command.UpdateParseCache(UsageMap, parser);
        Command.UpdateCache(cmd);
        return parser;
    }
    /// <inheritdoc/>
    public ValueTask<ITypeParser<T>> UpdateCacheAsync(IDbCommand cmd, DbDataReader reader, CancellationToken ct = default)
        => new(UpdateCache(cmd, reader));
}
