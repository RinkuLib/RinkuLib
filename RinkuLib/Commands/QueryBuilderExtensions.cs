using System.Data;
using System.Data.Common;
using RinkuLib.Queries;

namespace RinkuLib.Commands;
public static class QueryBuilderExtensions {
    public static QueryBuilder<QueryCommand<T>> StartBuilder<T>(this QueryCommand<T> command)
        => new(command);
    public static QueryBuilderCommand<QueryCommand<T>, TCmd> StartBuilder<T, TCmd>(this QueryCommand<T> command, TCmd cmd) where TCmd : IDbCommand
        => new(command, cmd);
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
    private static DbCommand GetCommandAndInfo<T>(this QueryBuilder<T> builder, DbConnection cnn, DbTransaction? transaction, int? timeout, out ICache? cache) where T : QueryCommand {
        var cmd = cnn.CreateCommand();
        if (transaction is not null)
            cmd.Transaction = transaction;
        if (timeout.HasValue)
            cmd.CommandTimeout = timeout.Value;
        var qc = builder.QueryCommand;
        qc.SetCommand(cmd, builder.Variables);
        cache = qc.Parameters.NeedToCache(builder.Variables) ? qc : null;
        return cmd;
    }
    private static IDbCommand GetCommandAndInfo<T>(this QueryBuilder<T> builder, IDbConnection cnn, IDbTransaction? transaction, int? timeout, out ICache? cache) where T : QueryCommand {
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
        cache = qc.Parameters.NeedToCache(builder.Variables) ? qc : null;
        return cmd;
    }
    private static DbCommand GetCommandAndInfo<TQuery, T>(this QueryBuilder<TQuery> builder, DbConnection cnn, DbTransaction? transaction, int? timeout, out ICache? cache, out Func<DbDataReader, T>? parser, out CommandBehavior behavior) where TQuery : QueryCommand<T> {
        var cmd = cnn.CreateCommand();
        if (transaction is not null)
            cmd.Transaction = transaction;
        if (timeout.HasValue)
            cmd.CommandTimeout = timeout.Value;
        var qc = builder.QueryCommand;
        qc.SetCommand(cmd, builder.Variables);
        parser = qc.GetFuncAndCache(builder.Variables, out behavior, out cache);
        return cmd;
    }
    private static IDbCommand GetCommandAndInfo<TQuery, T>(this QueryBuilder<TQuery> builder, IDbConnection cnn, IDbTransaction? transaction, int? timeout, out ICache? cache, out Func<DbDataReader, T>? parser, out CommandBehavior behavior) where TQuery : QueryCommand<T> {
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
        parser = qc.GetFuncAndCache(builder.Variables, out behavior, out cache);
        return cmd;
    }
    private static int Execute(QueryCommand queryCommand, object?[] variables, DbCommand cmd, DbTransaction? transaction, int? timeout, bool disposeCommand) {
        if (transaction is not null)
            cmd.Transaction = transaction;
        if (timeout.HasValue)
            cmd.CommandTimeout = timeout.Value;
        queryCommand.SetCommand(cmd, variables);
        return cmd.Execute(disposeCommand);
    }
    private static Task<int> ExecuteAsync(QueryCommand queryCommand, object?[] variables, DbCommand cmd, DbTransaction? transaction, int? timeout, bool disposeCommand, CancellationToken ct) {
        if (transaction is not null)
            cmd.Transaction = transaction;
        if (timeout.HasValue)
            cmd.CommandTimeout = timeout.Value;
        queryCommand.SetCommand(cmd, variables);
        return cmd.ExecuteAsync(disposeCommand, ct);
    }
    extension<T>(QueryBuilder<QueryCommand<T>> builder) {

