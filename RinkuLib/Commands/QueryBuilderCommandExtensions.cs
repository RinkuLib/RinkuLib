using System.Data;
using System.Data.Common;
using RinkuLib.TypeAccessing;

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
        public int Execute() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.Execute(false, command.NeedToCache(vars) ? command : null);
        }
        /// <summary>
        /// Executes the managed <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteAsync(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteAsync(false, command.NeedToCache(vars) ? command : null, ct);
        }
        /// <summary>
        /// Executes the managed <see cref="DbCommand"/> and return the scalar value.
        /// </summary>
        public T ExecuteScalar<T>() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteScalar<T>(false, command.NeedToCache(vars) ? command : null);
        }
        /// <summary>
        /// Executes the managed <see cref="DbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> ExecuteScalarAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteScalarAsync<T>(false, command.NeedToCache(vars) ? command : null, ct);
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
            return cmd.ExecuteReader(behavior, command.NeedToCache(vars) ? command : null);
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
            return cmd.ExecuteReaderAsync(behavior, command.NeedToCache(vars) ? command : null, ct);
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
        public T Query<T>() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCachedParser<T>(vars, out var parser))
                return parser.Query(cmd, false);
            else if (parser is not null)
                return parser.Query(cmd, command, false);
            return cmd.Query(false, null, new LinkerQueryCommandWithParser<T>(command, vars.ToBoolArray()));
        }
        /// <summary>
        /// Asynchronously executes the managed <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> QueryAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCachedParser<T>(vars, out var parser))
                return parser.QueryAsync(cmd, false, ct);
            else if (parser is not null)
                return parser.QueryAsync(cmd, command, false, ct);
            return cmd.QueryAsync(false, null, new LinkerQueryCommandWithParser<T>(command, vars.ToBoolArray()), ct);
        }

        /// <summary>
        /// Asynchronously executes a <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> StreamQueryAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCachedParser<T>(vars, out var parser))
                return cmd.StreamQueryAsync(false, parser, (ICache?)null, ct);
            else if (parser is not null)
                return cmd.StreamQueryAsync(false, parser, command, ct);
            return cmd.StreamQueryAsync(false, null, new LinkerQueryCommandWithParser<T>(command, vars.ToBoolArray()), ct);
        }
    }

    extension(QueryBuilderCommand<IDbCommand> builder) {
        /// <summary>
        /// Executes the managed <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        public int Execute() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.Execute(false, command.NeedToCache(vars) ? command : null);
        }
        /// <summary>
        /// Executes the managed <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteAsync(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteAsync(false, command.NeedToCache(vars) ? command : null, ct);
        }
        /// <summary>
        /// Executes the managed <see cref="DbCommand"/> and return the scalar value.
        /// </summary>
        public T ExecuteScalar<T>() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteScalar<T>(false, command.NeedToCache(vars) ? command : null);
        }
        /// <summary>
        /// Executes the managed <see cref="DbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> ExecuteScalarAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteScalarAsync<T>(false, command.NeedToCache(vars) ? command : null, ct);
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
            return cmd.ExecuteReader(behavior, command.NeedToCache(vars) ? command : null);
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
            return cmd.ExecuteReaderAsync(behavior, command.NeedToCache(vars) ? command : null, ct);
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
        public T Query<T>() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCachedParser<T>(vars, out var parser))
                return parser.Query(cmd, false);
            else if (parser is not null)
                return parser.Query(cmd, command, false);
            return cmd.Query(false, null, new LinkerQueryCommandWithParser<T>(command, vars.ToBoolArray()));
        }
        /// <summary>
        /// Asynchronously executes the managed <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> QueryAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCachedParser<T>(vars, out var parser))
                return parser.QueryAsync(cmd, false, ct);
            else if (parser is not null)
                return parser.QueryAsync(cmd, command, false, ct);
            return cmd.QueryAsync(false, null, new LinkerQueryCommandWithParser<T>(command, vars.ToBoolArray()), ct);
        }
    }
}
