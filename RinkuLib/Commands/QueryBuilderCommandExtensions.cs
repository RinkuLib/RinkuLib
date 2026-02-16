using System.Data;
using System.Data.Common;
using RinkuLib.Queries;

namespace RinkuLib.Commands;

/// <summary>
/// Extensions on <see cref="QueryBuilderCommand{T}"/>
/// </summary>
public static class QueryBuilderCommandExtensions {

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
        /// Executes the managed <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        public T? QuerySingle<T>() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QuerySingle<SchemaParser<T>, T>(cache, false);
            return cmd.QuerySingle<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), false);
        }
        /// <summary>
        /// Executes the managed <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        public IEnumerable<T> QueryMultiple<T>() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryMultiple<SchemaParser<T>, T>(cache, false);
            return cmd.QueryMultiple<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), false);
        }

        /// <summary>
        /// Asynchronously executes the managed <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QuerySingleAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QuerySingleAsync<SchemaParser<T>, T>(cache, false, ct);
            return cmd.QuerySingleAsync<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), false, ct);
        }
        /// <summary>
        /// Asynchronously executes the managed <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryMultipleAsync<SchemaParser<T>, T>(cache, false, ct);
            return cmd.QueryMultipleAsync<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), false, ct);
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
        /// Executes the managed <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        public T? QuerySingle<T>() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QuerySingle<SchemaParser<T>, T>(cache, false);
            return cmd.QuerySingle<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), false);
        }
        /// <summary>
        /// Executes the managed <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        public IEnumerable<T> QueryMultiple<T>() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryMultiple<SchemaParser<T>, T>(cache, false);
            return cmd.QueryMultiple<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), false);
        }

        /// <summary>
        /// Asynchronously executes the managed <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QuerySingleAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QuerySingleAsync<SchemaParser<T>, T>(cache, false, ct);
            return cmd.QuerySingleAsync<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), false, ct);
        }
        /// <summary>
        /// Asynchronously executes the managed <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryMultipleAsync<SchemaParser<T>, T>(cache, false, ct);
            return cmd.QueryMultipleAsync<ParsingCacheToMake<T>, T>(new(command, cache, vars.GetFalseIndexes()), false, ct);
        }
    }
}
