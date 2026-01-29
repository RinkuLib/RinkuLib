using System.Data;
using System.Data.Common;
using RinkuLib.Queries;

namespace RinkuLib.Commands;
public static class QueryBuilderExtensions {
    public static DbCommand GetCommand<T>(this QueryBuilder<T> builder, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) where T : QueryCommand {
        var cmd = cnn.CreateCommand();
        if (transaction is not null)
            cmd.Transaction = transaction;
        if (timeout.HasValue)
            cmd.CommandTimeout = timeout.Value;
        builder.QueryCommand.SetCommand(cmd, builder.Variables);
        return cmd;
    }
    public static IDbCommand GetCommand<T>(this QueryBuilder<T> builder, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) where T : QueryCommand {
        var cmd = cnn.CreateCommand();
        if (transaction is not null)
            cmd.Transaction = transaction;
        if (timeout.HasValue)
            cmd.CommandTimeout = timeout.Value;
        if (cmd is DbCommand c)
            builder.QueryCommand.SetCommand(c, builder.Variables);
        else
            builder.QueryCommand.SetCommand(cmd, builder.Variables);
        return cmd;
    }
    private static DbCommand GetCommandAndInfo<T>(this QueryBuilder<T> builder, DbConnection cnn, DbTransaction? transaction, int? timeout, out IParserCache? cache) where T : QueryCommand {
        var cmd = cnn.CreateCommand();
        if (transaction is not null)
            cmd.Transaction = transaction;
        if (timeout.HasValue)
            cmd.CommandTimeout = timeout.Value;
        var qc = builder.QueryCommand;
        qc.SetCommand(cmd, builder.Variables);
        cache = qc.NeedToCache(builder.Variables) ? qc : null;
        return cmd;
    }
    private static IDbCommand GetCommandAndInfo<T>(this QueryBuilder<T> builder, IDbConnection cnn, IDbTransaction? transaction, int? timeout, out IParserCache? cache) where T : QueryCommand {
        var cmd = cnn.CreateCommand();
        if (transaction is not null)
            cmd.Transaction = transaction;
        if (timeout.HasValue)
            cmd.CommandTimeout = timeout.Value;
        var qc = builder.QueryCommand;
        if (cmd is DbCommand c)
            qc.SetCommand(c, builder.Variables);
        else
            qc.SetCommand(cmd, builder.Variables);
        cache = qc.NeedToCache(builder.Variables) ? qc : null;
        return cmd;
    }
    private static DbCommand GetCommandAndInfo<TQuery, T>(this QueryBuilder<TQuery> builder, DbConnection cnn, DbTransaction? transaction, int? timeout, out IParserCache? cache, out Func<DbDataReader, T>? parser, out CommandBehavior behavior) where TQuery : QueryCommand {
        var cmd = cnn.CreateCommand();
        if (transaction is not null)
            cmd.Transaction = transaction;
        if (timeout.HasValue)
            cmd.CommandTimeout = timeout.Value;
        var qc = builder.QueryCommand;
        qc.SetCommand(cmd, builder.Variables);
        cache = qc.GetCacheAndParser(builder.Variables, out behavior, out parser);
        return cmd;
    }
    private static IDbCommand GetCommandAndInfo<TQuery, T>(this QueryBuilder<TQuery> builder, IDbConnection cnn, IDbTransaction? transaction, int? timeout, out IParserCache? cache, out Func<DbDataReader, T>? parser, out CommandBehavior behavior) where TQuery : QueryCommand {
        var cmd = cnn.CreateCommand();
        if (transaction is not null)
            cmd.Transaction = transaction;
        if (timeout.HasValue)
            cmd.CommandTimeout = timeout.Value;
        var qc = builder.QueryCommand;
        if (cmd is DbCommand c)
            qc.SetCommand(c, builder.Variables);
        else
            qc.SetCommand(cmd, builder.Variables);
        cache = qc.GetCacheAndParser(builder.Variables, out behavior, out parser);
        return cmd;
    }
    
