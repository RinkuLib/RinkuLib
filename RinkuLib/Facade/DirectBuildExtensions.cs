using System.Data;
using System.Data.Common;
using Rinku.Querying;

namespace Rinku; 
/// <summary>
/// Runs a <see cref="QueryCommand"/> with values from a parameter object.
/// Use an overload with an <c>out</c> command when output values must be read after execution.
/// </summary>
public static class DirectBuildExtensions {
    internal static bool[] ToBoolArray(this object?[] variables) {
        var arr = new bool[variables.Length];
        for (int i = 0; i < variables.Length; i++)
            if (variables[i] is not null)
                arr[i] = true;
        return arr;
    }
    private static T QueryParse<T>(QueryCommand command, DbCommand cmd, Span<bool> usageMap, bool disposeCommand) {
        if (command.TryGetCachedParser<T>(usageMap, out var parser))
            return parser.Query(cmd, disposeCommand);
        else if (parser is not null)
            return parser.Query(cmd, command, disposeCommand);
        return cmd.Query(new LinkerQueryCommandWithParser<T>(command, usageMap.ToArray()), disposeCommand);
    }
    private static T QueryParse<T>(QueryCommand command, IDbCommand cmd, Span<bool> usageMap, bool disposeCommand) {
        if (command.TryGetCachedParser<T>(usageMap, out var parser))
            return parser.Query(cmd, disposeCommand);
        else if (parser is not null)
            return parser.Query(cmd, command, disposeCommand);
        return cmd.Query(new LinkerQueryCommandWithParser<T>(command, usageMap.ToArray()), disposeCommand);
    }
    private static Task<T> QueryParseAsync<T>(QueryCommand command, DbCommand cmd, Span<bool> usageMap, bool disposeCommand, CancellationToken ct) {
        if (command.TryGetCachedParser<T>(usageMap, out var parser))
            return parser.QueryAsync(cmd, disposeCommand, ct);
        else if (parser is not null)
            return parser.QueryAsync(cmd, command, disposeCommand, ct);
        return cmd.QueryAsync(new LinkerQueryCommandWithParser<T>(command, usageMap.ToArray()), disposeCommand, ct);
    }
    private static Task<T> QueryParseAsync<T>(QueryCommand command, IDbCommand cmd, Span<bool> usageMap, bool disposeCommand, CancellationToken ct) {
        if (command.TryGetCachedParser<T>(usageMap, out var parser))
            return parser.QueryAsync(cmd, disposeCommand, ct);
        else if (parser is not null)
            return parser.QueryAsync(cmd, command, disposeCommand, ct);
        return cmd.QueryAsync(new LinkerQueryCommandWithParser<T>(command, usageMap.ToArray()), disposeCommand, ct);
    }
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
        /// Executes a <see cref="DbCommand"/> and returns the number of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public int Execute(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.Execute(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and returns the number of affected rows, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command to keep for output values and then dispose.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public int Execute(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.Execute(false, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and returns the number of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<int> ExecuteAsync(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteAsync(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and returns the number of affected rows, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command to keep for output values and then dispose.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<int> ExecuteAsync(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteAsync(false, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and returns the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public T? ExecuteScalar<T>(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalar<T>(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and returns the scalar value, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command to keep for output values and then dispose.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public T? ExecuteScalar<T>(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalar<T>(false, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and returns the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<T?> ExecuteScalarAsync<T>(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalarAsync<T>(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and returns the scalar value, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command to keep for output values and then dispose.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<T?> ExecuteScalarAsync<T>(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalarAsync<T>(false, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public DbDataReader ExecuteReader(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteReader(behavior, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<DbDataReader> ExecuteReaderAsync(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteReaderAsync(behavior, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public MultiReader ExecuteMultiReader(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = command.CreateUsageMap();
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReader(command, usageMap, false, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/>, which owns the command and disposes it with itself, so there is nothing to hold.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public MultiReader ExecuteMultiReader(DbConnection cnn, object? parametersObj = null, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = command.CreateUsageMap();
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReader(command, usageMap, true, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<MultiReader> ExecuteMultiReaderAsync(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = command.CreateUsageMap();
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReaderAsync(command, usageMap, false, behavior, ct);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/>, which owns the command and disposes it with itself, so there is nothing to hold.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<MultiReader> ExecuteMultiReaderAsync(DbConnection cnn, object? parametersObj = null, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = command.CreateUsageMap();
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReaderAsync(command, usageMap, true, behavior, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and reads the result as <typeparamref name="T"/>. The requested type controls the result.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public T Query<T>(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParse<T>(command, cmd, usageMap, true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parses its result as <typeparamref name="T"/> while keeping the command alive. the result shape defines zero-row and row-count behavior.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command to keep for output values and then dispose.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public T Query<T>(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParse<T>(command, cmd, usageMap, false);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and reads the result as <typeparamref name="T"/>. The requested type controls the result.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<T> QueryAsync<T>(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParseAsync<T>(command, cmd, usageMap, true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parses its result as <typeparamref name="T"/> while keeping the command alive. the result shape defines zero-row and row-count behavior.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command to keep for output values and then dispose.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<T> QueryAsync<T>(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParseAsync<T>(command, cmd, usageMap, false, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and streams its rows as <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public IAsyncEnumerable<T> StreamQueryAsync<T>(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return StreamParse<T>(command, cmd, usageMap, true, ct);
        }
        /// <summary>
        /// Streams rows asynchronously and keeps the command available to the caller.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command, kept for the caller to read (e.g. output parameters, filled once enumeration completes) and dispose</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public IAsyncEnumerable<T> StreamQueryAsync<T>(DbConnection cnn, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return StreamParse<T>(command, cmd, usageMap, false, ct);
        }



        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and returns the number of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public int Execute(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.Execute(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and returns the number of affected rows, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command to keep for output values and then dispose.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public int Execute(IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.Execute(false, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and returns the number of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<int> ExecuteAsync(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteAsync(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and returns the number of affected rows, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command to keep for output values and then dispose.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<int> ExecuteAsync(IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteAsync(false, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and returns the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public T? ExecuteScalar<T>(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalar<T>(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and returns the scalar value, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command to keep for output values and then dispose.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public T? ExecuteScalar<T>(IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalar<T>(false, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and returns the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<T?> ExecuteScalarAsync<T>(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalarAsync<T>(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and returns the scalar value, keeping the command alive.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command to keep for output values and then dispose.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<T?> ExecuteScalarAsync<T>(IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalarAsync<T>(false, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the reader of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public DbDataReader ExecuteReader(IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteReader(behavior, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes the reader of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<DbDataReader> ExecuteReaderAsync(IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteReaderAsync(behavior, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public MultiReader ExecuteMultiReader(IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = command.CreateUsageMap();
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReader(command, usageMap, false, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<MultiReader> ExecuteMultiReaderAsync(IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = command.CreateUsageMap();
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReaderAsync(command, usageMap, false, behavior, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and reads the result as <typeparamref name="T"/>. The requested type controls the result.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public T Query<T>(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParse<T>(command, cmd, usageMap, true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parses its result as <typeparamref name="T"/> while keeping the command alive. the result shape defines zero-row and row-count behavior.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command to keep for output values and then dispose.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public T Query<T>(IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParse<T>(command, cmd, usageMap, false);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and reads the result as <typeparamref name="T"/>. The requested type controls the result.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<T> QueryAsync<T>(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParseAsync<T>(command, cmd, usageMap, true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parses its result as <typeparamref name="T"/> while keeping the command alive. the result shape defines zero-row and row-count behavior.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command to keep for output values and then dispose.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<T> QueryAsync<T>(IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParseAsync<T>(command, cmd, usageMap, false, ct);
        }
        #endregion
        #region generic param

        /// <summary>
        /// Executes a <see cref="DbCommand"/> and returns the number of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public int Execute<TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.Execute(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and returns the number of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<int> ExecuteAsync<TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteAsync(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and returns the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public T? ExecuteScalar<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalar<T>(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and returns the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<T?> ExecuteScalarAsync<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalarAsync<T>(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public DbDataReader ExecuteReader<TObj>(DbConnection cnn, out DbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteReader(behavior, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<DbDataReader> ExecuteReaderAsync<TObj>(DbConnection cnn, out DbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteReaderAsync(behavior, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public MultiReader ExecuteMultiReader<TObj>(DbConnection cnn, out DbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = command.CreateUsageMap();
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReader(command, usageMap, false, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<MultiReader> ExecuteMultiReaderAsync<TObj>(DbConnection cnn, out DbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = command.CreateUsageMap();
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReaderAsync(command, usageMap, false, behavior, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and reads the result as <typeparamref name="T"/>. The requested type controls the result.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public T Query<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParse<T>(command, cmd, usageMap, true);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and reads the result as <typeparamref name="T"/>. The requested type controls the result.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<T> QueryAsync<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParseAsync<T>(command, cmd, usageMap, true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and streams its rows as <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public IAsyncEnumerable<T> StreamQueryAsync<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return StreamParse<T>(command, cmd, usageMap, true, ct);
        }



        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and returns the number of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public int Execute<TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.Execute(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and returns the number of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<int> ExecuteAsync<TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteAsync(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and returns the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public T? ExecuteScalar<T, TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalar<T>(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and returns the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<T?> ExecuteScalarAsync<T, TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteScalarAsync<T>(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the reader of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public DbDataReader ExecuteReader<TObj>(IDbConnection cnn, out IDbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteReader(behavior, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes the reader of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<DbDataReader> ExecuteReaderAsync<TObj>(IDbConnection cnn, out IDbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteReaderAsync(behavior, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public MultiReader ExecuteMultiReader<TObj>(IDbConnection cnn, out IDbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = command.CreateUsageMap();
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReader(command, usageMap, false, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<MultiReader> ExecuteMultiReaderAsync<TObj>(IDbConnection cnn, out IDbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = command.CreateUsageMap();
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReaderAsync(command, usageMap, false, behavior, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and reads the result as <typeparamref name="T"/>. The requested type controls the result.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public T Query<T, TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParse<T>(command, cmd, usageMap, true);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and reads the result as <typeparamref name="T"/>. The requested type controls the result.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<T> QueryAsync<T, TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return QueryParseAsync<T>(command, cmd, usageMap, true, ct);
        }
        #endregion
        #region ref generic param

        /// <summary>
        /// Executes a <see cref="DbCommand"/> and returns the number of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public int Execute<TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.Execute(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and returns the number of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<int> ExecuteAsync<TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteAsync(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and returns the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public T? ExecuteScalar<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteScalar<T>(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and returns the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<T?> ExecuteScalarAsync<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteScalarAsync<T>(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public DbDataReader ExecuteReader<TObj>(DbConnection cnn, out DbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteReader(behavior, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<DbDataReader> ExecuteReaderAsync<TObj>(DbConnection cnn, out DbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteReaderAsync(behavior, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public MultiReader ExecuteMultiReader<TObj>(DbConnection cnn, out DbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = command.CreateUsageMap();
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteMultiReader(command, usageMap, false, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<MultiReader> ExecuteMultiReaderAsync<TObj>(DbConnection cnn, out DbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = command.CreateUsageMap();
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteMultiReaderAsync(command, usageMap, false, behavior, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and reads the result as <typeparamref name="T"/>. The requested type controls the result.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public T Query<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return QueryParse<T>(command, cmd, usageMap, true);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and reads the result as <typeparamref name="T"/>. The requested type controls the result.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<T> QueryAsync<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return QueryParseAsync<T>(command, cmd, usageMap, true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and streams its rows as <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public IAsyncEnumerable<T> StreamQueryAsync<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return StreamParse<T>(command, cmd, usageMap, true, ct);
        }



        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and returns the number of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public int Execute<TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.Execute(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and returns the number of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<int> ExecuteAsync<TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteAsync(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and returns the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public T? ExecuteScalar<T, TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteScalar<T>(true, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and returns the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<T?> ExecuteScalarAsync<T, TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteScalarAsync<T>(true, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the reader of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public DbDataReader ExecuteReader<TObj>(IDbConnection cnn, out IDbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteReader(behavior, command.NeedToCache(usageMap) ? command : null);
        }
        /// <summary>
        /// Executes the reader of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<DbDataReader> ExecuteReaderAsync<TObj>(IDbConnection cnn, out IDbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteReaderAsync(behavior, command.NeedToCache(usageMap) ? command : null, ct);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public MultiReader ExecuteMultiReader<TObj>(IDbConnection cnn, out IDbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = command.CreateUsageMap();
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteMultiReader(command, usageMap, false, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="cmd">The command that owns the reader.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<MultiReader> ExecuteMultiReaderAsync<TObj>(IDbConnection cnn, out IDbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = command.CreateUsageMap();
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return cmd.ExecuteMultiReaderAsync(command, usageMap, false, behavior, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and reads the result as <typeparamref name="T"/>. The requested type controls the result.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        public T Query<T, TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return QueryParse<T>(command, cmd, usageMap, true);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and reads the result as <typeparamref name="T"/>. The requested type controls the result.
        /// </summary>
        /// <param name="cnn">The connection to use.</param>
        /// <param name="parametersObj">The object whose members supply parameter values.</param>
        /// <param name="transaction">The transaction to use.</param>
        /// <param name="timeout">The command timeout in seconds.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<T> QueryAsync<T, TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            return QueryParseAsync<T>(command, cmd, usageMap, true, ct);
        }

        /// <summary>Runs the command and reads the result in the shape selected by <paramref name="resultType"/>.</summary>
        public object Query(Type resultType, DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null)
            => command.QueryRuntime(resultType, cnn, parametersObj, transaction, timeout);
        /// <inheritdoc/>
        public object Query(Type resultType, DbConnection cnn, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null)
            => command.QueryRuntime(resultType, cnn, out cmd, parametersObj, transaction, timeout);
        /// <inheritdoc/>
        public Task<object> QueryAsync(Type resultType, DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => command.QueryRuntimeAsync(resultType, cnn, parametersObj, transaction, timeout, ct);
        /// <inheritdoc/>
        public Task<object> QueryAsync(Type resultType, DbConnection cnn, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => command.QueryRuntimeAsync(resultType, cnn, out cmd, parametersObj, transaction, timeout, ct);
        /// <inheritdoc/>
        public object Query(Type resultType, IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null)
            => command.QueryRuntime(resultType, cnn, parametersObj, transaction, timeout);
        /// <inheritdoc/>
        public object Query(Type resultType, IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null)
            => command.QueryRuntime(resultType, cnn, out cmd, parametersObj, transaction, timeout);
        /// <inheritdoc/>
        public Task<object> QueryAsync(Type resultType, IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => command.QueryRuntimeAsync(resultType, cnn, parametersObj, transaction, timeout, ct);
        /// <inheritdoc/>
        public Task<object> QueryAsync(Type resultType, IDbConnection cnn, out IDbCommand cmd, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => command.QueryRuntimeAsync(resultType, cnn, out cmd, parametersObj, transaction, timeout, ct);
        /// <inheritdoc/>
        public object Query<TObj>(Type resultType, DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull
            => command.QueryRuntime(resultType, cnn, parametersObj, transaction, timeout);
        /// <inheritdoc/>
        public Task<object> QueryAsync<TObj>(Type resultType, DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull
            => command.QueryRuntimeAsync(resultType, cnn, parametersObj, transaction, timeout, ct);
        /// <inheritdoc/>
        public object Query<TObj>(Type resultType, IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull
            => command.QueryRuntime(resultType, cnn, parametersObj, transaction, timeout);
        /// <inheritdoc/>
        public Task<object> QueryAsync<TObj>(Type resultType, IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull
            => command.QueryRuntimeAsync(resultType, cnn, parametersObj, transaction, timeout, ct);
        /// <inheritdoc/>
        public object Query<TObj>(Type resultType, DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull
            => command.QueryRuntime(resultType, cnn, ref parametersObj, transaction, timeout);
        /// <inheritdoc/>
        public Task<object> QueryAsync<TObj>(Type resultType, DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull
            => command.QueryRuntimeAsync(resultType, cnn, ref parametersObj, transaction, timeout, ct);
        /// <inheritdoc/>
        public object Query<TObj>(Type resultType, IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull
            => command.QueryRuntime(resultType, cnn, ref parametersObj, transaction, timeout);
        /// <inheritdoc/>
        public Task<object> QueryAsync<TObj>(Type resultType, IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull
            => command.QueryRuntimeAsync(resultType, cnn, ref parametersObj, transaction, timeout, ct);

        /// <summary>Reads multiple result sets and returns an owning reader that disposes the command.</summary>
        public MultiReader ExecuteMultiReader(IDbConnection cnn, object? parametersObj = null, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout); var usage = command.CreateUsageMap(); command.SetCommand(cmd, parametersObj, usage);
            return cmd.ExecuteMultiReader(command, usage, true, behavior);
        }
        /// <inheritdoc/>
        public Task<MultiReader> ExecuteMultiReaderAsync(IDbConnection cnn, object? parametersObj = null, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout); var usage = command.CreateUsageMap(); command.SetCommand(cmd, parametersObj, usage);
            return cmd.ExecuteMultiReaderAsync(command, usage, true, behavior, ct);
        }
        /// <inheritdoc/>
        public MultiReader ExecuteMultiReader<TObj>(DbConnection cnn, TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout); var usage = command.CreateUsageMap(); command.SetCommand(cmd, parametersObj, usage);
            return cmd.ExecuteMultiReader(command, usage, true, behavior);
        }
        /// <inheritdoc/>
        public Task<MultiReader> ExecuteMultiReaderAsync<TObj>(DbConnection cnn, TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout); var usage = command.CreateUsageMap(); command.SetCommand(cmd, parametersObj, usage);
            return cmd.ExecuteMultiReaderAsync(command, usage, true, behavior, ct);
        }
        /// <inheritdoc/>
        public MultiReader ExecuteMultiReader<TObj>(IDbConnection cnn, TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout); var usage = command.CreateUsageMap(); command.SetCommand(cmd, parametersObj, usage);
            return cmd.ExecuteMultiReader(command, usage, true, behavior);
        }
        /// <inheritdoc/>
        public Task<MultiReader> ExecuteMultiReaderAsync<TObj>(IDbConnection cnn, TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout); var usage = command.CreateUsageMap(); command.SetCommand(cmd, parametersObj, usage);
            return cmd.ExecuteMultiReaderAsync(command, usage, true, behavior, ct);
        }
        /// <inheritdoc/>
        public MultiReader ExecuteMultiReader<TObj>(DbConnection cnn, ref TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout); var usage = command.CreateUsageMap(); command.SetCommand(cmd, ref parametersObj, usage);
            return cmd.ExecuteMultiReader(command, usage, true, behavior);
        }
        /// <inheritdoc/>
        public Task<MultiReader> ExecuteMultiReaderAsync<TObj>(DbConnection cnn, ref TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout); var usage = command.CreateUsageMap(); command.SetCommand(cmd, ref parametersObj, usage);
            return cmd.ExecuteMultiReaderAsync(command, usage, true, behavior, ct);
        }
        /// <inheritdoc/>
        public MultiReader ExecuteMultiReader<TObj>(IDbConnection cnn, ref TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout); var usage = command.CreateUsageMap(); command.SetCommand(cmd, ref parametersObj, usage);
            return cmd.ExecuteMultiReader(command, usage, true, behavior);
        }
        /// <inheritdoc/>
        public Task<MultiReader> ExecuteMultiReaderAsync<TObj>(IDbConnection cnn, ref TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout); var usage = command.CreateUsageMap(); command.SetCommand(cmd, ref parametersObj, usage);
            return cmd.ExecuteMultiReaderAsync(command, usage, true, behavior, ct);
        }
        #endregion
    }
}
