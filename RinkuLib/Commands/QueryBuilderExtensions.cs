using System.Data;
using System.Data.Common;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tools;

namespace RinkuLib.Commands;
/// <summary>
/// Uses the <see cref="TypeParser{T}"/> to retrieve or make the complied parser function and cache both the parser and any used parameters
/// </summary>
public struct ParsingCacheToMake<T>(QueryCommand command, SchemaParser<T> cache, int[] falseIndexes) : ISchemaParser<T> {
    private readonly QueryCommand Command = command;
    private Func<DbDataReader, T> parser = cache.parser;
    /// <inheritdoc/>
    public CommandBehavior Behavior { get; } = cache.Behavior;
    private readonly int[] FalseIndexes = falseIndexes;
    /// <inheritdoc/>
    public readonly bool IsInit => false;
    /// <inheritdoc/>
    public void Init(DbDataReader reader, IDbCommand cmd) {
        if (parser == null) {
            var schema = reader.GetColumns();
            var p = TypeParser<T>.GetParserFunc(ref schema, out var defaultBehavior);
            parser = p;
            Command.UpdateCache(FalseIndexes, schema, new SchemaParser<T>(parser, defaultBehavior));
        }
        Command.UpdateCache(cmd);
    }
    /// <inheritdoc/>
    public readonly T Parse(DbDataReader reader) => parser(reader);
}
/// <summary>
/// No cache are actualy associated with this struct
/// </summary>
public struct NoNeedToCache : ICache {
    /// <inheritdoc/>
    public readonly void UpdateCache(IDbCommand cmd) { }
}
/// <summary>
/// Extensions on <see cref="QueryBuilder"/>
/// </summary>
public static class QueryBuilderExtensions {
    /// <summary>
    /// Create a <see cref="IDbCommand"/> using the <see cref="QueryCommand"/> blueprint and a state array
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="command">The blueprint that uses the state to create the <see cref="DbCommand"/></param>
    /// <param name="variables">The current state array for the <see cref="DbCommand"/> creation</param>
    /// <param name="cnn">The connection to execute on</param>
    /// <param name="transaction">The transaction to execute on</param>
    /// <param name="timeout">The timeout for the command</param>
    /// <returns></returns>
    public static DbCommand GetCommand<T>(T command, object?[] variables, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) where T : IQueryCommand {
        var cmd = cnn.CreateCommand();
        if (transaction is not null)
            cmd.Transaction = transaction;
        if (timeout.HasValue)
            cmd.CommandTimeout = timeout.Value;
        command.SetCommand(cmd, variables);
        return cmd;
    }
    /// <summary>
    /// Create a <see cref="IDbCommand"/> using the <see cref="IQueryCommand"/> blueprint and a state array
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="command">The blueprint that uses the state to create the <see cref="IDbCommand"/></param>
    /// <param name="variables">The current state array for the <see cref="IDbCommand"/> creation</param>
    /// <param name="cnn">The connection to execute on</param>
    /// <param name="transaction">The transaction to execute on</param>
    /// <param name="timeout">The timeout for the command</param>
    /// <returns></returns>
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
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public int ExecuteQuery(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.NeedToCache(vars))
                return cmd.ExecuteQuery(command, true);
            return cmd.ExecuteQuery<NoNeedToCache>(default, true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteQueryAsync(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.NeedToCache(vars))
                return cmd.ExecuteQueryAsync(command, true, ct);
            return cmd.ExecuteQueryAsync<NoNeedToCache>(default, true, ct);
        }

        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? QuerySingle<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QuerySingle<SchemaParser<T>, T>(cache, true);
            return cmd.QuerySingle<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public IEnumerable<T> QueryMultiple<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryMultiple<SchemaParser<T>, T>(cache, true);
            return cmd.QueryMultiple<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), true);
        }

        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QuerySingleAsync<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QuerySingleAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QuerySingleAsync<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryMultipleAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryMultipleAsync<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), true, ct);
        }

        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public int ExecuteQuery(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.NeedToCache(vars))
                return cmd.ExecuteQuery(command, true);
            return cmd.ExecuteQuery<NoNeedToCache>(default, true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteQueryAsync(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.NeedToCache(vars))
                return cmd.ExecuteQueryAsync(command, true, ct);
            return cmd.ExecuteQueryAsync<NoNeedToCache>(default, true, ct);
        }

        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? QuerySingle<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QuerySingle<SchemaParser<T>, T>(cache, true);
            return cmd.QuerySingle<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public IEnumerable<T> QueryMultiple<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryMultiple<SchemaParser<T>, T>(cache, true);
            return cmd.QueryMultiple<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), true);
        }

        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QuerySingleAsync<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QuerySingleAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QuerySingleAsync<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryMultipleAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryMultipleAsync<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), true, ct);
        }
    }
}