        public T? QuerySingle(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo<QueryCommand<T>, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QuerySingle(parser, cache, behavior, true);
        public T? QuerySingle(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo<QueryCommand<T>, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QuerySingle(parser, cache, behavior, false);
        public IEnumerable<T> QueryMultiple(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo<QueryCommand<T>, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QueryMultiple(parser, cache, behavior, true);
        public IEnumerable<T> QueryMultiple(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo<QueryCommand<T>, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QueryMultiple(parser, cache, behavior, false);

        public Task<T?> QuerySingleAsync(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo<QueryCommand<T>, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QuerySingleAsync(parser, cache, behavior, true, ct);
        public Task<T?> QuerySingleAsync(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo<QueryCommand<T>, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QuerySingleAsync(parser, cache, behavior, false, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo<QueryCommand<T>, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QueryMultipleAsync(parser, cache, behavior, true, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo<QueryCommand<T>, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QueryMultipleAsync(parser, cache, behavior, false, ct);



        public T? QuerySingle(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo<QueryCommand<T>, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QuerySingle(parser, cache, behavior, true);
        public T? QuerySingle(IDbConnection cnn, out IDbCommand cmd, IDbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo<QueryCommand<T>, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QuerySingle(parser, cache, behavior, false);
        public IEnumerable<T> QueryMultiple(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo<QueryCommand<T>, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QueryMultiple(parser, cache, behavior, true);
        public IEnumerable<T> QueryMultiple(IDbConnection cnn, out IDbCommand cmd, IDbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo<QueryCommand<T>, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QueryMultiple(parser, cache, behavior, false);

        public Task<T?> QuerySingleAsync(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo<QueryCommand<T>, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QuerySingleAsync(parser, cache, behavior, true, ct);
        public Task<T?> QuerySingleAsync(IDbConnection cnn, out IDbCommand cmd, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo<QueryCommand<T>, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QuerySingleAsync(parser, cache, behavior, false, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo<QueryCommand<T>, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior)
                .QueryMultipleAsync(parser, cache, behavior, true, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync(IDbConnection cnn, out IDbCommand cmd, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo<QueryCommand<T>, T>(cnn, transaction, timeout, out var cache, out var parser, out var behavior))
                .QueryMultipleAsync(parser, cache, behavior, false, ct);
    }
    extension(QueryBuilder<QueryCommand> builder) {
        public int Execute(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null)
            => Execute(builder.QueryCommand, builder.Variables, cnn.CreateCommand(), transaction, timeout, true);
        public int Execute(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null)
            => Execute(builder.QueryCommand, builder.Variables, cmd = cnn.CreateCommand(), transaction, timeout, false);
        public Task<int> ExecuteAsync(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => ExecuteAsync(builder.QueryCommand, builder.Variables, cnn.CreateCommand(), transaction, timeout, true, ct);
        public Task<int> ExecuteAsync(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => ExecuteAsync(builder.QueryCommand, builder.Variables, cmd = cnn.CreateCommand(), transaction, timeout, false, ct);


        public T? QuerySingle<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache)
                .QuerySingle<T>(null, cache, default, true);
        public T? QuerySingle<T>(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache))
                .QuerySingle<T>(null, cache, default, false);
        public IEnumerable<T> QueryMultiple<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache)
                .QueryMultiple<T>(null, cache, default, true);
        public IEnumerable<T> QueryMultiple<T>(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache))
                .QueryMultiple<T>(null, cache, default, false);

        public Task<T?> QuerySingleAsync<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache)
                .QuerySingleAsync<T>(null, cache, default, true, ct);
        public Task<T?> QuerySingleAsync<T>(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache))
                .QuerySingleAsync<T>(null, cache, default, false, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache)
                .QueryMultipleAsync<T>(null, cache, default, true, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache))
                .QueryMultipleAsync<T>(null, cache, default, false, ct);



        public T? QuerySingle<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache)
                .QuerySingle<T>(null, cache, default, true);
        public T? QuerySingle<T>(IDbConnection cnn, out IDbCommand cmd, IDbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache))
                .QuerySingle<T>(null, cache, default, false);
        public IEnumerable<T> QueryMultiple<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache)
                .QueryMultiple<T>(null, cache, default, true);
        public IEnumerable<T> QueryMultiple<T>(IDbConnection cnn, out IDbCommand cmd, IDbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache))
                .QueryMultiple<T>(null, cache, default, false);

        public Task<T?> QuerySingleAsync<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache)
                .QuerySingleAsync<T>(null, cache, default, true, ct);
        public Task<T?> QuerySingleAsync<T>(IDbConnection cnn, out IDbCommand cmd, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache))
                .QuerySingleAsync<T>(null, cache, default, false, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache)
                .QueryMultipleAsync<T>(null, cache, default, true, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(IDbConnection cnn, out IDbCommand cmd, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache))
                .QueryMultipleAsync<T>(null, cache, default, false, ct);
    }
    extension<TQuery>(QueryBuilder<TQuery> builder) where TQuery : QueryCommand {

        public T? QuerySingle<T>(DbConnection cnn, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache)
                .QuerySingle(func, cache, defaultBehavior, true);
        public T? QuerySingle<T>(DbConnection cnn, out DbCommand cmd, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache))
                .QuerySingle(func, cache, defaultBehavior, false);
        public IEnumerable<T> QueryMultiple<T>(DbConnection cnn, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache)
                .QueryMultiple(func, cache, defaultBehavior, true);
        public IEnumerable<T> QueryMultiple<T>(DbConnection cnn, out DbCommand cmd, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache))
                .QueryMultiple(func, cache, defaultBehavior, false);

        public Task<T?> QuerySingleAsync<T>(DbConnection cnn, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache)
                .QuerySingleAsync(func, cache, defaultBehavior, true, ct);
        public Task<T?> QuerySingleAsync<T>(DbConnection cnn, out DbCommand cmd, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache))
                .QuerySingleAsync(func, cache, defaultBehavior, false, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(DbConnection cnn, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache)
                .QueryMultipleAsync(func, cache, defaultBehavior, true, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(DbConnection cnn, out DbCommand cmd, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache))
                .QueryMultipleAsync(func, cache, defaultBehavior, false, ct);



        public T? QuerySingle<T>(IDbConnection cnn, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache)
                .QuerySingle(func, cache, defaultBehavior, true);
        public T? QuerySingle<T>(IDbConnection cnn, out IDbCommand cmd, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache))
                .QuerySingle(func, cache, defaultBehavior, false);
        public IEnumerable<T> QueryMultiple<T>(IDbConnection cnn, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null)
            => builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache)
                .QueryMultiple(func, cache, defaultBehavior, true);
        public IEnumerable<T> QueryMultiple<T>(IDbConnection cnn, out IDbCommand cmd, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null)
            => (cmd = builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache))
                .QueryMultiple(func, cache, defaultBehavior, false);

