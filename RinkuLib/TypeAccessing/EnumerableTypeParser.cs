using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
namespace RinkuLib.TypeAccessing;
/// <summary>
/// The base for the streamed shape, <see cref="IEnumerable{T}"/>, it yields rows as you enumerate rather than
/// buffering them, keeping memory flat on large results. It holds the reader open while you iterate and
/// disposes it when enumeration ends.
/// </summary>
public abstract class BaseEnumerableTypeParser<T> : ITypeParser<IEnumerable<T>>, IReaderHoldingParser<IEnumerable<T>> {
    bool ITypeParser<IEnumerable<T>>.InternalProtect => true;
    /// <summary>
    /// Used to parse a single item. Reports whether the reader is left on an untreated row
    /// </summary>
    protected abstract (bool CanContinue, T Result) ParseOne(DbDataReader reader);
    /// <inheritdoc/>
    public IEnumerable<T> ParseThen<TDone>(DbDataReader reader, TDone onDone) where TDone : IReaderDone {
        try {
            bool canContinue;
            do {
                (canContinue, var item) = ParseOne(reader);
                yield return item;
            } while (canContinue);
        }
        finally {
            onDone.Invoke(reader);
        }
    }
    /// <inheritdoc/>
    public abstract CommandBehavior Behavior { get; }
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerable<T> Default() => [];
    /// <summary>
    /// The enumerable is lazy, rows are read while it is enumerated,
    /// so <c>CanContinue</c> cannot be known up front and is always <see langword="false"/>
    /// </summary>
    public (bool CanContinue, IEnumerable<T> Result) Parse(DbDataReader reader) => (false, ParseRows(reader));
    private IEnumerable<T> ParseRows(DbDataReader reader) {
        bool canContinue;
        do {
            (canContinue, var item) = ParseOne(reader);
            yield return item;
        } while (canContinue);
    }
    /// <inheritdoc cref="Parse"/>
    public ValueTask<(bool CanContinue, IEnumerable<T> Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default)
        => new(Parse(reader));
    /// <inheritdoc/>
    public IEnumerable<T> Query(DbCommand command, bool disposeCommand = false) {
        var cnn = command.Connection ?? throw new RinkuNoConnectionException();
        var wasClosed = cnn.State != ConnectionState.Open;
        try {
            var behavior = Behavior;
            if (wasClosed) {
                cnn.Open();
                behavior |= CommandBehavior.CloseConnection;
            }
            using var reader = command.ExecuteReader(behavior);
            wasClosed = false;
            if (reader.Read()) {
                bool canContinue;
                do {
                    (canContinue, var item) = ParseOne(reader);
                    yield return item;
                } while (canContinue);
            }
        }
        finally {
            if (wasClosed)
                cnn.Close();
            if (disposeCommand) {
                command.Parameters.Clear();
                command.Dispose();
            }
        }
    }
    /// <inheritdoc/>
    public IEnumerable<T> Query(IDbCommand command, bool disposeCommand = false) {
        var cnn = command.Connection ?? throw new RinkuNoConnectionException();
        var wasClosed = cnn.State != ConnectionState.Open;
        try {
            var behavior = Behavior;
            if (wasClosed) {
                cnn.Open();
                behavior |= CommandBehavior.CloseConnection;
            }
            var r = command.ExecuteReader(behavior);
            using var reader = WrappedBasicReader.Wrap(r);
            wasClosed = false;
            if (reader.Read()) {
                bool canContinue;
                do {
                    (canContinue, var item) = ParseOne(reader);
                    yield return item;
                } while (canContinue);
            }
        }
        finally {
            if (disposeCommand) {
                command.Parameters.Clear();
                command.Dispose();
            }
            if (wasClosed)
                cnn.Close();
        }
    }
    /// <inheritdoc/>
    /// <remarks>
    /// The opening, the running and the first row are done while there is something to await, and the rows
    /// are handed back over the reader that leaves open. Walking them out, or leaving the walk early, is
    /// what closes it. The rows themselves come synchronously, which is what an
    /// <see cref="IEnumerable{T}"/> is, <c>StreamQueryAsync</c> being the asynchronous stream.
    /// </remarks>
    public async Task<IEnumerable<T>> QueryAsync(DbCommand command, bool disposeCommand = false, CancellationToken ct = default) {
        var cnn = command.Connection ?? throw new RinkuNoConnectionException();
        var wasClosed = cnn.State != ConnectionState.Open;
        DbDataReader? reader = null;
        try {
            var behavior = Behavior;
            if (wasClosed) {
                await cnn.OpenAsync(ct).ConfigureAwait(false);
                behavior |= CommandBehavior.CloseConnection;
            }
            reader = await command.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
            wasClosed = false;
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                return Default();
            var open = reader;
            reader = null;
            return disposeCommand
                ? ParseThen(open, new LetGoOfReaderAndCommand(command))
                : ParseThen(open, new LetGoOfReader());
        }
        finally {
            if (reader is not null) {
                LetGo(reader, command, disposeCommand);
                if (wasClosed && cnn.State != ConnectionState.Closed)
                    cnn.Close();
            }
        }
    }
    /// <inheritdoc cref="QueryAsync(DbCommand, bool, CancellationToken)"/>
    /// <remarks>
    /// A command that is only an <see cref="IDbCommand"/> has no asynchronous road of its own, so there is
    /// nothing here to await and the whole run waits for the walk.
    /// </remarks>
    public Task<IEnumerable<T>> QueryAsync(IDbCommand command, bool disposeCommand = false, CancellationToken ct = default) {
        if (command is DbCommand c)
            return QueryAsync(c, disposeCommand, ct);
        return Task.FromResult(Query(command, disposeCommand));
    }
    /// <summary>Hands back a reader no result took, and the command when this run owns it.</summary>
    private static void LetGo(DbDataReader reader, IDbCommand command, bool disposeCommand) {
        if (disposeCommand)
            new LetGoOfReaderAndCommand(command).Invoke(reader);
        else
            new LetGoOfReader().Invoke(reader);
    }
    /// <inheritdoc/>
    public IEnumerable<T> Query(DbCommand command, ICache cache, bool disposeCommand = false) {
        var cnn = command.Connection ?? throw new RinkuNoConnectionException();
        var wasClosed = cnn.State != ConnectionState.Open;
        try {
            var behavior = Behavior;
            if (wasClosed) {
                cnn.Open();
                behavior |= CommandBehavior.CloseConnection;
            }
            using var reader = command.ExecuteReader(behavior);
            wasClosed = false;
            cache.UpdateCache(command);
            if (reader.Read()) {
                bool canContinue;
                do {
                    (canContinue, var item) = ParseOne(reader);
                    yield return item;
                } while (canContinue);
            }
        }
        finally {
            if (wasClosed)
                cnn.Close();
            if (disposeCommand) {
                command.Parameters.Clear();
                command.Dispose();
            }
        }
    }
    /// <inheritdoc/>
    public IEnumerable<T> Query(IDbCommand command, ICache cache, bool disposeCommand = false) {
        var cnn = command.Connection ?? throw new RinkuNoConnectionException();
        var wasClosed = cnn.State != ConnectionState.Open;
        try {
            var behavior = Behavior;
            if (wasClosed) {
                cnn.Open();
                behavior |= CommandBehavior.CloseConnection;
            }
            var r = command.ExecuteReader(behavior);
            using var reader = WrappedBasicReader.Wrap(r);
            wasClosed = false;
            cache.UpdateCache(command);
            if (reader.Read()) {
                bool canContinue;
                do {
                    (canContinue, var item) = ParseOne(reader);
                    yield return item;
                } while (canContinue);
            }
        }
        finally {
            if (disposeCommand) {
                command.Parameters.Clear();
                command.Dispose();
            }
            if (wasClosed)
                cnn.Close();
        }
    }
    /// <inheritdoc cref="QueryAsync(DbCommand, bool, CancellationToken)"/>
    public async Task<IEnumerable<T>> QueryAsync(DbCommand command, ICache cache, bool disposeCommand = false, CancellationToken ct = default) {
        var cnn = command.Connection ?? throw new RinkuNoConnectionException();
        var wasClosed = cnn.State != ConnectionState.Open;
        DbDataReader? reader = null;
        try {
            var behavior = Behavior;
            if (wasClosed) {
                await cnn.OpenAsync(ct).ConfigureAwait(false);
                behavior |= CommandBehavior.CloseConnection;
            }
            reader = await command.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
            wasClosed = false;
            await cache.UpdateCacheAsync(command, ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                return Default();
            var open = reader;
            reader = null;
            return disposeCommand
                ? ParseThen(open, new LetGoOfReaderAndCommand(command))
                : ParseThen(open, new LetGoOfReader());
        }
        finally {
            if (reader is not null) {
                LetGo(reader, command, disposeCommand);
                if (wasClosed && cnn.State != ConnectionState.Closed)
                    cnn.Close();
            }
        }
    }
    /// <inheritdoc cref="QueryAsync(IDbCommand, bool, CancellationToken)"/>
    public Task<IEnumerable<T>> QueryAsync(IDbCommand command, ICache cache, bool disposeCommand = false, CancellationToken ct = default) {
        if (command is DbCommand c)
            return QueryAsync(c, cache, disposeCommand, ct);
        return Task.FromResult(Query(command, cache, disposeCommand));
    }
}
/// <summary>
/// The parser behind <see cref="IEnumerable{T}"/>, streaming each row through an element parser as you enumerate.
/// </summary>
public sealed class EnumerableTypeParser<T>(ITypeParser<T> elementParser) : BaseEnumerableTypeParser<T> {
    private readonly ITypeParser<T> ElementParser = elementParser;
    /// <inheritdoc/>
    public override CommandBehavior Behavior => ElementParser.Behavior & ~CommandBehavior.SingleRow;
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override (bool CanContinue, T Result) ParseOne(DbDataReader reader) => ElementParser.Parse(reader);
}
/// <summary>The <see cref="EnumerableTypeParser{T}"/> fast path, for elements read by a plain row delegate.</summary>
public sealed class FastEnumerableTypeParser<T>(CommandBehavior behavior, Func<DbDataReader, T> parser) : BaseEnumerableTypeParser<T> {
    private readonly Func<DbDataReader, T> Parser = parser;
    /// <inheritdoc/>
    public override CommandBehavior Behavior { get; } = behavior & ~CommandBehavior.SingleRow;
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override (bool CanContinue, T Result) ParseOne(DbDataReader reader) {
        var res = Parser(reader);
        return (reader.Read(), res);
    }
}