    extension(QueryBuilder<QueryCommand> builder) {
        public T? QuerySingle<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo<QueryCommand, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QuerySingle(parser, cache, behavior, true);
        public T? QuerySingle<T>(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo<QueryCommand, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QuerySingle(parser, cache, behavior, false);
        public IEnumerable<T> QueryMultiple<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo<QueryCommand, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QueryMultiple(parser, cache, behavior, true);
        public IEnumerable<T> QueryMultiple<T>(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo<QueryCommand, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QueryMultiple(parser, cache, behavior, false);

        public Task<T?> QuerySingleAsync<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo<QueryCommand, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QuerySingleAsync(parser, cache, behavior, true, ct);
        public Task<T?> QuerySingleAsync<T>(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo<QueryCommand, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QuerySingleAsync(parser, cache, behavior, false, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo<QueryCommand, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QueryMultipleAsync(parser, cache, behavior, true, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo<QueryCommand, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QueryMultipleAsync(parser, cache, behavior, false, ct);



        public T? QuerySingle<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo<QueryCommand, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QuerySingle(parser, cache, behavior, true);
        public T? QuerySingle<T>(IDbConnection cnn, out IDbCommand cmd, IDbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo<QueryCommand, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QuerySingle(parser, cache, behavior, false);
        public IEnumerable<T> QueryMultiple<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo<QueryCommand, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QueryMultiple(parser, cache, behavior, true);
        public IEnumerable<T> QueryMultiple<T>(IDbConnection cnn, out IDbCommand cmd, IDbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo<QueryCommand, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QueryMultiple(parser, cache, behavior, false);

        public Task<T?> QuerySingleAsync<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo<QueryCommand, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QuerySingleAsync(parser, cache, behavior, true, ct);
        public Task<T?> QuerySingleAsync<T>(IDbConnection cnn, out IDbCommand cmd, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo<QueryCommand, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QuerySingleAsync(parser, cache, behavior, false, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo<QueryCommand, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QueryMultipleAsync(parser, cache, behavior, true, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(IDbConnection cnn, out IDbCommand cmd, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo<QueryCommand, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QueryMultipleAsync(parser, cache, behavior, false, ct);
    }
    extension<TQuery>(QueryBuilder<TQuery> builder) where TQuery : QueryCommand {

        public T? QuerySingle<T>(DbConnection cnn, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null)
            => (func is null 
            ? builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache, out func, out defaultBehavior)
            : builder.GetCommandAndInfo(cnn, transaction, timeout, out cache))
                .QuerySingle(func, cache, defaultBehavior, true);
        public T? QuerySingle<T>(DbConnection cnn, out DbCommand cmd, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null)
            => (cmd = func is null
            ? builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache, out func, out defaultBehavior)
            : builder.GetCommandAndInfo(cnn, transaction, timeout, out cache))
                .QuerySingle(func, cache, defaultBehavior, false);
        public IEnumerable<T> QueryMultiple<T>(DbConnection cnn, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null)
            => (func is null
            ? builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache, out func, out defaultBehavior)
            : builder.GetCommandAndInfo(cnn, transaction, timeout, out cache))
                .QueryMultiple(func, cache, defaultBehavior, true);
        public IEnumerable<T> QueryMultiple<T>(DbConnection cnn, out DbCommand cmd, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null)
            => (cmd = func is null
            ? builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache, out func, out defaultBehavior)
            : builder.GetCommandAndInfo(cnn, transaction, timeout, out cache))
                .QueryMultiple(func, cache, defaultBehavior, false);

        public Task<T?> QuerySingleAsync<T>(DbConnection cnn, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (func is null
            ? builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache, out func, out defaultBehavior)
            : builder.GetCommandAndInfo(cnn, transaction, timeout, out cache))
                .QuerySingleAsync(func, cache, defaultBehavior, true, ct);
        public Task<T?> QuerySingleAsync<T>(DbConnection cnn, out DbCommand cmd, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = func is null
            ? builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache, out func, out defaultBehavior)
            : builder.GetCommandAndInfo(cnn, transaction, timeout, out cache))
                .QuerySingleAsync(func, cache, defaultBehavior, false, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(DbConnection cnn, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (func is null
            ? builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache, out func, out defaultBehavior)
            : builder.GetCommandAndInfo(cnn, transaction, timeout, out cache))
                .QueryMultipleAsync(func, cache, defaultBehavior, true, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(DbConnection cnn, out DbCommand cmd, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = func is null
            ? builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache, out func, out defaultBehavior)
            : builder.GetCommandAndInfo(cnn, transaction, timeout, out cache))
                .QueryMultipleAsync(func, cache, defaultBehavior, false, ct);



        public T? QuerySingle<T>(IDbConnection cnn, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null)
            => (func is null
            ? builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache, out func, out defaultBehavior)
            : builder.GetCommandAndInfo(cnn, transaction, timeout, out cache))
                .QuerySingle(func, cache, defaultBehavior, true);
        public T? QuerySingle<T>(IDbConnection cnn, out IDbCommand cmd, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null)
            => (cmd = func is null
            ? builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache, out func, out defaultBehavior)
            : builder.GetCommandAndInfo(cnn, transaction, timeout, out cache))
                .QuerySingle(func, cache, defaultBehavior, false);
        public IEnumerable<T> QueryMultiple<T>(IDbConnection cnn, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null)
            => (func is null
            ? builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache, out func, out defaultBehavior)
            : builder.GetCommandAndInfo(cnn, transaction, timeout, out cache))
                .QueryMultiple(func, cache, defaultBehavior, true);
        public IEnumerable<T> QueryMultiple<T>(IDbConnection cnn, out IDbCommand cmd, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null)
            => (cmd = func is null
            ? builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache, out func, out defaultBehavior)
            : builder.GetCommandAndInfo(cnn, transaction, timeout, out cache))
                .QueryMultiple(func, cache, defaultBehavior, false);

        public Task<T?> QuerySingleAsync<T>(IDbConnection cnn, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (func is null
            ? builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache, out func, out defaultBehavior)
            : builder.GetCommandAndInfo(cnn, transaction, timeout, out cache))
                .QuerySingleAsync(func, cache, defaultBehavior, true, ct);
        public Task<T?> QuerySingleAsync<T>(IDbConnection cnn, out IDbCommand cmd, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = func is null
            ? builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache, out func, out defaultBehavior)
            : builder.GetCommandAndInfo(cnn, transaction, timeout, out cache))
                .QuerySingleAsync(func, cache, defaultBehavior, false, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(IDbConnection cnn, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (func is null
            ? builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache, out func, out defaultBehavior)
            : builder.GetCommandAndInfo(cnn, transaction, timeout, out cache))
                .QueryMultipleAsync(func, cache, defaultBehavior, true, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(IDbConnection cnn, out IDbCommand cmd, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = func is null
            ? builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache, out func, out defaultBehavior)
            : builder.GetCommandAndInfo(cnn, transaction, timeout, out cache))
                .QueryMultipleAsync(func, cache, defaultBehavior, false, ct);

        public int Execute(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache)
                .ExecuteQuery(cache, true);
        public int Execute(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache))
                .ExecuteQuery(cache, false);
        public Task<int> ExecuteAsync(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache)
                .ExecuteQueryAsync(cache, true, ct);
        public Task<int> ExecuteAsync(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache))
                .ExecuteQueryAsync(cache, false, ct);
    }
}
