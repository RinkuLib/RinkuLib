using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.Commands;

/// <summary>
/// Uses the <see cref="TypeParser{T}"/> to retrieve or make the complied parser function and cache both the parser and any used parameters
/// </summary>
public class LinkerQueryCommandWithParser<T>(QueryCommand command, bool[] usageMap) : ICacheUsingParser<T> {
    private readonly QueryCommand Command = command;
    private readonly bool[] UsageMap = usageMap;

    /// <inheritdoc/>
    public void UpdateCache(IDbCommand cmd, DbDataReader reader, [NotNull] ref ITypeParser<T>? parser) {
        var schema = reader.GetColumns();
        parser ??= TypeParser<T>.GetTypeParser(ref schema);
        Command.UpdateParseCache(UsageMap, schema, parser);
        Command.UpdateCache(cmd);
    }

}

/// <summary>
/// Uses the <see cref="TypeParser{T}"/> to retrieve or make the complied parser function and cache both the parser and any used parameters
/// </summary>
public class CacheWrapper<T>(ICache cache) : ICacheUsingParser<T> {
    private readonly ICache Cache = cache;

    /// <inheritdoc/>
    public void UpdateCache(IDbCommand cmd, DbDataReader reader, [NotNull] ref ITypeParser<T>? parser) {
        var schema = reader.GetColumns();
        parser ??= TypeParser<T>.GetTypeParser(ref schema);

        Cache.UpdateCache(cmd);
    }

}
