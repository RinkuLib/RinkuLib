using System.Data;
using System.Data.Common;
using RinkuLib.Queries;

namespace RinkuLib.Commands;

/// <summary>
/// Extensions on <see cref="QueryBuilderCommand{T}"/>
/// </summary>
public static class QueryBuilderCommandExtensions {
    /// <summary>Transform into a bool array where true means a not null value</summary>
    public static bool[] ToBoolArr(this object?[] vars) {
        var res = new bool[vars.Length];
        for (int i = 0; i < vars.Length; i++)
            res[i] = vars[i] is not null;
        return res;
    }
    extension(QueryBuilderCommand<DbCommand> builder) {
        /// <summary>
        /// Executes the managed <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        public int ExecuteQuery() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.NeedToCache(vars))
                return cmd.ExecuteQuery(command, false);
            return cmd.ExecuteQuery<NoNeedToCache>(default, false);
        }
        /// <summary>
        /// Executes the managed <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteQueryAsync(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.NeedToCache(vars))
                return cmd.ExecuteQueryAsync(command, false, ct);
            return cmd.ExecuteQueryAsync<NoNeedToCache>(default, false, ct);
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="behavior">The behavior to use for the reader</param>
        public DbDataReader ExecuteReader(CommandBehavior behavior = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.NeedToCache(vars))
                return cmd.ExecuteReader(command, behavior);
            return cmd.ExecuteReader<NoNeedToCache>(default, behavior);
        }
        /// <summary>
        /// Executes the managed <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<DbDataReader> ExecuteReaderAsync(CommandBehavior behavior = default, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.NeedToCache(vars))
                return cmd.ExecuteReaderAsync(command, behavior, ct);
            return cmd.ExecuteReaderAsync<NoNeedToCache>(default, behavior, ct);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="behavior">The behavior to use for the reader</param>
        public MultiReader ExecuteMultiReader(CommandBehavior behavior = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteMultiReader(command, vars.ToBoolArr(), false, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="behavior">The behavior to use for the reader</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<MultiReader> ExecuteMultiReaderAsync(CommandBehavior behavior = default, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteMultiReaderAsync(command, vars.ToBoolArr(), false, behavior, ct);
        }
        /// <summary>
        /// Executes the managed <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        public T? QueryOne<T>() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryOne<SchemaParser<T>, T>(cache, false);
            return cmd.QueryOne<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), false);
        }
        /// <summary>
        /// Executes the managed <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        public IEnumerable<T> QueryAll<T>() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryAll<SchemaParser<T>, T>(cache, false);
            return cmd.QueryAll<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), false);
        }
        /// <summary>
        /// Executes the managed <see cref="DbCommand"/> and parse each rows to return a buffered collection of <typeparamref name="T"/>.
        /// </summary>
        public List<T> QueryAllBuffered<T>() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryAllBuffered<SchemaParser<T>, T>(cache, false);
            return cmd.QueryAllBuffered<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), false);
        }

        /// <summary>
        /// Asynchronously executes the managed <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QueryOneAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryOneAsync<SchemaParser<T>, T>(cache, false, ct);
            return cmd.QueryOneAsync<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), false, ct);
        }
        /// <summary>
        /// Asynchronously executes the managed <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryAllAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryAllAsync<SchemaParser<T>, T>(cache, false, ct);
            return cmd.QueryAllAsync<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), false, ct);
        }
        /// <summary>
        /// Asynchronously executes the managed <see cref="DbCommand"/> and parse each rows to return a buffered collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<List<T>> QueryAllBufferedAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryAllBufferedAsync<SchemaParser<T>, T>(cache, false, ct);
            return cmd.QueryAllBufferedAsync<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), false, ct);
        }
    }

    extension(QueryBuilderCommand<IDbCommand> builder) {
        /// <summary>
        /// Executes the managed <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        public int ExecuteQuery() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.NeedToCache(vars))
                return cmd.ExecuteQuery(command, false);
            return cmd.ExecuteQuery<NoNeedToCache>(default, false);
        }
        /// <summary>
        /// Executes the managed <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteQueryAsync(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.NeedToCache(vars))
                return cmd.ExecuteQueryAsync(command, false, ct);
            return cmd.ExecuteQueryAsync<NoNeedToCache>(default, false, ct);
        }
        /// <summary>
        /// Executes the reader of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="behavior">The behavior to use for the reader</param>
        public DbDataReader ExecuteReader(CommandBehavior behavior = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.NeedToCache(vars))
                return cmd.ExecuteReader(command, behavior);
            return cmd.ExecuteReader<NoNeedToCache>(default, behavior);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="IDbCommand"/>.
        /// </summary>
        /// <param name="behavior">The behavior to use for the reader</param>
        public MultiReader ExecuteMultiReader(CommandBehavior behavior = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteMultiReader(command, vars.ToBoolArr(), false, behavior);
        }

        /// <summary>
        /// Executes the managed <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        public T? QueryOne<T>() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryOne<SchemaParser<T>, T>(cache, false);
            return cmd.QueryOne<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), false);
        }
        /// <summary>
        /// Executes the managed <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        public IEnumerable<T> QueryAll<T>() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryAll<SchemaParser<T>, T>(cache, false);
            return cmd.QueryAll<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), false);
        }
        /// <summary>
        /// Executes the managed <see cref="IDbCommand"/> and parse each rows to return a buffered collection of <typeparamref name="T"/>.
        /// </summary>
        public List<T> QueryAllBuffered<T>() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryAllBuffered<SchemaParser<T>, T>(cache, false);
            return cmd.QueryAllBuffered<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), false);
        }

        /// <summary>
        /// Asynchronously executes the managed <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QueryOneAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryOneAsync<SchemaParser<T>, T>(cache, false, ct);
            return cmd.QueryOneAsync<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), false, ct);
        }
        /// <summary>
        /// Asynchronously executes the managed <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryAllAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryAllAsync<SchemaParser<T>, T>(cache, false, ct);
            return cmd.QueryAllAsync<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), false, ct);
        }
        /// <summary>
        /// Asynchronously executes the managed <see cref="IDbCommand"/> and parse each rows to return a buffered collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<List<T>> QueryAllBufferedAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryAllBufferedAsync<SchemaParser<T>, T>(cache, false, ct);
            return cmd.QueryAllBufferedAsync<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), false, ct);
        }
    }
}
