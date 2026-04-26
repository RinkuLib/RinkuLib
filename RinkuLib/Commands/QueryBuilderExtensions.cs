using System.Data;
using System.Data.Common;
using RinkuLib.Queries;
using RinkuLib.TypeAccessing;

namespace RinkuLib.Commands;
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
        public T ExecuteScalar<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
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
        public Task<T> ExecuteScalarAsync<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
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
        public T? Query<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCachedParser<T>(vars, out var parser))
                return parser.Query(cmd, true);
            else if (parser is not null)
                return parser.Query(cmd, command, true);
            return cmd.Query(true, null, new LinkerQueryCommandWithParser<T>(command, vars.ToBoolArray()));
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QueryAsync<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCachedParser<T>(vars, out var parser))
                return parser.QueryAsync(cmd, true, ct);
            else if (parser is not null)
                return parser.QueryAsync(cmd, command, true, ct);
            return cmd.QueryAsync(true, null, new LinkerQueryCommandWithParser<T>(command, vars.ToBoolArray()), ct);
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
                return cmd.StreamQueryAsync(false, parser, (ICache?)null, ct);
            else if (parser is not null)
                return cmd.StreamQueryAsync(false, parser, command, ct);
            return cmd.StreamQueryAsync(false, null, new LinkerQueryCommandWithParser<T>(command, vars.ToBoolArray()), ct);
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
            return cmd.Execute(true, command.NeedToCache(vars) ? command : null);
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
            return cmd.ExecuteAsync(true, command.NeedToCache(vars) ? command : null, ct);
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
            return cmd.ExecuteScalar<T>(true, command.NeedToCache(vars) ? command : null);
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
            return cmd.ExecuteScalarAsync<T>(true, command.NeedToCache(vars) ? command : null, ct);
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
            return cmd.ExecuteReader(behavior, command.NeedToCache(vars) ? command : null);
        }
        /// <summary>
        /// Executes the reader of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<DbDataReader> ExecuteReaderAsync(IDbConnection cnn, out IDbCommand cmd, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            cmd = GetCommand(command, vars, cnn, transaction, timeout);
            return cmd.ExecuteReaderAsync(behavior, command.NeedToCache(vars) ? command : null, ct);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public MultiReader ExecuteMultiReader(IDbConnection cnn, out IDbCommand cmd, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            cmd = GetCommand(command, vars, cnn, transaction, timeout);
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteMultiReader(command, vars.ToBoolArr(), false, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<MultiReader> ExecuteMultiReaderAsync(IDbConnection cnn, out IDbCommand cmd, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            cmd = GetCommand(command, vars, cnn, transaction, timeout);
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteMultiReaderAsync(command, vars.ToBoolArr(), false, behavior, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? Query<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCachedParser<T>(vars, out var parser))
                return parser.Query(cmd, true);
            else if (parser is not null)
                return parser.Query(cmd, command, true);
            return cmd.Query(true, null, new LinkerQueryCommandWithParser<T>(command, vars.ToBoolArray()));
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QueryAsync<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = GetCommand(command, vars, cnn, transaction, timeout);
            if (command.TryGetCachedParser<T>(vars, out var parser))
                return parser.QueryAsync(cmd, true, ct);
            else if (parser is not null)
                return parser.QueryAsync(cmd, command, true, ct);
            return cmd.QueryAsync(true, null, new LinkerQueryCommandWithParser<T>(command, vars.ToBoolArray()), ct);
        }
    }
}