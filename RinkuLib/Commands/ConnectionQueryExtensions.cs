using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using RinkuLib.Queries;

namespace RinkuLib.Commands;

/// <summary>
/// Runs a query straight from its SQL string, no <see cref="QueryCommand"/> to declare first. The command is
/// built once and cached by the exact string, so repeating that string reuses it. Every result shape the
/// declared form offers has a string counterpart here. Reach for <see cref="GetOrCreateCommand"/> when you
/// want to hold and configure the cached command.
/// </summary>
public static class ConnectionQueryExtensions {
    private static readonly ConcurrentDictionary<string, QueryCommand> _commandCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the shared <see cref="QueryCommand"/> for <paramref name="sql"/>, the same instance the
    /// string-form <c>cnn.Query&lt;T&gt;(sql, ...)</c> calls reuse, creating and caching it on first request.
    /// Hold onto it to own and configure it, its parameter metadata cache and the rest, exactly as with a
    /// declared <see cref="QueryCommand"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static QueryCommand GetOrCreateCommand(string sql) => _commandCache.GetOrAdd(sql, s => new QueryCommand(s));

    #region DbConnection - Object Parameters
    /// <summary>Runs <paramref name="sql"/> and returns the affected-row count.</summary>
    public static int Execute(this DbConnection cnn, string sql, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) =>
        GetOrCreateCommand(sql).Execute(cnn, parametersObj, transaction, timeout);
    /// <summary>Runs <paramref name="sql"/> and returns the affected-row count, handing back the command in <paramref name="cmd"/> to read output parameters and dispose.</summary>
    public static int Execute(this DbConnection cnn, string sql, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) =>
        GetOrCreateCommand(sql).Execute(cnn, out cmd, parametersObj, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> and returns the affected-row count.</summary>
    public static Task<int> ExecuteAsync(this DbConnection cnn, string sql, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) =>
        GetOrCreateCommand(sql).ExecuteAsync(cnn, parametersObj, transaction, timeout, ct);
    /// <summary>Asynchronously runs <paramref name="sql"/> and returns the affected-row count, handing back the command in <paramref name="cmd"/> to read output parameters and dispose.</summary>
    public static Task<int> ExecuteAsync(this DbConnection cnn, string sql, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) =>
        GetOrCreateCommand(sql).ExecuteAsync(cnn, out cmd, parametersObj, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> and returns the first column of the first row as <typeparamref name="T"/>.</summary>
    public static T? ExecuteScalar<T>(this DbConnection cnn, string sql, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) =>
        GetOrCreateCommand(sql).ExecuteScalar<T>(cnn, parametersObj, transaction, timeout);
    /// <summary>Runs <paramref name="sql"/> and returns the first column of the first row as <typeparamref name="T"/>, handing back the command in <paramref name="cmd"/> to read output parameters and dispose.</summary>
    public static T? ExecuteScalar<T>(this DbConnection cnn, string sql, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) =>
        GetOrCreateCommand(sql).ExecuteScalar<T>(cnn, out cmd, parametersObj, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> and returns the first column of the first row as <typeparamref name="T"/>.</summary>
    public static Task<T?> ExecuteScalarAsync<T>(this DbConnection cnn, string sql, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) =>
        GetOrCreateCommand(sql).ExecuteScalarAsync<T>(cnn, parametersObj, transaction, timeout, ct);
    /// <summary>Asynchronously runs <paramref name="sql"/> and returns the first column of the first row as <typeparamref name="T"/>, handing back the command in <paramref name="cmd"/> to read output parameters and dispose.</summary>
    public static Task<T?> ExecuteScalarAsync<T>(this DbConnection cnn, string sql, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) =>
        GetOrCreateCommand(sql).ExecuteScalarAsync<T>(cnn, out cmd, parametersObj, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> and returns its <see cref="DbDataReader"/>, with the owning command in <paramref name="cmd"/>.</summary>
    public static DbDataReader ExecuteReader(this DbConnection cnn, string sql, out DbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) =>
        GetOrCreateCommand(sql).ExecuteReader(cnn, out cmd, parametersObj, behavior, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> and returns its <see cref="DbDataReader"/>, with the owning command in <paramref name="cmd"/>.</summary>
    public static Task<DbDataReader> ExecuteReaderAsync(this DbConnection cnn, string sql, out DbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) =>
        GetOrCreateCommand(sql).ExecuteReaderAsync(cnn, out cmd, parametersObj, behavior, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> and returns a <see cref="MultiReader"/> over its result sets, with the owning command in <paramref name="cmd"/>.</summary>
    public static MultiReader ExecuteMultiReader(this DbConnection cnn, string sql, out DbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) =>
        GetOrCreateCommand(sql).ExecuteMultiReader(cnn, out cmd, parametersObj, behavior, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> and returns a <see cref="MultiReader"/> over its result sets, with the owning command in <paramref name="cmd"/>.</summary>
    public static Task<MultiReader> ExecuteMultiReaderAsync(this DbConnection cnn, string sql, out DbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) =>
        GetOrCreateCommand(sql).ExecuteMultiReaderAsync(cnn, out cmd, parametersObj, behavior, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> and reads the result as <typeparamref name="T"/>, in the shape <typeparamref name="T"/> chooses.</summary>
    public static T Query<T>(this DbConnection cnn, string sql, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) =>
        GetOrCreateCommand(sql).Query<T>(cnn, parametersObj, transaction, timeout);
    /// <summary>Runs <paramref name="sql"/> and reads the result as <typeparamref name="T"/>, handing back the command in <paramref name="cmd"/> to read output parameters and dispose.</summary>
    public static T Query<T>(this DbConnection cnn, string sql, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null) =>
        GetOrCreateCommand(sql).Query<T>(cnn, out cmd, parametersObj, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> and reads the result as <typeparamref name="T"/>, in the shape <typeparamref name="T"/> chooses.</summary>
    public static Task<T> QueryAsync<T>(this DbConnection cnn, string sql, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) =>
        GetOrCreateCommand(sql).QueryAsync<T>(cnn, parametersObj, transaction, timeout, ct);
    /// <summary>Asynchronously runs <paramref name="sql"/> and reads the result as <typeparamref name="T"/>, handing back the command in <paramref name="cmd"/> to read output parameters and dispose.</summary>
    public static Task<T> QueryAsync<T>(this DbConnection cnn, string sql, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) =>
        GetOrCreateCommand(sql).QueryAsync<T>(cnn, out cmd, parametersObj, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> and streams its rows as <typeparamref name="T"/>, produced one at a time as you enumerate.</summary>
    public static IAsyncEnumerable<T> StreamQueryAsync<T>(this DbConnection cnn, string sql, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) =>
        GetOrCreateCommand(sql).StreamQueryAsync<T>(cnn, parametersObj, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> and streams its rows as <typeparamref name="T"/>, handing back the command in <paramref name="cmd"/> to read output parameters and dispose once enumeration completes.</summary>
    public static IAsyncEnumerable<T> StreamQueryAsync<T>(this DbConnection cnn, string sql, out DbCommand cmd, object? parametersObj = null, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) =>
        GetOrCreateCommand(sql).StreamQueryAsync<T>(cnn, out cmd, parametersObj, transaction, timeout, ct);

    #endregion

    #region IDbConnection - Object Parameters
    /// <summary>Runs <paramref name="sql"/> and returns the affected-row count.</summary>
    public static int Execute(this IDbConnection cnn, string sql, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) =>
        GetOrCreateCommand(sql).Execute(cnn, parametersObj, transaction, timeout);
    /// <summary>Runs <paramref name="sql"/> and returns the affected-row count, handing back the command in <paramref name="cmd"/> to read output parameters and dispose.</summary>
    public static int Execute(this IDbConnection cnn, string sql, out IDbCommand cmd, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) =>
        GetOrCreateCommand(sql).Execute(cnn, out cmd, parametersObj, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> and returns the affected-row count.</summary>
    public static Task<int> ExecuteAsync(this IDbConnection cnn, string sql, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) =>
        GetOrCreateCommand(sql).ExecuteAsync(cnn, parametersObj, transaction, timeout, ct);
    /// <summary>Asynchronously runs <paramref name="sql"/> and returns the affected-row count, handing back the command in <paramref name="cmd"/> to read output parameters and dispose.</summary>
    public static Task<int> ExecuteAsync(this IDbConnection cnn, string sql, out IDbCommand cmd, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) =>
        GetOrCreateCommand(sql).ExecuteAsync(cnn, out cmd, parametersObj, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> and returns the first column of the first row as <typeparamref name="T"/>.</summary>
    public static T? ExecuteScalar<T>(this IDbConnection cnn, string sql, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) =>
        GetOrCreateCommand(sql).ExecuteScalar<T>(cnn, parametersObj, transaction, timeout);
    /// <summary>Runs <paramref name="sql"/> and returns the first column of the first row as <typeparamref name="T"/>, handing back the command in <paramref name="cmd"/> to read output parameters and dispose.</summary>
    public static T? ExecuteScalar<T>(this IDbConnection cnn, string sql, out IDbCommand cmd, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) =>
        GetOrCreateCommand(sql).ExecuteScalar<T>(cnn, out cmd, parametersObj, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> and returns the first column of the first row as <typeparamref name="T"/>.</summary>
    public static Task<T?> ExecuteScalarAsync<T>(this IDbConnection cnn, string sql, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) =>
        GetOrCreateCommand(sql).ExecuteScalarAsync<T>(cnn, parametersObj, transaction, timeout, ct);
    /// <summary>Asynchronously runs <paramref name="sql"/> and returns the first column of the first row as <typeparamref name="T"/>, handing back the command in <paramref name="cmd"/> to read output parameters and dispose.</summary>
    public static Task<T?> ExecuteScalarAsync<T>(this IDbConnection cnn, string sql, out IDbCommand cmd, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) =>
        GetOrCreateCommand(sql).ExecuteScalarAsync<T>(cnn, out cmd, parametersObj, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> and returns its <see cref="DbDataReader"/>, with the owning command in <paramref name="cmd"/>.</summary>
    public static DbDataReader ExecuteReader(this IDbConnection cnn, string sql, out IDbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) =>
        GetOrCreateCommand(sql).ExecuteReader(cnn, out cmd, parametersObj, behavior, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> and returns its <see cref="DbDataReader"/>, with the owning command in <paramref name="cmd"/>.</summary>
    public static Task<DbDataReader> ExecuteReaderAsync(this IDbConnection cnn, string sql, out IDbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) =>
        GetOrCreateCommand(sql).ExecuteReaderAsync(cnn, out cmd, parametersObj, behavior, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> and returns a <see cref="MultiReader"/> over its result sets, with the owning command in <paramref name="cmd"/>.</summary>
    public static MultiReader ExecuteMultiReader(this IDbConnection cnn, string sql, out IDbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) =>
        GetOrCreateCommand(sql).ExecuteMultiReader(cnn, out cmd, parametersObj, behavior, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> and returns a <see cref="MultiReader"/> over its result sets, with the owning command in <paramref name="cmd"/>.</summary>
    public static Task<MultiReader> ExecuteMultiReaderAsync(this IDbConnection cnn, string sql, out IDbCommand cmd, object? parametersObj = null, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) =>
        GetOrCreateCommand(sql).ExecuteMultiReaderAsync(cnn, out cmd, parametersObj, behavior, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> and reads the result as <typeparamref name="T"/>, in the shape <typeparamref name="T"/> chooses.</summary>
    public static T Query<T>(this IDbConnection cnn, string sql, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) =>
        GetOrCreateCommand(sql).Query<T>(cnn, parametersObj, transaction, timeout);
    /// <summary>Runs <paramref name="sql"/> and reads the result as <typeparamref name="T"/>, handing back the command in <paramref name="cmd"/> to read output parameters and dispose.</summary>
    public static T Query<T>(this IDbConnection cnn, string sql, out IDbCommand cmd, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null) =>
        GetOrCreateCommand(sql).Query<T>(cnn, out cmd, parametersObj, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> and reads the result as <typeparamref name="T"/>, in the shape <typeparamref name="T"/> chooses.</summary>
    public static Task<T> QueryAsync<T>(this IDbConnection cnn, string sql, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) =>
        GetOrCreateCommand(sql).QueryAsync<T>(cnn, parametersObj, transaction, timeout, ct);
    /// <summary>Asynchronously runs <paramref name="sql"/> and reads the result as <typeparamref name="T"/>, handing back the command in <paramref name="cmd"/> to read output parameters and dispose.</summary>
    public static Task<T> QueryAsync<T>(this IDbConnection cnn, string sql, out IDbCommand cmd, object? parametersObj = null, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) =>
        GetOrCreateCommand(sql).QueryAsync<T>(cnn, out cmd, parametersObj, transaction, timeout, ct);

    #endregion

    #region DbConnection - Generic Parameters
    /// <summary>Runs <paramref name="sql"/> with a statically typed parameter object and returns the affected-row count.</summary>
    public static int Execute<TObj>(this DbConnection cnn, string sql, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull =>
        GetOrCreateCommand(sql).Execute(cnn, parametersObj, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> with a statically typed parameter object and returns the affected-row count.</summary>
    public static Task<int> ExecuteAsync<TObj>(this DbConnection cnn, string sql, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteAsync(cnn, parametersObj, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> with a statically typed parameter object and returns the first column of the first row as <typeparamref name="T"/>.</summary>
    public static T? ExecuteScalar<T, TObj>(this DbConnection cnn, string sql, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteScalar<T, TObj>(cnn, parametersObj, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> with a statically typed parameter object and returns the first column of the first row as <typeparamref name="T"/>.</summary>
    public static Task<T?> ExecuteScalarAsync<T, TObj>(this DbConnection cnn, string sql, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteScalarAsync<T, TObj>(cnn, parametersObj, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> with a statically typed parameter object and returns its <see cref="DbDataReader"/>, with the owning command in <paramref name="cmd"/>.</summary>
    public static DbDataReader ExecuteReader<TObj>(this DbConnection cnn, string sql, out DbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteReader(cnn, out cmd, parametersObj, behavior, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> with a statically typed parameter object and returns its <see cref="DbDataReader"/>, with the owning command in <paramref name="cmd"/>.</summary>
    public static Task<DbDataReader> ExecuteReaderAsync<TObj>(this DbConnection cnn, string sql, out DbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteReaderAsync(cnn, out cmd, parametersObj, behavior, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> with a statically typed parameter object and returns a <see cref="MultiReader"/> over its result sets, with the owning command in <paramref name="cmd"/>.</summary>
    public static MultiReader ExecuteMultiReader<TObj>(this DbConnection cnn, string sql, out DbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteMultiReader(cnn, out cmd, parametersObj, behavior, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> with a statically typed parameter object and returns a <see cref="MultiReader"/> over its result sets, with the owning command in <paramref name="cmd"/>.</summary>
    public static Task<MultiReader> ExecuteMultiReaderAsync<TObj>(this DbConnection cnn, string sql, out DbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteMultiReaderAsync(cnn, out cmd, parametersObj, behavior, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> with a statically typed parameter object and reads the result as <typeparamref name="T"/>, in the shape <typeparamref name="T"/> chooses.</summary>
    public static T Query<T, TObj>(this DbConnection cnn, string sql, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull =>
        GetOrCreateCommand(sql).Query<T, TObj>(cnn, parametersObj, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> with a statically typed parameter object and reads the result as <typeparamref name="T"/>, in the shape <typeparamref name="T"/> chooses.</summary>
    public static Task<T> QueryAsync<T, TObj>(this DbConnection cnn, string sql, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).QueryAsync<T, TObj>(cnn, parametersObj, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> with a statically typed parameter object and streams its rows as <typeparamref name="T"/>, produced one at a time as you enumerate.</summary>
    public static IAsyncEnumerable<T> StreamQueryAsync<T, TObj>(this DbConnection cnn, string sql, TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).StreamQueryAsync<T, TObj>(cnn, parametersObj, transaction, timeout, ct);

    #endregion

    #region IDbConnection - Generic Parameters
    /// <summary>Runs <paramref name="sql"/> with a statically typed parameter object and returns the affected-row count.</summary>
    public static int Execute<TObj>(this IDbConnection cnn, string sql, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull =>
        GetOrCreateCommand(sql).Execute(cnn, parametersObj, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> with a statically typed parameter object and returns the affected-row count.</summary>
    public static Task<int> ExecuteAsync<TObj>(this IDbConnection cnn, string sql, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteAsync(cnn, parametersObj, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> with a statically typed parameter object and returns the first column of the first row as <typeparamref name="T"/>.</summary>
    public static T? ExecuteScalar<T, TObj>(this IDbConnection cnn, string sql, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteScalar<T, TObj>(cnn, parametersObj, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> with a statically typed parameter object and returns the first column of the first row as <typeparamref name="T"/>.</summary>
    public static Task<T?> ExecuteScalarAsync<T, TObj>(this IDbConnection cnn, string sql, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteScalarAsync<T, TObj>(cnn, parametersObj, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> with a statically typed parameter object and returns its <see cref="DbDataReader"/>, with the owning command in <paramref name="cmd"/>.</summary>
    public static DbDataReader ExecuteReader<TObj>(this IDbConnection cnn, string sql, out IDbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteReader(cnn, out cmd, parametersObj, behavior, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> with a statically typed parameter object and returns its <see cref="DbDataReader"/>, with the owning command in <paramref name="cmd"/>.</summary>
    public static Task<DbDataReader> ExecuteReaderAsync<TObj>(this IDbConnection cnn, string sql, out IDbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteReaderAsync(cnn, out cmd, parametersObj, behavior, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> with a statically typed parameter object and returns a <see cref="MultiReader"/> over its result sets, with the owning command in <paramref name="cmd"/>.</summary>
    public static MultiReader ExecuteMultiReader<TObj>(this IDbConnection cnn, string sql, out IDbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteMultiReader(cnn, out cmd, parametersObj, behavior, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> with a statically typed parameter object and returns a <see cref="MultiReader"/> over its result sets, with the owning command in <paramref name="cmd"/>.</summary>
    public static Task<MultiReader> ExecuteMultiReaderAsync<TObj>(this IDbConnection cnn, string sql, out IDbCommand cmd, TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteMultiReaderAsync(cnn, out cmd, parametersObj, behavior, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> with a statically typed parameter object and reads the result as <typeparamref name="T"/>, in the shape <typeparamref name="T"/> chooses.</summary>
    public static T Query<T, TObj>(this IDbConnection cnn, string sql, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull =>
        GetOrCreateCommand(sql).Query<T, TObj>(cnn, parametersObj, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> with a statically typed parameter object and reads the result as <typeparamref name="T"/>, in the shape <typeparamref name="T"/> chooses.</summary>
    public static Task<T> QueryAsync<T, TObj>(this IDbConnection cnn, string sql, TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).QueryAsync<T, TObj>(cnn, parametersObj, transaction, timeout, ct);

    #endregion

    #region DbConnection - Ref Generic Parameters
    /// <summary>Runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and returns the affected-row count.</summary>
    public static int Execute<TObj>(this DbConnection cnn, string sql, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull =>
        GetOrCreateCommand(sql).Execute(cnn, ref parametersObj, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and returns the affected-row count.</summary>
    public static Task<int> ExecuteAsync<TObj>(this DbConnection cnn, string sql, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteAsync(cnn, ref parametersObj, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and returns the first column of the first row as <typeparamref name="T"/>.</summary>
    public static T? ExecuteScalar<T, TObj>(this DbConnection cnn, string sql, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteScalar<T, TObj>(cnn, ref parametersObj, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and returns the first column of the first row as <typeparamref name="T"/>.</summary>
    public static Task<T?> ExecuteScalarAsync<T, TObj>(this DbConnection cnn, string sql, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteScalarAsync<T, TObj>(cnn, ref parametersObj, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and returns its <see cref="DbDataReader"/>, with the owning command in <paramref name="cmd"/>.</summary>
    public static DbDataReader ExecuteReader<TObj>(this DbConnection cnn, string sql, out DbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteReader(cnn, out cmd, ref parametersObj, behavior, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and returns its <see cref="DbDataReader"/>, with the owning command in <paramref name="cmd"/>.</summary>
    public static Task<DbDataReader> ExecuteReaderAsync<TObj>(this DbConnection cnn, string sql, out DbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteReaderAsync(cnn, out cmd, ref parametersObj, behavior, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and returns a <see cref="MultiReader"/> over its result sets, with the owning command in <paramref name="cmd"/>.</summary>
    public static MultiReader ExecuteMultiReader<TObj>(this DbConnection cnn, string sql, out DbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteMultiReader(cnn, out cmd, ref parametersObj, behavior, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and returns a <see cref="MultiReader"/> over its result sets, with the owning command in <paramref name="cmd"/>.</summary>
    public static Task<MultiReader> ExecuteMultiReaderAsync<TObj>(this DbConnection cnn, string sql, out DbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteMultiReaderAsync(cnn, out cmd, ref parametersObj, behavior, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and reads the result as <typeparamref name="T"/>, in the shape <typeparamref name="T"/> chooses.</summary>
    public static T Query<T, TObj>(this DbConnection cnn, string sql, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null) where TObj : notnull =>
        GetOrCreateCommand(sql).Query<T, TObj>(cnn, ref parametersObj, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and reads the result as <typeparamref name="T"/>, in the shape <typeparamref name="T"/> chooses.</summary>
    public static Task<T> QueryAsync<T, TObj>(this DbConnection cnn, string sql, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).QueryAsync<T, TObj>(cnn, ref parametersObj, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and streams its rows as <typeparamref name="T"/>, produced one at a time as you enumerate.</summary>
    public static IAsyncEnumerable<T> StreamQueryAsync<T, TObj>(this DbConnection cnn, string sql, ref TObj parametersObj, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).StreamQueryAsync<T, TObj>(cnn, ref parametersObj, transaction, timeout, ct);

    #endregion

    #region IDbConnection - Ref Generic Parameters
    /// <summary>Runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and returns the affected-row count.</summary>
    public static int Execute<TObj>(this IDbConnection cnn, string sql, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull =>
        GetOrCreateCommand(sql).Execute(cnn, ref parametersObj, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and returns the affected-row count.</summary>
    public static Task<int> ExecuteAsync<TObj>(this IDbConnection cnn, string sql, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteAsync(cnn, ref parametersObj, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and returns the first column of the first row as <typeparamref name="T"/>.</summary>
    public static T? ExecuteScalar<T, TObj>(this IDbConnection cnn, string sql, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteScalar<T, TObj>(cnn, ref parametersObj, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and returns the first column of the first row as <typeparamref name="T"/>.</summary>
    public static Task<T?> ExecuteScalarAsync<T, TObj>(this IDbConnection cnn, string sql, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteScalarAsync<T, TObj>(cnn, ref parametersObj, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and returns its <see cref="DbDataReader"/>, with the owning command in <paramref name="cmd"/>.</summary>
    public static DbDataReader ExecuteReader<TObj>(this IDbConnection cnn, string sql, out IDbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteReader(cnn, out cmd, ref parametersObj, behavior, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and returns its <see cref="DbDataReader"/>, with the owning command in <paramref name="cmd"/>.</summary>
    public static Task<DbDataReader> ExecuteReaderAsync<TObj>(this IDbConnection cnn, string sql, out IDbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteReaderAsync(cnn, out cmd, ref parametersObj, behavior, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and returns a <see cref="MultiReader"/> over its result sets, with the owning command in <paramref name="cmd"/>.</summary>
    public static MultiReader ExecuteMultiReader<TObj>(this IDbConnection cnn, string sql, out IDbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteMultiReader(cnn, out cmd, ref parametersObj, behavior, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and returns a <see cref="MultiReader"/> over its result sets, with the owning command in <paramref name="cmd"/>.</summary>
    public static Task<MultiReader> ExecuteMultiReaderAsync<TObj>(this IDbConnection cnn, string sql, out IDbCommand cmd, ref TObj parametersObj, CommandBehavior behavior = default, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).ExecuteMultiReaderAsync(cnn, out cmd, ref parametersObj, behavior, transaction, timeout, ct);
    /// <summary>Runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and reads the result as <typeparamref name="T"/>, in the shape <typeparamref name="T"/> chooses.</summary>
    public static T Query<T, TObj>(this IDbConnection cnn, string sql, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null) where TObj : notnull =>
        GetOrCreateCommand(sql).Query<T, TObj>(cnn, ref parametersObj, transaction, timeout);
    /// <summary>Asynchronously runs <paramref name="sql"/> taking the parameter object by reference to avoid a copy, and reads the result as <typeparamref name="T"/>, in the shape <typeparamref name="T"/> chooses.</summary>
    public static Task<T> QueryAsync<T, TObj>(this IDbConnection cnn, string sql, ref TObj parametersObj, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TObj : notnull =>
        GetOrCreateCommand(sql).QueryAsync<T, TObj>(cnn, ref parametersObj, transaction, timeout, ct);

    #endregion
}