        public Task<T?> QuerySingleAsync<T>(IDbConnection cnn, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache)
                .QuerySingleAsync(func, cache, defaultBehavior, true, ct);
        public Task<T?> QuerySingleAsync<T>(IDbConnection cnn, out IDbCommand cmd, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache))
                .QuerySingleAsync(func, cache, defaultBehavior, false, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(IDbConnection cnn, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache)
                .QueryMultipleAsync(func, cache, defaultBehavior, true, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(IDbConnection cnn, out IDbCommand cmd, Func<DbDataReader, T>? func = null, CommandBehavior defaultBehavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => (cmd = builder.GetCommandAndInfo(cnn, transaction, timeout, out var cache))
                .QueryMultipleAsync(func, cache, defaultBehavior, false, ct);

        public int Execute(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null)
            => Execute(builder.QueryCommand, builder.Variables, cnn.CreateCommand(), transaction, timeout, true);
        public int Execute(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null)
            => Execute(builder.QueryCommand, builder.Variables, cmd = cnn.CreateCommand(), transaction, timeout, false);
        public Task<int> ExecuteAsync(DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => ExecuteAsync(builder.QueryCommand, builder.Variables, cnn.CreateCommand(), transaction, timeout, true, ct);
        public Task<int> ExecuteAsync(DbConnection cnn, out DbCommand cmd, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
            => ExecuteAsync(builder.QueryCommand, builder.Variables, cmd = cnn.CreateCommand(), transaction, timeout, false, ct);
    }
}
