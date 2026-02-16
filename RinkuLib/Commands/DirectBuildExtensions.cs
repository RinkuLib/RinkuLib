using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using RinkuLib.Queries;

namespace RinkuLib.Commands; 
/// <summary>Extensions to direcly call a <see cref="QueryCommand"/> with an obj</summary>
public static class DirectBuildExtensions {
    /// <summary>
    /// Extension to get the indexes of the false items
    /// </summary>
    public static int[] GetFalseIndexes(this Span<bool> usageMap) {
        if (usageMap.Length == 0)
            return [];
        Span<int> buffer = usageMap.Length <= 256
            ? stackalloc int[usageMap.Length]
            : new int[usageMap.Length];

        int count = 0;
        for (int i = 0; i < usageMap.Length; i++)
            if (!usageMap[i])
                buffer[count++] = i;
        if (count == 0)
            return [];
        return buffer[..count].ToArray();
    }
    /// <summary>
    /// Extension to get the indexes of the false items
    /// </summary>
    public static int[] GetFalseIndexes(this object?[] variables) {
        if (variables.Length == 0)
            return [];
        Span<int> buffer = variables.Length <= 256
            ? stackalloc int[variables.Length]
            : new int[variables.Length];

        int count = 0;
        for (int i = 0; i < variables.Length; i++)
            if (variables[i] is null)
                buffer[count++] = i;
        if (count == 0)
            return [];
        return buffer[..count].ToArray();
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
        public int ExecuteQuery(DbConnection cnn, object parametersObj, DbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteQuery(command, true);
            return cmd.ExecuteQuery<NoNeedToCache>(default, true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteQueryAsync(DbConnection cnn, object parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteQueryAsync(command, true, ct);
            return cmd.ExecuteQueryAsync<NoNeedToCache>(default, true, ct);
        }

        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? QuerySingle<T>(DbConnection cnn, object parametersObj, DbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QuerySingle<SchemaParser<T>, T>(cache, true);
            return cmd.QuerySingle<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public IEnumerable<T> QueryMultiple<T>(DbConnection cnn, object parametersObj, DbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryMultiple<SchemaParser<T>, T>(cache, true);
            return cmd.QueryMultiple<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }

        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QuerySingleAsync<T>(DbConnection cnn, object parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QuerySingleAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QuerySingleAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(DbConnection cnn, object parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryMultipleAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryMultipleAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }

        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public int ExecuteQuery(IDbConnection cnn, object parametersObj, IDbTransaction? transaction = null, int? timeout = null) {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.ExecuteQuery(c, parametersObj, t, timeout);
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteQuery(command, true);
            return cmd.ExecuteQuery<NoNeedToCache>(default, true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteQueryAsync(IDbConnection cnn, object parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.ExecuteQueryAsync(c, parametersObj, t, timeout, ct);
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteQueryAsync(command, true, ct);
            return cmd.ExecuteQueryAsync<NoNeedToCache>(default, true, ct);
        }

        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? QuerySingle<T>(IDbConnection cnn, object parametersObj, IDbTransaction? transaction = null, int? timeout = null) {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QuerySingle<T>(c, parametersObj, t, timeout);
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QuerySingle<SchemaParser<T>, T>(cache, true);
            return cmd.QuerySingle<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public IEnumerable<T> QueryMultiple<T>(IDbConnection cnn, object parametersObj, IDbTransaction? transaction = null, int? timeout = null) {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryMultiple<T>(c, parametersObj, t, timeout);
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryMultiple<SchemaParser<T>, T>(cache, true);
            return cmd.QueryMultiple<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }

        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QuerySingleAsync<T>(IDbConnection cnn, object parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QuerySingleAsync<T>(c, parametersObj, t, timeout, ct);
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QuerySingleAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QuerySingleAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(IDbConnection cnn, object parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryMultipleAsync<T>(c, parametersObj, t, timeout, ct);
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryMultipleAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryMultipleAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
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
        public int ExecuteQuery<TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteQuery(command, true);
            return cmd.ExecuteQuery<NoNeedToCache>(default, true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteQueryAsync<TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteQueryAsync(command, true, ct);
            return cmd.ExecuteQueryAsync<NoNeedToCache>(default, true, ct);
        }

        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? QuerySingle<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QuerySingle<SchemaParser<T>, T>(cache, true);
            return cmd.QuerySingle<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public IEnumerable<T> QueryMultiple<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryMultiple<SchemaParser<T>, T>(cache, true);
            return cmd.QueryMultiple<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }

        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QuerySingleAsync<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QuerySingleAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QuerySingleAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryMultipleAsync<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryMultipleAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryMultipleAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }

        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public int ExecuteQuery<TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.ExecuteQuery<TObj>(c, parametersObj, t, timeout);
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteQuery(command, true);
            return cmd.ExecuteQuery<NoNeedToCache>(default, true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteQueryAsync<TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.ExecuteQueryAsync<TObj>(c, parametersObj, t, timeout, ct);
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteQueryAsync(command, true, ct);
            return cmd.ExecuteQueryAsync<NoNeedToCache>(default, true, ct);
        }

        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? QuerySingle<T, TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QuerySingle<T, TObj>(c, parametersObj, t, timeout);
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QuerySingle<SchemaParser<T>, T>(cache, true);
            return cmd.QuerySingle<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public IEnumerable<T> QueryMultiple<T, TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryMultiple<T, TObj>(c, parametersObj, t, timeout);
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryMultiple<SchemaParser<T>, T>(cache, true);
            return cmd.QueryMultiple<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }

        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QuerySingleAsync<T, TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QuerySingleAsync<T, TObj>(c, parametersObj, t, timeout, ct);
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QuerySingleAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QuerySingleAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryMultipleAsync<T, TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryMultipleAsync<T, TObj>(c, parametersObj, t, timeout, ct);
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryMultipleAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryMultipleAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
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
        public int ExecuteQuery<TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteQuery(command, true);
            return cmd.ExecuteQuery<NoNeedToCache>(default, true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteQueryAsync<TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteQueryAsync(command, true, ct);
            return cmd.ExecuteQueryAsync<NoNeedToCache>(default, true, ct);
        }

        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? QuerySingle<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QuerySingle<SchemaParser<T>, T>(cache, true);
            return cmd.QuerySingle<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public IEnumerable<T> QueryMultiple<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryMultiple<SchemaParser<T>, T>(cache, true);
            return cmd.QueryMultiple<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }

        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QuerySingleAsync<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QuerySingleAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QuerySingleAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryMultipleAsync<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryMultipleAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryMultipleAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }

        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public int ExecuteQuery<TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.ExecuteQuery<TObj>(c, ref parametersObj, t, timeout);
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteQuery(command, true);
            return cmd.ExecuteQuery<NoNeedToCache>(default, true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteQueryAsync<TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.ExecuteQueryAsync<TObj>(c, ref parametersObj, t, timeout, ct);
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteQueryAsync(command, true, ct);
            return cmd.ExecuteQueryAsync<NoNeedToCache>(default, true, ct);
        }

        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? QuerySingle<T, TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QuerySingle<T, TObj>(c, ref parametersObj, t, timeout);
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QuerySingle<SchemaParser<T>, T>(cache, true);
            return cmd.QuerySingle<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public IEnumerable<T> QueryMultiple<T, TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryMultiple<T, TObj>(c, ref parametersObj, t, timeout);
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryMultiple<SchemaParser<T>, T>(cache, true);
            return cmd.QueryMultiple<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }

        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QuerySingleAsync<T, TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QuerySingleAsync<T, TObj>(c, ref parametersObj, t, timeout, ct);
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QuerySingleAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QuerySingleAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryMultipleAsync<T, TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryMultipleAsync<T, TObj>(c, ref parametersObj, t, timeout, ct);
            var cmd = cnn.CreateCommand();
            if (transaction is not null)
                cmd.Transaction = transaction;
            if (timeout.HasValue)
                cmd.CommandTimeout = timeout.Value;
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryMultipleAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryMultipleAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }
        #endregion
    }
}
