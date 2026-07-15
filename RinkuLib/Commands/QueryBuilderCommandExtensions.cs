using System.Data;
using System.Data.Common;
using RinkuLib.TypeAccessing;

namespace RinkuLib.Commands;

/// <summary>
/// Runs a query off a <see cref="QueryBuilderCommand{T}"/>. The bound command already carries the builder's
/// values, so these methods only render its text and execute, in the same shapes the command's own execution
/// methods offer. Call them again after changing values to rerun the one command.
/// </summary>
public static class QueryBuilderCommandExtensions {
    /// <summary>Projects the values to a presence map, <see langword="true"/> for each key that carries a value.</summary>
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
        public T? ExecuteScalar<T>() {
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
        public Task<T?> ExecuteScalarAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteScalarAsync<T>(false, command.NeedToCache(vars) ? command : null, ct);
        }
        /// <summary>
        /// Runs the bound command and returns its <see cref="DbDataReader"/>.
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
        /// Asynchronously runs the bound command and returns its <see cref="DbDataReader"/>.
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
            return cmd.Query(new LinkerQueryCommandWithParser<T>(command, vars.ToBoolArray()), false);
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
            return cmd.QueryAsync(new LinkerQueryCommandWithParser<T>(command, vars.ToBoolArray()), false, ct);
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
                return cmd.StreamQueryAsync(parser, null, false, ct);
            else if (parser is not null)
                return cmd.StreamQueryAsync(parser, command, false, ct);
            return cmd.StreamQueryAsync(new LinkerQueryCommandWithParser<T>(command, vars.ToBoolArray()), false, ct);
        }
    }

    extension(QueryBuilderCommand<IDbCommand> builder) {
        /// <inheritdoc cref="QueryBuilderCommandExtensions.Execute(QueryBuilderCommand{DbCommand})"/>
        public int Execute() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.Execute(false, command.NeedToCache(vars) ? command : null);
        }
        /// <inheritdoc cref="QueryBuilderCommandExtensions.ExecuteAsync(QueryBuilderCommand{DbCommand}, CancellationToken)"/>
        public Task<int> ExecuteAsync(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteAsync(false, command.NeedToCache(vars) ? command : null, ct);
        }
        /// <inheritdoc cref="QueryBuilderCommandExtensions.ExecuteScalar{T}(QueryBuilderCommand{DbCommand})"/>
        public T? ExecuteScalar<T>() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteScalar<T>(false, command.NeedToCache(vars) ? command : null);
        }
        /// <inheritdoc cref="QueryBuilderCommandExtensions.ExecuteScalarAsync{T}(QueryBuilderCommand{DbCommand}, CancellationToken)"/>
        public Task<T?> ExecuteScalarAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteScalarAsync<T>(false, command.NeedToCache(vars) ? command : null, ct);
        }
        /// <inheritdoc cref="QueryBuilderCommandExtensions.ExecuteReader(QueryBuilderCommand{DbCommand}, CommandBehavior)"/>
        public DbDataReader ExecuteReader(CommandBehavior behavior = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteReader(behavior, command.NeedToCache(vars) ? command : null);
        }
        /// <inheritdoc cref="QueryBuilderCommandExtensions.ExecuteReaderAsync(QueryBuilderCommand{DbCommand}, CommandBehavior, CancellationToken)"/>
        public Task<DbDataReader> ExecuteReaderAsync(CommandBehavior behavior = default, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteReaderAsync(behavior, command.NeedToCache(vars) ? command : null, ct);
        }
        /// <inheritdoc cref="QueryBuilderCommandExtensions.ExecuteMultiReader(QueryBuilderCommand{DbCommand}, CommandBehavior)"/>
        public MultiReader ExecuteMultiReader(CommandBehavior behavior = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteMultiReader(command, vars.ToBoolArr(), false, behavior);
        }
        /// <inheritdoc cref="QueryBuilderCommandExtensions.ExecuteMultiReaderAsync(QueryBuilderCommand{DbCommand}, CommandBehavior, CancellationToken)"/>
        public Task<MultiReader> ExecuteMultiReaderAsync(CommandBehavior behavior = default, CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            return cmd.ExecuteMultiReaderAsync(command, vars.ToBoolArr(), false, behavior, ct);
        }
        /// <inheritdoc cref="QueryBuilderCommandExtensions.Query{T}(QueryBuilderCommand{DbCommand})"/>
        public T Query<T>() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCachedParser<T>(vars, out var parser))
                return parser.Query(cmd, false);
            else if (parser is not null)
                return parser.Query(cmd, command, false);
            return cmd.Query(new LinkerQueryCommandWithParser<T>(command, vars.ToBoolArray()), false);
        }
        /// <inheritdoc cref="QueryBuilderCommandExtensions.QueryAsync{T}(QueryBuilderCommand{DbCommand}, CancellationToken)"/>
        public Task<T> QueryAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCachedParser<T>(vars, out var parser))
                return parser.QueryAsync(cmd, false, ct);
            else if (parser is not null)
                return parser.QueryAsync(cmd, command, false, ct);
            return cmd.QueryAsync(new LinkerQueryCommandWithParser<T>(command, vars.ToBoolArray()), false, ct);
        }
    }
}
