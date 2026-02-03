using System.Data;
using System.Data.Common;
using RinkuLib.Queries;

namespace RinkuLib.Commands;

public static class QueryBuilderCommandExtensions {

    extension(QueryBuilderCommand<DbCommand> builder) {
        public int ExecuteQuery() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.NeedToCache(vars))
                return cmd.ExecuteQuery(command, false);
            return cmd.ExecuteQuery<NoNeedToCache>(default, false);
        }
        public Task<int> ExecuteQueryAsync(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.NeedToCache(vars))
                return cmd.ExecuteQueryAsync(command, false, ct);
            return cmd.ExecuteQueryAsync<NoNeedToCache>(default, false, ct);
        }
        public T? QuerySingle<T>() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QuerySingle<ParsingCache<T>, T>(cache, false);
            return cmd.QuerySingle<ParsingCacheToMake<T>, T>(new(command, cache, command.GetActualGetCacheIndex<T>(vars)), false);
        }
        public IEnumerable<T> QueryMultiple<T>() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryMultiple<ParsingCache<T>, T>(cache, false);
            return cmd.QueryMultiple<ParsingCacheToMake<T>, T>(new(command, cache, command.GetActualGetCacheIndex<T>(vars)), false);
        }

        public Task<T?> QuerySingleAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QuerySingleAsync<ParsingCache<T>, T>(cache, false, ct);
            return cmd.QuerySingleAsync<ParsingCacheToMake<T>, T>(new(command, cache, command.GetActualGetCacheIndex<T>(vars)), false, ct);
        }
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryMultipleAsync<ParsingCache<T>, T>(cache, false, ct);
            return cmd.QueryMultipleAsync<ParsingCacheToMake<T>, T>(new(command, cache, command.GetActualGetCacheIndex<T>(vars)), false, ct);
        }
    }

    extension(QueryBuilderCommand<IDbCommand> builder) {
        public T? QuerySingle<T>() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QuerySingle<ParsingCache<T>, T>(cache, false);
            return cmd.QuerySingle<ParsingCacheToMake<T>, T>(new(command, cache, command.GetActualGetCacheIndex<T>(vars)), false);
        }
        public IEnumerable<T> QueryMultiple<T>() {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryMultiple<ParsingCache<T>, T>(cache, false);
            return cmd.QueryMultiple<ParsingCacheToMake<T>, T>(new(command, cache, command.GetActualGetCacheIndex<T>(vars)), false);
        }

        public Task<T?> QuerySingleAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QuerySingleAsync<ParsingCache<T>, T>(cache, false, ct);
            return cmd.QuerySingleAsync<ParsingCacheToMake<T>, T>(new(command, cache, command.GetActualGetCacheIndex<T>(vars)), false, ct);
        }
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(CancellationToken ct = default) {
            var vars = builder.Variables;
            var command = builder.QueryCommand;
            var cmd = builder.Command;
            cmd.CommandText = command.QueryText.Parse(vars);
            if (command.TryGetCache<T>(vars, out var cache))
                return cmd.QueryMultipleAsync<ParsingCache<T>, T>(cache, false, ct);
            return cmd.QueryMultipleAsync<ParsingCacheToMake<T>, T>(new(command, cache, command.GetActualGetCacheIndex<T>(vars)), false, ct);
        }
    }
}
