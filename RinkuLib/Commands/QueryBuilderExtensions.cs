using System.Data;
using System.Data.Common;
using RinkuLib.Queries;
using RinkuLib.TypeAccessing;

namespace RinkuLib.Commands;
/// <summary>
/// Runs a query off an in-memory <see cref="QueryBuilder"/>. Once the builder holds the values you want,
/// these methods turn them into a command on a connection and execute it, in the same shapes the command's
/// own execution methods offer.
/// </summary>
public static class QueryBuilderExtensions {
    /// <summary>
    /// Builds a fresh command on <paramref name="cnn"/> for the current builder values. Useful when you want
    /// the command in hand rather than running it through one of the execution methods.
    /// </summary>
    /// <param name="command">The command whose text and parameters are produced from the values.</param>
    /// <param name="variables">The builder values driving the command.</param>
    /// <param name="cnn">The connection to create the command on.</param>
    /// <param name="transaction">The transaction to enlist, if any.</param>
    /// <param name="timeout">The command timeout, if set.</param>
    /// <returns>A command ready to run.</returns>
    public static DbCommand GetCommand<T>(T command, object?[] variables, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) where T : IQueryCommand {
        var cmd = cnn.CreateCommand();
        if (transaction is not null)
            cmd.Transaction = transaction;
        if (timeout.HasValue)
            cmd.CommandTimeout = timeout.Value;
        command.SetCommand(cmd, variables);
        return cmd;
    }
    /// <inheritdoc cref="GetCommand{T}(T, object[], DbConnection, DbTransaction, int?)"/>
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
            return cmd.Execute(true, command.NeedToCache(vars) ? command : null);
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
            return cmd.ExecuteAsync(true, command.NeedToCache(vars) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? ExecuteScalar<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            return cmd.ExecuteScalar<T>(true, command.NeedToCache(vars) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> ExecuteScalarAsync<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            return cmd.ExecuteScalarAsync<T>(true, command.NeedToCache(vars) ? command : null, ct);
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
            return cmd.ExecuteReader(behavior, command.NeedToCache(vars) ? command : null);
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
            return cmd.ExecuteReaderAsync(behavior, command.NeedToCache(vars) ? command : null, ct);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public MultiReader ExecuteMultiReader(DbConnection cnn, out DbCommand cmd, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            cmd = GetCommand(command, vars, cnn, transaction, timeout);
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteMultiReader(command, vars.ToBoolArr(), false, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<MultiReader> ExecuteMultiReaderAsync(DbConnection cnn, out DbCommand cmd, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            cmd = GetCommand(command, vars, cnn, transaction, timeout);
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteMultiReaderAsync(command, vars.ToBoolArr(), false, behavior, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T Query<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCachedParser<T>(vars, out var parser))
                return parser.Query(cmd, true);
            else if (parser is not null)
                return parser.Query(cmd, command, true);
            return cmd.Query(new LinkerQueryCommandWithParser<T>(command, vars.ToBoolArray()), true);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> QueryAsync<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCachedParser<T>(vars, out var parser))
                return parser.QueryAsync(cmd, true, ct);
            else if (parser is not null)
                return parser.QueryAsync(cmd, command, true, ct);
            return cmd.QueryAsync(new LinkerQueryCommandWithParser<T>(command, vars.ToBoolArray()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> StreamQueryAsync<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCachedParser<T>(vars, out var parser))
                return cmd.StreamQueryAsync(parser, null, false, ct);
            else if (parser is not null)
                return cmd.StreamQueryAsync(parser, command, false, ct);
            return cmd.StreamQueryAsync(new LinkerQueryCommandWithParser<T>(command, vars.ToBoolArray()), false, ct);
        }



        /// <inheritdoc cref="QueryBuilderExtensions.Execute(QueryBuilder, DbConnection, DbTransaction, int?)"/>
        public int Execute(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            return cmd.Execute(true, command.NeedToCache(vars) ? command : null);
        }
        /// <inheritdoc cref="QueryBuilderExtensions.ExecuteAsync(QueryBuilder, DbConnection, DbTransaction, int?, CancellationToken)"/>
        public Task<int> ExecuteAsync(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            return cmd.ExecuteAsync(true, command.NeedToCache(vars) ? command : null, ct);
        }
        /// <inheritdoc cref="QueryBuilderExtensions.ExecuteScalar{T}(QueryBuilder, DbConnection, DbTransaction, int?)"/>
        public T? ExecuteScalar<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            return cmd.ExecuteScalar<T>(true, command.NeedToCache(vars) ? command : null);
        }
        /// <inheritdoc cref="QueryBuilderExtensions.ExecuteScalarAsync{T}(QueryBuilder, DbConnection, DbTransaction, int?, CancellationToken)"/>
        public Task<T?> ExecuteScalarAsync<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            return cmd.ExecuteScalarAsync<T>(true, command.NeedToCache(vars) ? command : null, ct);
        }
        /// <inheritdoc cref="QueryBuilderExtensions.ExecuteReader(QueryBuilder, DbConnection, out DbCommand, CommandBehavior, DbTransaction, int?)"/>
        public DbDataReader ExecuteReader(IDbConnection cnn, out IDbCommand cmd, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            cmd = GetCommand(command, vars, cnn, transaction, timeout);
            return cmd.ExecuteReader(behavior, command.NeedToCache(vars) ? command : null);
        }
        /// <inheritdoc cref="QueryBuilderExtensions.ExecuteReaderAsync(QueryBuilder, DbConnection, out DbCommand, CommandBehavior, DbTransaction, int?, CancellationToken)"/>
        public Task<DbDataReader> ExecuteReaderAsync(IDbConnection cnn, out IDbCommand cmd, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            cmd = GetCommand(command, vars, cnn, transaction, timeout);
            return cmd.ExecuteReaderAsync(behavior, command.NeedToCache(vars) ? command : null, ct);
        }
        /// <inheritdoc cref="QueryBuilderExtensions.ExecuteMultiReader(QueryBuilder, DbConnection, out DbCommand, CommandBehavior, DbTransaction, int?)"/>
        public MultiReader ExecuteMultiReader(IDbConnection cnn, out IDbCommand cmd, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            cmd = GetCommand(command, vars, cnn, transaction, timeout);
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteMultiReader(command, vars.ToBoolArr(), false, behavior);
        }
        /// <inheritdoc cref="QueryBuilderExtensions.ExecuteMultiReaderAsync(QueryBuilder, DbConnection, out DbCommand, CommandBehavior, DbTransaction, int?, CancellationToken)"/>
        public Task<MultiReader> ExecuteMultiReaderAsync(IDbConnection cnn, out IDbCommand cmd, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            cmd = GetCommand(command, vars, cnn, transaction, timeout);
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteMultiReaderAsync(command, vars.ToBoolArr(), false, behavior, ct);
        }
        /// <inheritdoc cref="QueryBuilderExtensions.Query{T}(QueryBuilder, DbConnection, DbTransaction, int?)"/>
        public T Query<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCachedParser<T>(vars, out var parser))
                return parser.Query(cmd, true);
            else if (parser is not null)
                return parser.Query(cmd, command, true);
            return cmd.Query(new LinkerQueryCommandWithParser<T>(command, vars.ToBoolArray()), true);
        }
        /// <inheritdoc cref="QueryBuilderExtensions.QueryAsync{T}(QueryBuilder, DbConnection, DbTransaction, int?, CancellationToken)"/>
        public Task<T> QueryAsync<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCachedParser<T>(vars, out var parser))
                return parser.QueryAsync(cmd, true, ct);
            else if (parser is not null)
                return parser.QueryAsync(cmd, command, true, ct);
            return cmd.QueryAsync(new LinkerQueryCommandWithParser<T>(command, vars.ToBoolArray()), true, ct);
        }
    }
}