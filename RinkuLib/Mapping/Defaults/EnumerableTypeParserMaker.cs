using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Rinku.Internal;
using Rinku.Mapping.Parsers;
using Rinku.Mapping.Parsers.Defaults;

namespace Rinku.Mapping.Defaults;

/// <summary>
/// Creates parsers for <see cref="IEnumerable{T}"/> results.
/// The returned sequence runs the query as it is enumerated.
/// An asynchronous query opens the reader asynchronously before returning the sequence.
/// Use an asynchronous stream when every row must be read asynchronously.
/// </summary>
public sealed class EnumerableTypeParserMaker() : ReusingBaseTypeParserMaker(
    [typeof(IEnumerable<>)],
    (def, itemType, ref _) => typeof(EnumerableTypeParser<>).MakeGenericType(itemType),
    (def, itemType, ref _) => typeof(FastEnumerableTypeParser<>).MakeGenericType(itemType)), ITypeParserMaker {

    private static readonly MethodInfo Rows = Road(nameof(ColdRows));
    private static readonly MethodInfo RowsAsync = Road(nameof(ColdRowsAsync));
    private static MethodInfo Road(string name)
        => typeof(EnumerableTypeParserMaker).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <inheritdoc/>
    public bool TryColdStart<T>(DbCommand cmd, ICacheGivingParser<T> cache, bool disposeCommand, [MaybeNullWhen(false)] out T result)
        => TrySync(cmd, cache, disposeCommand, out result);
    /// <inheritdoc/>
    public bool TryColdStart<T>(IDbCommand cmd, ICacheGivingParser<T> cache, bool disposeCommand, [MaybeNullWhen(false)] out T result)
        => TrySync(cmd, cache, disposeCommand, out result);
    /// <inheritdoc/>
    public bool TryColdStartAsync<T>(DbCommand cmd, ICacheGivingParser<T> cache, bool disposeCommand, CancellationToken ct, [MaybeNullWhen(false)] out Task<T> result)
        => TryAsync(cmd, cache, disposeCommand, ct, out result);
    /// <inheritdoc cref="TryColdStartAsync{T}(DbCommand, ICacheGivingParser{T}, bool, CancellationToken, out Task{T})"/>
    public bool TryColdStartAsync<T>(IDbCommand cmd, ICacheGivingParser<T> cache, bool disposeCommand, CancellationToken ct, [MaybeNullWhen(false)] out Task<T> result)
        => TryAsync(cmd, cache, disposeCommand, ct, out result);

    /// <inheritdoc/>
    public bool TryColdStart(Type type, DbCommand cmd, ICacheGivingParser cache, bool disposeCommand, [MaybeNullWhen(false)] out object? result)
        => TrySync(type, cmd, cache, disposeCommand, out result);
    /// <inheritdoc/>
    public bool TryColdStart(Type type, IDbCommand cmd, ICacheGivingParser cache, bool disposeCommand, [MaybeNullWhen(false)] out object? result)
        => TrySync(type, cmd, cache, disposeCommand, out result);
    /// <inheritdoc/>
    public bool TryColdStartAsync(Type type, DbCommand cmd, ICacheGivingParser cache, bool disposeCommand, CancellationToken ct, [MaybeNullWhen(false)] out Task<object?>? result)
        => TryAsync(type, cmd, cache, disposeCommand, ct, out result);
    /// <inheritdoc/>
    public bool TryColdStartAsync(Type type, IDbCommand cmd, ICacheGivingParser cache, bool disposeCommand, CancellationToken ct, [MaybeNullWhen(false)] out Task<object?>? result)
        => TryAsync(type, cmd, cache, disposeCommand, ct, out result);

    private bool TrySync<T>(IDbCommand cmd, ICacheGivingParser<T> cache, bool disposeCommand, [MaybeNullWhen(false)] out T result) {
        if (!CanHandle<T>()) {
            result = default;
            return false;
        }
        result = (T)Rows.MakeGenericMethod(typeof(T).GetGenericArguments()[0])
            .Invoke(null, [cmd, cache, disposeCommand])!;
        return true;
    }
    private bool TryAsync<T>(IDbCommand cmd, ICacheGivingParser<T> cache, bool disposeCommand, CancellationToken ct, [MaybeNullWhen(false)] out Task<T> result) {
        if (!CanHandle<T>()) {
            result = null;
            return false;
        }
        result = (Task<T>)RowsAsync.MakeGenericMethod(typeof(T).GetGenericArguments()[0])
            .Invoke(null, [cmd, cache, disposeCommand, ct])!;
        return true;
    }

    private bool TrySync(Type type, IDbCommand cmd, ICacheGivingParser cache, bool disposeCommand, [MaybeNullWhen(false)] out object? result) {
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(IEnumerable<>)) {
            result = null;
            return false;
        }
        result = Rows.MakeGenericMethod(type.GetGenericArguments()[0]).Invoke(null, [cmd, cache, disposeCommand]);
        return true;
    }
    private bool TryAsync(Type type, IDbCommand cmd, ICacheGivingParser cache, bool disposeCommand, CancellationToken ct, [MaybeNullWhen(false)] out Task<object?>? result) {
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(IEnumerable<>)) {
            result = null;
            return false;
        }
        result = ConvertTask(RowsAsync.MakeGenericMethod(type.GetGenericArguments()[0]).Invoke(null, [cmd, cache, disposeCommand, ct])!);
        return true;
    }
    private static async Task<object?> ConvertTask(object task) {
        await ((Task)task).ConfigureAwait(false);
        return task.GetType().GetProperty("Result")!.GetValue(task);
    }

    private static IEnumerable<TItem> ColdRows<TItem>(IDbCommand cmd, ICacheGivingParser<IEnumerable<TItem>> cache, bool disposeCommand) {
        var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
        var wasClosed = cnn.State != ConnectionState.Open;
        try {
            var behavior = cache.Behavior;
            if (wasClosed) {
                cnn.Open();
                behavior |= CommandBehavior.CloseConnection;
            }
            using var reader = cmd is DbCommand c
                ? c.ExecuteReader(behavior)
                : WrappedBasicReader.Wrap(cmd.ExecuteReader(behavior));
            wasClosed = false;
            var parser = cache.UpdateCache(cmd, reader);
            if (reader.Read()) {
                if (parser is BaseEnumerableTypeParser<TItem> streaming) {
                    if (streaming.DirectRowParser is { } rowParser) {
                        do { yield return rowParser(reader); } while (reader.Read());
                    }
                    else {
                        bool canContinue;
                        do {
                            (canContinue, var item) = streaming.ParseCurrent(reader);
                            yield return item;
                        } while (canContinue);
                    }
                }
                else {
                    foreach (var item in parser.Parse(reader).Result)
                        yield return item;
                }
            }
        }
        finally {
            if (wasClosed)
                cnn.Close();
            if (disposeCommand) {
                cmd.Parameters.Clear();
                cmd.Dispose();
            }
        }
    }

    private static async Task<IEnumerable<TItem>> ColdRowsAsync<TItem>(IDbCommand cmd, ICacheGivingParser<IEnumerable<TItem>> cache, bool disposeCommand, CancellationToken ct) {
        var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
        var wasClosed = cnn.State != ConnectionState.Open;
        DbDataReader? reader = null;
        try {
            var behavior = cache.Behavior;
            if (wasClosed) {
                if (cnn is DbConnection c)
                    await c.OpenAsync(ct).ConfigureAwait(false);
                else
                    cnn.Open();
                behavior |= CommandBehavior.CloseConnection;
            }
            reader = cmd is DbCommand dbCmd
                ? await dbCmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false)
                : WrappedBasicReader.Wrap(cmd.ExecuteReader(behavior));
            wasClosed = false;
            var parser = await cache.UpdateCacheAsync(cmd, reader, ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                return parser.Default();
            var holding = parser as IReaderHoldingParser<IEnumerable<TItem>>
                ?? throw new RinkuInternalException(ErrorCodes.InternalInvariant,
                    $"the columns gave {parser.GetType()} for a streamed {typeof(TItem)}, which does not hold a reader");
            var open = reader;
            reader = null;
            return disposeCommand
                ? holding.ParseThen(open, new LetGoOfReaderAndCommand(cmd))
                : holding.ParseThen(open, new LetGoOfReader());
        }
        finally {
            if (reader is not null) {
                if (disposeCommand)
                    new LetGoOfReaderAndCommand(cmd).Invoke(reader);
                else
                    new LetGoOfReader().Invoke(reader);
                if (wasClosed && cnn.State != ConnectionState.Closed)
                    cnn.Close();
            }
        }
    }
}
