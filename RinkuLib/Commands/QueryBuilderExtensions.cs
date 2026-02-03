using System.Data;
using System.Data.Common;
using RinkuLib.DbParsing;
using RinkuLib.Queries;

namespace RinkuLib.Commands;

public unsafe struct ParsingCacheToMake<T>(QueryCommand command, ParsingCache<T> cache, int index) : IParsingCache<T> {
    private readonly QueryCommand Command = command;
    //private delegate*<DbDataReader, T> parser = cache.parser;
    private Func<DbDataReader, T> parser = cache.parser;
    public CommandBehavior Behavior { get; } = cache.Behavior;
    private readonly int Index = index;
    public readonly bool IsValid => false;
    public void Init(DbDataReader reader, IDbCommand cmd) {
        if (parser == null) {
            var p = TypeParser<T>.GetParserFunc(reader.GetColumns(), out var defaultBehavior);
            parser = p;
            Command.UpdateCache(Index, new ParsingCache<T>(parser, defaultBehavior));
        }
        Command.UpdateCache(cmd);
    }

    public readonly T Parse(DbDataReader reader) => parser(reader);
}
public struct NoNeedToCache : ICache {
    public readonly void UpdateCache(IDbCommand cmd) { }
}

public static class QueryBuilderExtensions {
    public static DbCommand GetCommand<T>(T command, object?[] variables, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) where T : IQueryCommand {
        var cmd = cnn.CreateCommand();
        if (transaction is not null)
            cmd.Transaction = transaction;
        if (timeout.HasValue)
            cmd.CommandTimeout = timeout.Value;
        command.SetCommand(cmd, variables);
        return cmd;
    }
    public static IDbCommand GetCommand<T>(T command, object?[] variables, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) where T : IQueryCommand {
        var cmd = cnn.CreateCommand();
        if (transaction is not null)
            cmd.Transaction = transaction;
        if (timeout.HasValue)
            cmd.CommandTimeout = timeout.Value;
        if (cmd is DbCommand c)
            command.SetCommand(c, variables);
        else
            command.SetCommand(cmd, variables);
        return cmd;
    }

    extension(QueryBuilder builder) {
        public int ExecuteQuery(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.NeedToCache(vars))
                return cmd.ExecuteQuery(command, true);
            return cmd.ExecuteQuery<NoNeedToCache>(default, true);
        }
        public Task<int> ExecuteQueryAsync(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.NeedToCache(vars))
                return cmd.ExecuteQueryAsync(command, true, ct);
            return cmd.ExecuteQueryAsync<NoNeedToCache>(default, true, ct);
        }
        public T? QuerySingle<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QuerySingle<ParsingCache<T>, T>(cache, true);
            return cmd.QuerySingle<ParsingCacheToMake<T>, T>(new(command, cache, command.GetActualGetCacheIndex<T>(vars)), true);
        }
        public IEnumerable<T> QueryMultiple<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryMultiple<ParsingCache<T>, T>(cache, true);
            return cmd.QueryMultiple<ParsingCacheToMake<T>, T>(new(command, cache, command.GetActualGetCacheIndex<T>(vars)), true);
        }

        public Task<T?> QuerySingleAsync<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QuerySingleAsync<ParsingCache<T>, T>(cache, true, ct);
            return cmd.QuerySingleAsync<ParsingCacheToMake<T>, T>(new(command, cache, command.GetActualGetCacheIndex<T>(vars)), true, ct);
        }
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryMultipleAsync<ParsingCache<T>, T>(cache, true, ct);
            return cmd.QueryMultipleAsync<ParsingCacheToMake<T>, T>(new(command, cache, command.GetActualGetCacheIndex<T>(vars)), true, ct);
        }

        public T? QuerySingle<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QuerySingle<ParsingCache<T>, T>(cache, true);
            return cmd.QuerySingle<ParsingCacheToMake<T>, T>(new(command, cache, command.GetActualGetCacheIndex<T>(vars)), true);
        }
        public IEnumerable<T> QueryMultiple<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryMultiple<ParsingCache<T>, T>(cache, true);
            return cmd.QueryMultiple<ParsingCacheToMake<T>, T>(new(command, cache, command.GetActualGetCacheIndex<T>(vars)), true);
        }

        public Task<T?> QuerySingleAsync<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QuerySingleAsync<ParsingCache<T>, T>(cache, true, ct);
            return cmd.QuerySingleAsync<ParsingCacheToMake<T>, T>(new(command, cache, command.GetActualGetCacheIndex<T>(vars)), true, ct);
        }
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryMultipleAsync<ParsingCache<T>, T>(cache, true, ct);
            return cmd.QueryMultipleAsync<ParsingCacheToMake<T>, T>(new(command, cache, command.GetActualGetCacheIndex<T>(vars)), true, ct);
        }
    }
}
