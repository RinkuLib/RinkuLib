using System.Data;
using System.Data.Common;
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
        public int Execute(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.Execute(command, true);
            return cmd.Execute<NoNeedToCache>(default, true);
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
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteAsync(command, true, ct);
            return cmd.ExecuteAsync<NoNeedToCache>(default, true, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T ExecuteScalar<T>(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteScalar<T, QueryCommand>(command, true);
            return cmd.ExecuteScalar<T, NoNeedToCache>(default, true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> ExecuteScalarAsync<T>(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteScalarAsync<T, QueryCommand>(command, true, ct);
            return cmd.ExecuteScalarAsync<T, NoNeedToCache>(default, true, ct);
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
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteReader(command, behavior);
            return cmd.ExecuteReader<NoNeedToCache>(default, behavior);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public DbDataReader ExecuteReader(DbConnection cnn, object? parametersObj = null, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteReader(command, behavior);
            return cmd.ExecuteReader<NoNeedToCache>(default, behavior);
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
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteReaderAsync(command, behavior, ct);
            return cmd.ExecuteReaderAsync<NoNeedToCache>(default, behavior, ct);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<DbDataReader> ExecuteReaderAsync(DbConnection cnn, object? parametersObj = null, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteReaderAsync(command, behavior, ct);
            return cmd.ExecuteReaderAsync<NoNeedToCache>(default, behavior, ct);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public MultiReader ExecuteMultiReader(DbConnection cnn, object? parametersObj = null, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = new bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReader(command, usageMap, true, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<MultiReader> ExecuteMultiReaderAsync(DbConnection cnn, object? parametersObj = null, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
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
        public T? QueryOne<T>(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryOne<SchemaParser<T>, T>(cache, true);
            return cmd.QueryOne<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public IEnumerable<T> QueryAll<T>(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAll<SchemaParser<T>, T>(cache, true);
            return cmd.QueryAll<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse each rows to return a buffered collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public List<T> QueryAllBuffered<T>(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAllBuffered<SchemaParser<T>, T>(cache, true);
            return cmd.QueryAllBuffered<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }

        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QueryOneAsync<T>(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryOneAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryOneAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryAllAsync<T>(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAllAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryAllAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse each rows to return a buffered collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<List<T>> QueryAllBufferedAsync<T>(DbConnection cnn, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAllBufferedAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryAllBufferedAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }

        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public int Execute(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.Execute(c, parametersObj, t, timeout);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.Execute(command, true);
            return cmd.Execute<NoNeedToCache>(default, true);
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
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.ExecuteAsync(c, parametersObj, t, timeout, ct);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteAsync(command, true, ct);
            return cmd.ExecuteAsync<NoNeedToCache>(default, true, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T ExecuteScalar<T>(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.ExecuteScalar<T>(c, parametersObj, t, timeout);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteScalar<T, QueryCommand>(command, true);
            return cmd.ExecuteScalar<T, NoNeedToCache>(default, true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> ExecuteScalarAsync<T>(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.ExecuteScalarAsync<T>(c, parametersObj, t, timeout, ct);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteScalarAsync<T, QueryCommand>(command, true, ct);
            return cmd.ExecuteScalarAsync<T, NoNeedToCache>(default, true, ct);
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
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteReader(command, behavior);
            return cmd.ExecuteReader<NoNeedToCache>(default, behavior);
        }
        /// <summary>
        /// Executes the reader of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public DbDataReader ExecuteReader(IDbConnection cnn, object? parametersObj = null, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteReader(command, behavior);
            return cmd.ExecuteReader<NoNeedToCache>(default, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public MultiReader ExecuteMultiReader(IDbConnection cnn, object? parametersObj = null, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) {
            var cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = new bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReader(command, usageMap, true, behavior);
        }

        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? QueryOne<T>(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryOne<T>(c, parametersObj, t, timeout);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryOne<SchemaParser<T>, T>(cache, true);
            return cmd.QueryOne<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public IEnumerable<T> QueryAll<T>(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryAll<T>(c, parametersObj, t, timeout);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAll<SchemaParser<T>, T>(cache, true);
            return cmd.QueryAll<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse each rows to return a buffered collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public List<T> QueryAllBuffered<T>(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryAllBuffered<T>(c, parametersObj, t, timeout);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAllBuffered<SchemaParser<T>, T>(cache, true);
            return cmd.QueryAllBuffered<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }

        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QueryOneAsync<T>(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryOneAsync<T>(c, parametersObj, t, timeout, ct);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryOneAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryOneAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryAllAsync<T>(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryAllAsync<T>(c, parametersObj, t, timeout, ct);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAllAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryAllAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse each rows to return a buffered collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<List<T>> QueryAllBufferedAsync<T>(IDbConnection cnn, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryAllBufferedAsync<T>(c, parametersObj, t, timeout, ct);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAllBufferedAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryAllBufferedAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
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
            if (command.NeedToCache(usageMap))
                return cmd.Execute(command, true);
            return cmd.Execute<NoNeedToCache>(default, true);
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
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteAsync(command, true, ct);
            return cmd.ExecuteAsync<NoNeedToCache>(default, true, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T ExecuteScalar<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteScalar<T, QueryCommand>(command, true);
            return cmd.ExecuteScalar<T, NoNeedToCache>(default, true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> ExecuteScalarAsync<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteScalarAsync<T, QueryCommand>(command, true, ct);
            return cmd.ExecuteScalarAsync<T, NoNeedToCache>(default, true, ct);
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
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteReader(command, behavior);
            return cmd.ExecuteReader<NoNeedToCache>(default, behavior);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public DbDataReader ExecuteReader<TObj>(DbConnection cnn, TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteReader(command, behavior);
            return cmd.ExecuteReader<NoNeedToCache>(default, behavior);
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
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteReaderAsync(command, behavior, ct);
            return cmd.ExecuteReaderAsync<NoNeedToCache>(default, behavior, ct);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<DbDataReader> ExecuteReaderAsync<TObj>(DbConnection cnn, TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteReaderAsync(command, behavior, ct);
            return cmd.ExecuteReaderAsync<NoNeedToCache>(default, behavior, ct);
        }

        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public MultiReader ExecuteMultiReader<TObj>(DbConnection cnn, TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = new bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReader(command, usageMap, true, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<MultiReader> ExecuteMultiReaderAsync<TObj>(DbConnection cnn, TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
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
        public T? QueryOne<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryOne<SchemaParser<T>, T>(cache, true);
            return cmd.QueryOne<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public IEnumerable<T> QueryAll<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAll<SchemaParser<T>, T>(cache, true);
            return cmd.QueryAll<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse each rows to return a buffered collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public List<T> QueryAllBuffered<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAllBuffered<SchemaParser<T>, T>(cache, true);
            return cmd.QueryAllBuffered<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }

        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QueryOneAsync<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryOneAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryOneAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryAllAsync<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAllAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryAllAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse each rows to return a buffered collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<List<T>> QueryAllBufferedAsync<T, TObj>(DbConnection cnn, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAllBufferedAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryAllBufferedAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }

        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public int Execute<TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.Execute<TObj>(c, parametersObj, t, timeout);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.Execute(command, true);
            return cmd.Execute<NoNeedToCache>(default, true);
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
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.ExecuteAsync<TObj>(c, parametersObj, t, timeout, ct);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteAsync(command, true, ct);
            return cmd.ExecuteAsync<NoNeedToCache>(default, true, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T ExecuteScalar<T, TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.ExecuteScalar<T, TObj>(c, parametersObj, t, timeout);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteScalar<T, QueryCommand>(command, true);
            return cmd.ExecuteScalar<T, NoNeedToCache>(default, true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> ExecuteScalarAsync<T, TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.ExecuteScalarAsync<T, TObj>(c, parametersObj, t, timeout, ct);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteScalarAsync<T, QueryCommand>(command, true, ct);
            return cmd.ExecuteScalarAsync<T, NoNeedToCache>(default, true, ct);
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
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteReader(command, behavior);
            return cmd.ExecuteReader<NoNeedToCache>(default, behavior);
        }
        /// <summary>
        /// Executes the reader of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public DbDataReader ExecuteReader<TObj>(IDbConnection cnn, TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteReader(command, behavior);
            return cmd.ExecuteReader<NoNeedToCache>(default, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public MultiReader ExecuteMultiReader<TObj>(IDbConnection cnn, TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = new bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReader(command, usageMap, true, behavior);
        }

        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? QueryOne<T, TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryOne<T, TObj>(c, parametersObj, t, timeout);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryOne<SchemaParser<T>, T>(cache, true);
            return cmd.QueryOne<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public IEnumerable<T> QueryAll<T, TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryAll<T, TObj>(c, parametersObj, t, timeout);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAll<SchemaParser<T>, T>(cache, true);
            return cmd.QueryAll<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse each rows to return a buffered collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public List<T> QueryAllBuffered<T, TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryAllBuffered<T, TObj>(c, parametersObj, t, timeout);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAllBuffered<SchemaParser<T>, T>(cache, true);
            return cmd.QueryAllBuffered<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }

        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QueryOneAsync<T, TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryOneAsync<T, TObj>(c, parametersObj, t, timeout, ct);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryOneAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryOneAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryAllAsync<T, TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryAllAsync<T, TObj>(c, parametersObj, t, timeout, ct);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAllAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryAllAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse each rows to return a buffered collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<List<T>> QueryAllBufferedAsync<T, TObj>(IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryAllBufferedAsync<T, TObj>(c, parametersObj, t, timeout, ct);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAllBufferedAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryAllBufferedAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
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
            if (command.NeedToCache(usageMap))
                return cmd.Execute(command, true);
            return cmd.Execute<NoNeedToCache>(default, true);
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
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteAsync(command, true, ct);
            return cmd.ExecuteAsync<NoNeedToCache>(default, true, ct);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T ExecuteScalar<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteScalar<T, QueryCommand>(command, true);
            return cmd.ExecuteScalar<T, NoNeedToCache>(default, true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> ExecuteScalarAsync<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteScalarAsync<T, QueryCommand>(command, true, ct);
            return cmd.ExecuteScalarAsync<T, NoNeedToCache>(default, true, ct);
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
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteReader(command, behavior);
            return cmd.ExecuteReader<NoNeedToCache>(default, behavior);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public DbDataReader ExecuteReader<TObj>(DbConnection cnn, ref TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteReader(command, behavior);
            return cmd.ExecuteReader<NoNeedToCache>(default, behavior);
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
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteReaderAsync(command, behavior, ct);
            return cmd.ExecuteReaderAsync<NoNeedToCache>(default, behavior, ct);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<DbDataReader> ExecuteReaderAsync<TObj>(DbConnection cnn, ref TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteReaderAsync(command, behavior, ct);
            return cmd.ExecuteReaderAsync<NoNeedToCache>(default, behavior, ct);
        }
        /// <summary>
        /// Executes the <see cref = "MultiReader" /> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public MultiReader ExecuteMultiReader<TObj>(DbConnection cnn, ref TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = new bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReader(command, usageMap, true, behavior);
        }
        /// <summary>
        /// Executes the <see cref = "MultiReader" /> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<MultiReader> ExecuteMultiReaderAsync<TObj>(DbConnection cnn, ref TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
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
        public T? QueryOne<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryOne<SchemaParser<T>, T>(cache, true);
            return cmd.QueryOne<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public IEnumerable<T> QueryAll<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAll<SchemaParser<T>, T>(cache, true);
            return cmd.QueryAll<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="DbCommand"/> and parse each rows to return a buffered collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public List<T> QueryAllBuffered<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAllBuffered<SchemaParser<T>, T>(cache, true);
            return cmd.QueryAllBuffered<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }

        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QueryOneAsync<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryOneAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryOneAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryAllAsync<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAllAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryAllAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse each rows to return a buffered collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="DbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<List<T>> QueryAllBufferedAsync<T, TObj>(DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAllBufferedAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryAllBufferedAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }

        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public int Execute<TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.Execute<TObj>(c, ref parametersObj, t, timeout);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.Execute(command, true);
            return cmd.Execute<NoNeedToCache>(default, true);
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
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.ExecuteAsync<TObj>(c, ref parametersObj, t, timeout, ct);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteAsync(command, true, ct);
            return cmd.ExecuteAsync<NoNeedToCache>(default, true, ct);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T ExecuteScalar<T, TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.ExecuteScalar<T, TObj>(c, ref parametersObj, t, timeout);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteScalar<T, QueryCommand>(command, true);
            return cmd.ExecuteScalar<T, NoNeedToCache>(default, true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> ExecuteScalarAsync<T, TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.ExecuteScalarAsync<T, TObj>(c, ref parametersObj, t, timeout, ct);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteScalarAsync<T, QueryCommand>(command, true, ct);
            return cmd.ExecuteScalarAsync<T, NoNeedToCache>(default, true, ct);
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
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteReader(command, behavior);
            return cmd.ExecuteReader<NoNeedToCache>(default, behavior);
        }
        /// <summary>
        /// Executes the reader of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public DbDataReader ExecuteReader<TObj>(IDbConnection cnn, ref TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.NeedToCache(usageMap))
                return cmd.ExecuteReader(command, behavior);
            return cmd.ExecuteReader<NoNeedToCache>(default, behavior);
        }
        /// <summary>
        /// Executes the <see cref = "MultiReader" /> of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public MultiReader ExecuteMultiReader<TObj>(IDbConnection cnn, ref TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            var cmd = cnn.GetCommand(transaction, timeout);
            bool[] usageMap = new bool[command.Mapper.Count];
            command.SetCommand(cmd, parametersObj, usageMap);
            return cmd.ExecuteMultiReader(command, usageMap, true, behavior);
        }

        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public T? QueryOne<T, TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryOne<T, TObj>(c, ref parametersObj, t, timeout);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryOne<SchemaParser<T>, T>(cache, true);
            return cmd.QueryOne<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public IEnumerable<T> QueryAll<T, TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryAll<T, TObj>(c, ref parametersObj, t, timeout);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAll<SchemaParser<T>, T>(cache, true);
            return cmd.QueryAll<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }
        /// <summary>
        /// Executes a <see cref="IDbCommand"/> and parse each rows to return a buffered collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        public List<T> QueryAllBuffered<T, TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryAllBuffered<T, TObj>(c, ref parametersObj, t, timeout);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAllBuffered<SchemaParser<T>, T>(cache, true);
            return cmd.QueryAllBuffered<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true);
        }

        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QueryOneAsync<T, TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryOneAsync<T, TObj>(c, ref parametersObj, t, timeout, ct);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryOneAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryOneAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryAllAsync<T, TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryAllAsync<T, TObj>(c, ref parametersObj, t, timeout, ct);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAllAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryAllAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }
        /// <summary>
        /// Asynchronously executes a <see cref="IDbCommand"/> and parse each rows to return a buffered collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cnn">The connection to execute on</param>
        /// <param name="parametersObj">The current state object for the <see cref="IDbCommand"/> creation</param>
        /// <param name="transaction">The transaction to execute on</param>
        /// <param name="timeout">The timeout for the command</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<List<T>> QueryAllBufferedAsync<T, TObj>(IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull {
            if (cnn is DbConnection c && transaction is DbTransaction t)
                return command.QueryAllBufferedAsync<T, TObj>(c, ref parametersObj, t, timeout, ct);
            var cmd = cnn.GetCommand(transaction, timeout);
            Span<bool> usageMap = stackalloc bool[command.Mapper.Count];
            command.SetCommand(cmd, ref parametersObj, usageMap);
            if (command.TryGetCache<T>(usageMap, out var cache))
                return cmd.QueryAllBufferedAsync<SchemaParser<T>, T>(cache, true, ct);
            return cmd.QueryAllBufferedAsync<ParsingCacheToMake<T>, T>(new(command, cache, usageMap.GetFalseIndexes()), true, ct);
        }
        #endregion
    }
}
