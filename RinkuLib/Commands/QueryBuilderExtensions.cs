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
        public int Execute(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.NeedToCache(vars))
                return cmd.Execute(command, true);
            return cmd.Execute<NoNeedToCache>(default, true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteAsync(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.NeedToCache(vars))
                return cmd.ExecuteAsync(command, true, ct);
            return cmd.ExecuteAsync<NoNeedToCache>(default, true, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T ExecuteScalar<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.NeedToCache(vars))
                return cmd.ExecuteScalar<T, QueryCommand>(command, true);
            return cmd.ExecuteScalar<T, NoNeedToCache>(default, true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> ExecuteScalarAsync<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.NeedToCache(vars))
                return cmd.ExecuteScalarAsync<T, QueryCommand>(command, true, ct);
            return cmd.ExecuteScalarAsync<T, NoNeedToCache>(default, true, ct);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public DbDataReader ExecuteReader(DbConnection cnn, out DbCommand cmd, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.NeedToCache(vars))
                return cmd.ExecuteReader(command, behavior);
            return cmd.ExecuteReader<NoNeedToCache>(default, behavior);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public DbDataReader ExecuteReader(DbConnection cnn, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.NeedToCache(vars))
                return cmd.ExecuteReader(command, behavior);
            return cmd.ExecuteReader<NoNeedToCache>(default, behavior);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<DbDataReader> ExecuteReaderAsync(DbConnection cnn, out DbCommand cmd, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.NeedToCache(vars))
                return cmd.ExecuteReaderAsync(command, behavior, ct);
            return cmd.ExecuteReaderAsync<NoNeedToCache>(default, behavior, ct);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<DbDataReader> ExecuteReaderAsync(DbConnection cnn, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.NeedToCache(vars))
                return cmd.ExecuteReaderAsync(command, behavior, ct);
            return cmd.ExecuteReaderAsync<NoNeedToCache>(default, behavior, ct);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public MultiReader ExecuteMultiReader(DbConnection cnn, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteMultiReader(command, vars.ToBoolArr(), false, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<MultiReader> ExecuteMultiReaderAsync(DbConnection cnn, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteMultiReaderAsync(command, vars.ToBoolArr(), false, behavior, ct);
        }

        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? QueryOne<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryOne<SchemaParser<T>, T>(cache, true);
            return cmd.QueryOne<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public IEnumerable<T> QueryAll<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryAll<SchemaParser<T>, T>(cache, true);
            return cmd.QueryAll<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse each rows to return a buffered collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public List<T> QueryAllBuffered<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryAllBuffered<SchemaParser<T>, T>(cache, true);
            return cmd.QueryAllBuffered<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), true);
        }

        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QueryOneAsync<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryOneAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryOneAsync<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryAllAsync<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryAllAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryAllAsync<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<List<T>> QueryAllBufferedAsync<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryAllBufferedAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryAllBufferedAsync<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), true, ct);
        }

        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public int Execute(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.NeedToCache(vars))
                return cmd.Execute(command, true);
            return cmd.Execute<NoNeedToCache>(default, true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteAsync(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.NeedToCache(vars))
                return cmd.ExecuteAsync(command, true, ct);
            return cmd.ExecuteAsync<NoNeedToCache>(default, true, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T ExecuteScalar<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.NeedToCache(vars))
                return cmd.ExecuteScalar<T, QueryCommand>(command, true);
            return cmd.ExecuteScalar<T, NoNeedToCache>(default, true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> ExecuteScalarAsync<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.NeedToCache(vars))
                return cmd.ExecuteScalarAsync<T, QueryCommand>(command, true, ct);
            return cmd.ExecuteScalarAsync<T, NoNeedToCache>(default, true, ct);
        }
        /// <summary>
        /// Executes the reader of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public DbDataReader ExecuteReader(IDbConnection cnn, out IDbCommand cmd, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.NeedToCache(vars))
                return cmd.ExecuteReader(command, behavior);
            return cmd.ExecuteReader<NoNeedToCache>(default, behavior);
        }
        /// <summary>
        /// Executes the reader of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public DbDataReader ExecuteReader(IDbConnection cnn, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.NeedToCache(vars))
                return cmd.ExecuteReader(command, behavior);
            return cmd.ExecuteReader<NoNeedToCache>(default, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public MultiReader ExecuteMultiReader(IDbConnection cnn, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteMultiReader(command, vars.ToBoolArr(), false, behavior);
        }

        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? QueryOne<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryOne<SchemaParser<T>, T>(cache, true);
            return cmd.QueryOne<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public IEnumerable<T> QueryAll<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryAll<SchemaParser<T>, T>(cache, true);
            return cmd.QueryAll<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse each rows to return a buffered collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public List<T> QueryAllBuffered<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryAllBuffered<SchemaParser<T>, T>(cache, true);
            return cmd.QueryAllBuffered<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), true);
        }

        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QueryOneAsync<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryOneAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryOneAsync<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryAllAsync<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryAllAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryAllAsync<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse each rows to return a buffered collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<List<T>> QueryAllBufferedAsync<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryAllBufferedAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryAllBufferedAsync<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), true, ct);
        }
    }
}
