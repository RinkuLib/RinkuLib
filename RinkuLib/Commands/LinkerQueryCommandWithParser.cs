using System.Data;
using System.Data.Common;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.Commands;

/// <summary>
/// Uses the <see cref="TypeParser"/> to retrieve or make the complied parser function and cache both the parser and any used parameters
/// </summary>
public class LinkerQueryCommandWithParser<T>(QueryCommand command, bool[] usageMap) : ICacheGivingParser<T> {
    private readonly QueryCommand Command = command;
    private readonly bool[] UsageMap = usageMap;
    /// <inheritdoc/>
    public CommandBehavior Behavior => CommandBehavior.Default;
    /// <inheritdoc/>
    public ITypeParser<T> UpdateCache(IDbCommand cmd, DbDataReader reader) {
        var schema = reader.GetColumns();
        var parser = TypeParser.GetTypeParser<T>(ref schema);
        Command.UpdateParseCache(UsageMap, schema, parser);
        Command.UpdateCache(cmd);
        return parser;
    }
    /// <inheritdoc/>
    public ValueTask<ITypeParser<T>> UpdateCacheAsync(IDbCommand cmd, DbDataReader reader, CancellationToken ct = default)
        => new(UpdateCache(cmd, reader));
}