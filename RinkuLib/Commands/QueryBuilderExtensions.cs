using System.Data;
using System.Data.Common;
using RinkuLib.Queries;

namespace RinkuLib.Commands;
public static class QueryBuilderExtensions {

    extension(QueryBuilder builder) {
        public T? QuerySingle<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QuerySingle(parser, cache, behavior, true);
        public T? QuerySingle<T>(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QuerySingle(parser, cache, behavior, false);
        public IEnumerable<T> QueryMultiple<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QueryMultiple(parser, cache, behavior, true);
        public IEnumerable<T> QueryMultiple<T>(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QueryMultiple(parser, cache, behavior, false);

        public Task<T?> QuerySingleAsync<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QuerySingleAsync(parser, cache, behavior, true, ct);
        public Task<T?> QuerySingleAsync<T>(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QuerySingleAsync(parser, cache, behavior, false, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QueryMultipleAsync(parser, cache, behavior, true, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QueryMultipleAsync(parser, cache, behavior, false, ct);

        public T? QuerySingle<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QuerySingle(parser, cache, behavior, true);
        public T? QuerySingle<T>(IDbConnection cnn, out IDbCommand cmd, IDbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QuerySingle(parser, cache, behavior, false);
        public IEnumerable<T> QueryMultiple<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QueryMultiple(parser, cache, behavior, true);
        public IEnumerable<T> QueryMultiple<T>(IDbConnection cnn, out IDbCommand cmd, IDbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QueryMultiple(parser, cache, behavior, false);

        public Task<T?> QuerySingleAsync<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QuerySingleAsync(parser, cache, behavior, true, ct);
        public Task<T?> QuerySingleAsync<T>(IDbConnection cnn, out IDbCommand cmd, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QuerySingleAsync(parser, cache, behavior, false, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QueryMultipleAsync(parser, cache, behavior, true, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(IDbConnection cnn, out IDbCommand cmd, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QueryMultipleAsync(parser, cache, behavior, false, ct);



    }
    extension<TBuilder>(TBuilder builder) where TBuilder : ICommandBuilder {
        public T? QuerySingle<T>(DbConnection cnn, Func<DbDataReader, T> func, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out _, out _)
                .QuerySingle(func, cache, defaultBehavior, true);
        public T? QuerySingle<T>(DbConnection cnn, out DbCommand cmd, Func<DbDataReader, T> func, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out _, out _))
                .QuerySingle(func, cache, defaultBehavior, false);
        public IEnumerable<T> QueryMultiple<T>(DbConnection cnn, Func<DbDataReader, T> func, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out _, out _)
                .QueryMultiple(func, cache, defaultBehavior, true);
        public IEnumerable<T> QueryMultiple<T>(DbConnection cnn, out DbCommand cmd, Func<DbDataReader, T> func, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out _, out _))
                .QueryMultiple(func, cache, defaultBehavior, false);

        public Task<T?> QuerySingleAsync<T>(DbConnection cnn, Func<DbDataReader, T> func, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out _, out _)
                .QuerySingleAsync(func, cache, defaultBehavior, true, ct);
        public Task<T?> QuerySingleAsync<T>(DbConnection cnn, out DbCommand cmd, Func<DbDataReader, T> func, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out _, out _))
                .QuerySingleAsync(func, cache, defaultBehavior, false, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(DbConnection cnn, Func<DbDataReader, T> func, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out _, out _)
                .QueryMultipleAsync(func, cache, defaultBehavior, true, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(DbConnection cnn, out DbCommand cmd, Func<DbDataReader, T> func, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out _, out _))
                .QueryMultipleAsync(func, cache, defaultBehavior, false, ct);



        public T? QuerySingle<T>(IDbConnection cnn, Func<DbDataReader, T> func, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out _, out _)
                .QuerySingle(func, cache, defaultBehavior, true);
        public T? QuerySingle<T>(IDbConnection cnn, out IDbCommand cmd, Func<DbDataReader, T> func, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out _, out _))
                .QuerySingle(func, cache, defaultBehavior, false);
        public IEnumerable<T> QueryMultiple<T>(IDbConnection cnn, Func<DbDataReader, T> func, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out _, out _)
                .QueryMultiple(func, cache, defaultBehavior, true);
        public IEnumerable<T> QueryMultiple<T>(IDbConnection cnn, out IDbCommand cmd, Func<DbDataReader, T> func, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out _, out _))
                .QueryMultiple(func, cache, defaultBehavior, false);

        public Task<T?> QuerySingleAsync<T>(IDbConnection cnn, Func<DbDataReader, T> func, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out _, out _)
                .QuerySingleAsync(func, cache, defaultBehavior, true, ct);
        public Task<T?> QuerySingleAsync<T>(IDbConnection cnn, out IDbCommand cmd, Func<DbDataReader, T> func, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out _, out _))
                .QuerySingleAsync(func, cache, defaultBehavior, false, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(IDbConnection cnn, Func<DbDataReader, T> func, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out _, out _)
                .QueryMultipleAsync(func, cache, defaultBehavior, true, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(IDbConnection cnn, out IDbCommand cmd, Func<DbDataReader, T> func, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo<T>(cnn, transaction, timeout, out var cache, out _, out _))
                .QueryMultipleAsync(func, cache, defaultBehavior, false, ct);

        public int Execute(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndCache(cnn, transaction, timeout, out var cache)
                .ExecuteQuery(cache, true);
        public int Execute(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndCache(cnn, transaction, timeout, out var cache))
                .ExecuteQuery(cache, false);
        public Task<int> ExecuteAsync(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndCache(cnn, transaction, timeout, out var cache)
                .ExecuteQueryAsync(cache, true, ct);
        public Task<int> ExecuteAsync(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndCache(cnn, transaction, timeout, out var cache))
                .ExecuteQueryAsync(cache, false, ct);
    }
}
