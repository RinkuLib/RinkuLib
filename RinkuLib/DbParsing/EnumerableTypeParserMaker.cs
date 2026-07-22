using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.DbParsing;

/// <summary>
/// The maker for <see cref="IEnumerable{T}"/>. It builds its parsers the way any single-element shape
/// does, and it also takes the first run, the one with no parser yet, because the rows of this shape are
/// read as they are walked and opening the reader to learn a parser is what runs the command.
/// </summary>
/// <remarks>
/// Taking that run means standing in for <c>Query</c> itself, once. From the run on there is a parser, so
/// every later call goes straight to it and never reaches here.
/// <para>
/// The two roads part on what they can wait for. The synchronous one holds everything back to the walk,
/// since a walk is all it has. The asynchronous one opens the connection, runs the command and learns the
/// parser while it can await, and hands back the rows over the reader it left open. The rows are walked
/// synchronously either way, which is what asking for an <see cref="IEnumerable{T}"/> asks for.
/// </para>
/// </remarks>
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

    /// <summary>
    /// Closes the element type onto the run and hands back what it makes. The command goes through as an
    /// object either way, so the two command kinds take the one road and part inside it, where the reader
    /// is opened.
    /// </summary>
    private bool TrySync<T>(IDbCommand cmd, ICacheGivingParser<T> cache, bool disposeCommand, [MaybeNullWhen(false)] out T result) {
        if (!CanHandle<T>()) {
            result = default;
            return false;
        }
        result = (T)Rows.MakeGenericMethod(typeof(T).GetGenericArguments()[0])
            .Invoke(null, [cmd, cache, disposeCommand])!;
        return true;
    }
    /// <inheritdoc cref="TrySync{T}"/>
    private bool TryAsync<T>(IDbCommand cmd, ICacheGivingParser<T> cache, bool disposeCommand, CancellationToken ct, [MaybeNullWhen(false)] out Task<T> result) {
        if (!CanHandle<T>()) {
            result = null;
            return false;
        }
        result = (Task<T>)RowsAsync.MakeGenericMethod(typeof(T).GetGenericArguments()[0])
            .Invoke(null, [cmd, cache, disposeCommand, ct])!;
        return true;
    }

    /// <summary>
    /// The whole run held back to the walk. Nothing is opened until the first row is asked for, so a result
    /// nobody walks leaves the database alone.
    /// </summary>
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
            if (reader.Read())
                foreach (var item in parser.Parse(reader).Result)
                    yield return item;
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

    /// <summary>
    /// The opening, the run and the learning done while there is something to await, and the rows handed
    /// back over the reader it left open. Walking them out, or leaving the walk early, is what closes it.
    /// </summary>
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
