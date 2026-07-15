using System.Data;
using System.Data.Common;
using RinkuLib.Queries;

namespace RinkuLib.Commands; 
/// <summary>
/// Runs a <see cref="QueryCommand"/> straight against a connection, taking its values from a parameter
/// object. This is the primary way to execute a declared command, one call that opens a command, binds the
/// object, runs, and returns the shape you ask for. The overloads cover every result shape, sync and async,
/// with an optional <c>out</c> command when you need to read output parameters afterward.
/// </summary>
public static class DirectBuildExtensions {
    /// <summary>Projects the values to a presence map, <see langword="true"/> for each key that carries a value.</summary>
    public static bool[] ToBoolArray(this object?[] variables) {
        var arr = new bool[variables.Length];
        for (int i = 0; i < variables.Length; i++)
            if (variables[i] is not null)
                arr[i] = true;
        return arr;
    }
    /// <summary>The shared parser dispatch of the Query methods</summary>
    private static T QueryParse<T>(QueryCommand command, DbCommand cmd, Span<bool> usageMap, bool disposeCommand) {
        if (command.TryGetCachedParser<T>(usageMap, out var parser))
            return parser.Query(cmd, disposeCommand);
        else if (parser is not null)
            return parser.Query(cmd, command, disposeCommand);
        return cmd.Query(new LinkerQueryCommandWithParser<T>(command, usageMap.ToArray()), disposeCommand);
    }
    /// <summary>The shared parser dispatch of the Query methods</summary>
    private static T QueryParse<T>(QueryCommand command, IDbCommand cmd, Span<bool> usageMap, bool disposeCommand) {
        if (command.TryGetCachedParser<T>(usageMap, out var parser))
            return parser.Query(cmd, disposeCommand);
        else if (parser is not null)
            return parser.Query(cmd, command, disposeCommand);
        return cmd.Query(new LinkerQueryCommandWithParser<T>(command, usageMap.ToArray()), disposeCommand);
    }
    /// <summary>The shared parser dispatch of the QueryAsync methods</summary>
    private static Task<T> QueryParseAsync<T>(QueryCommand command, DbCommand cmd, Span<bool> usageMap, bool disposeCommand, CancellationToken ct) {
        if (command.TryGetCachedParser<T>(usageMap, out var parser))
            return parser.QueryAsync(cmd, disposeCommand, ct);
        else if (parser is not null)
            return parser.QueryAsync(cmd, command, disposeCommand, ct);
        return cmd.QueryAsync(new LinkerQueryCommandWithParser<T>(command, usageMap.ToArray()), disposeCommand, ct);
    }
    /// <summary>The shared parser dispatch of the QueryAsync methods</summary>
    private static Task<T> QueryParseAsync<T>(QueryCommand command, IDbCommand cmd, Span<bool> usageMap, bool disposeCommand, CancellationToken ct) {
        if (command.TryGetCachedParser<T>(usageMap, out var parser))
            return parser.QueryAsync(cmd, disposeCommand, ct);
        else if (parser is not null)
            return parser.QueryAsync(cmd, command, disposeCommand, ct);
        return cmd.QueryAsync(new LinkerQueryCommandWithParser<T>(command, usageMap.ToArray()), disposeCommand, ct);
    }
    /// <summary>The shared parser dispatch of the StreamQueryAsync methods</summary>
    private static IAsyncEnumerable<T> StreamParse<T>(QueryCommand command, DbCommand cmd, Span<bool> usageMap, bool disposeCommand, CancellationToken ct) {
        if (command.TryGetCachedParser<T>(usageMap, out var parser))
            return cmd.StreamQueryAsync(parser, null, disposeCommand, ct);
        else if (parser is not null)
            return cmd.StreamQueryAsync(parser, command, disposeCommand, ct);
        return cmd.StreamQueryAsync(new LinkerQueryCommandWithParser<T>(command, usageMap.ToArray()), disposeCommand, ct);
    }
    extension(QueryCommand command) {
        #region object param
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public int Execute(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.Execute(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the nb of affected rows, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command, kept for the caller to read (e.g. output parameters) and dispose</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public int Execute(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.Execute(false, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteAsync(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteAsync(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the nb of affected rows, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command, kept for the caller to read (e.g. output parameters) and dispose</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteAsync(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteAsync(false, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? ExecuteScalar<T>(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalar<T>(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return scalar value, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command, kept for the caller to read (e.g. output parameters) and dispose</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? ExecuteScalar<T>(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalar<T>(false, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> ExecuteScalarAsync<T>(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalarAsync<T>(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the scalar value, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command, kept for the caller to read (e.g. output parameters) and dispose</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> ExecuteScalarAsync<T>(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalarAsync<T>(false, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public DbDataReader ExecuteReader(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteReader(behavior, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<DbDataReader> ExecuteReaderAsync(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteReaderAsync(behavior, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public MultiReader ExecuteMultiReader(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = new bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReader(command, usageMap, true, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<MultiReader> ExecuteMultiReaderAsync(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = new bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReaderAsync(command, usageMap, true, behavior, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T Query<T>(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParse<T>(command, cmd, usageMap, true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command, kept for the caller to read (e.g. output parameters) and dispose</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T Query<T>(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParse<T>(command, cmd, usageMap, false);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> QueryAsync<T>(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParseAsync<T>(command, cmd, usageMap, true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command, kept for the caller to read (e.g. output parameters) and dispose</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> QueryAsync<T>(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParseAsync<T>(command, cmd, usageMap, false, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> StreamQueryAsync<T>(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return StreamParse<T>(command, cmd, usageMap, true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and lazily parse the rows, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command, kept for the caller to read (e.g. output parameters, filled once enumeration completes) and dispose</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> StreamQueryAsync<T>(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return StreamParse<T>(command, cmd, usageMap, false, ct);
        }



        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public int Execute(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.Execute(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command, kept for the caller to read (e.g. output parameters) and dispose</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public int Execute(IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.Execute(false, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteAsync(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteAsync(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command, kept for the caller to read (e.g. output parameters) and dispose</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteAsync(IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteAsync(false, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? ExecuteScalar<T>(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalar<T>(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return scalar value, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command, kept for the caller to read (e.g. output parameters) and dispose</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? ExecuteScalar<T>(IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalar<T>(false, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> ExecuteScalarAsync<T>(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalarAsync<T>(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the scalar value, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command, kept for the caller to read (e.g. output parameters) and dispose</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> ExecuteScalarAsync<T>(IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalarAsync<T>(false, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the reader of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public DbDataReader ExecuteReader(IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteReader(behavior, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes the reader of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<DbDataReader> ExecuteReaderAsync(IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteReaderAsync(behavior, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public MultiReader ExecuteMultiReader(IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = new bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReader(command, usageMap, true, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<MultiReader> ExecuteMultiReaderAsync(IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = new bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReaderAsync(command, usageMap, true, behavior, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T Query<T>(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParse<T>(command, cmd, usageMap, true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command, kept for the caller to read (e.g. output parameters) and dispose</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T Query<T>(IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParse<T>(command, cmd, usageMap, false);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> QueryAsync<T>(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParseAsync<T>(command, cmd, usageMap, true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command, kept for the caller to read (e.g. output parameters) and dispose</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> QueryAsync<T>(IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParseAsync<T>(command, cmd, usageMap, false, ct);
        }
        #endregion
        #region generic param

        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public int Execute<TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.Execute(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteAsync<TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteAsync(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? ExecuteScalar<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalar<T>(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> ExecuteScalarAsync<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalarAsync<T>(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public DbDataReader ExecuteReader<TObj>(DbConnection cnn, out DbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteReader(behavior, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<DbDataReader> ExecuteReaderAsync<TObj>(DbConnection cnn, out DbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteReaderAsync(behavior, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public MultiReader ExecuteMultiReader<TObj>(DbConnection cnn, out DbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = new bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReader(command, usageMap, true, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<MultiReader> ExecuteMultiReaderAsync<TObj>(DbConnection cnn, out DbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = new bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReaderAsync(command, usageMap, true, behavior, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T Query<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParse<T>(command, cmd, usageMap, true);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> QueryAsync<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParseAsync<T>(command, cmd, usageMap, true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> StreamQueryAsync<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return StreamParse<T>(command, cmd, usageMap, true, ct);
        }



        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public int Execute<TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.Execute(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteAsync<TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteAsync(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? ExecuteScalar<T, TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalar<T>(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> ExecuteScalarAsync<T, TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalarAsync<T>(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the reader of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public DbDataReader ExecuteReader<TObj>(IDbConnection cnn, out IDbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteReader(behavior, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes the reader of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<DbDataReader> ExecuteReaderAsync<TObj>(IDbConnection cnn, out IDbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteReaderAsync(behavior, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public MultiReader ExecuteMultiReader<TObj>(IDbConnection cnn, out IDbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = new bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReader(command, usageMap, true, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<MultiReader> ExecuteMultiReaderAsync<TObj>(IDbConnection cnn, out IDbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = new bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReaderAsync(command, usageMap, true, behavior, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T Query<T, TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParse<T>(command, cmd, usageMap, true);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> QueryAsync<T, TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParseAsync<T>(command, cmd, usageMap, true, ct);
        }
        #endregion
        #region ref generic param

        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public int Execute<TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.Execute(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteAsync<TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteAsync(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? ExecuteScalar<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteScalar<T>(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> ExecuteScalarAsync<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteScalarAsync<T>(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public DbDataReader ExecuteReader<TObj>(DbConnection cnn, out DbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteReader(behavior, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<DbDataReader> ExecuteReaderAsync<TObj>(DbConnection cnn, out DbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteReaderAsync(behavior, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public MultiReader ExecuteMultiReader<TObj>(DbConnection cnn, out DbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = new bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteMultiReader(command, usageMap, true, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<MultiReader> ExecuteMultiReaderAsync<TObj>(DbConnection cnn, out DbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = new bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteMultiReaderAsync(command, usageMap, true, behavior, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T Query<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return QueryParse<T>(command, cmd, usageMap, true);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> QueryAsync<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return QueryParseAsync<T>(command, cmd, usageMap, true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> StreamQueryAsync<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return StreamParse<T>(command, cmd, usageMap, true, ct);
        }



        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public int Execute<TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.Execute(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteAsync<TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteAsync(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? ExecuteScalar<T, TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteScalar<T>(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> ExecuteScalarAsync<T, TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteScalarAsync<T>(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the reader of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public DbDataReader ExecuteReader<TObj>(IDbConnection cnn, out IDbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteReader(behavior, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes the reader of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<DbDataReader> ExecuteReaderAsync<TObj>(IDbConnection cnn, out IDbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteReaderAsync(behavior, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public MultiReader ExecuteMultiReader<TObj>(IDbConnection cnn, out IDbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = new bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteMultiReader(command, usageMap, true, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="cmd">The command associated with the reader</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<MultiReader> ExecuteMultiReaderAsync<TObj>(IDbConnection cnn, out IDbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = new bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteMultiReaderAsync(command, usageMap, true, behavior, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T Query<T, TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return QueryParse<T>(command, cmd, usageMap, true);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> QueryAsync<T, TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return QueryParseAsync<T>(command, cmd, usageMap, true, ct);
        }
        #endregion
    }
}
