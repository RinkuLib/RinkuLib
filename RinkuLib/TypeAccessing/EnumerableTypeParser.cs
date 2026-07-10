using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
namespace RinkuLib.TypeAccessing;
/// <summary>
/// The base for the streamed shape, <see cref="IEnumerable{T}"/>, it yields rows as you enumerate rather than
/// buffering them, keeping memory flat on large results. It holds the reader open while you iterate and
/// disposes it when enumeration ends.
/// </summary>
public abstract class BaseEnumerableTypeParser<T> : ITypeParser<IEnumerable<T>>, ILazyTypeParser<IEnumerable<T>> {
    bool ITypeParser<IEnumerable<T>>.InternalProtect => true;
    /// <summary>
    /// Used to parse a single item. Reports whether the reader is left on an untreated row
    /// </summary>
    protected abstract (bool CanContinue, T Result) ParseOne(DbDataReader reader);
    /// <inheritdoc/>
    public IEnumerable<T> ParseAndOwn<TCallback>(DbDataReader reader, TCallback callback) where TCallback : ILazyTypeParserCallback {
        try {
            bool canContinue;
            do {
                (canContinue, var item) = ParseOne(reader);
                yield return item;
            } while (canContinue);
        }
        finally {
            callback.Invoke(reader);
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
        var cnn = command.Connection ?? throw new Exception("no connections was set with the command");
        var wasClosed = cnn.State != ConnectionState.Open;
        try {
            var behavior = Behavior;
            if (wasClosed) {
                cnn.Open();
                behavior |= CommandBehavior.CloseConnection;
                wasClosed = false;
            }
            using var reader = command.ExecuteReader(behavior);
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
        var cnn = command.Connection ?? throw new Exception("no connections was set with the command");
        var wasClosed = cnn.State != ConnectionState.Open;
        try {
            var behavior = Behavior;
            if (wasClosed) {
                cnn.Open();
                behavior |= CommandBehavior.CloseConnection;
                wasClosed = false;
            }
            var r = command.ExecuteReader(behavior);
            using var reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
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
    public Task<IEnumerable<T>> QueryAsync(DbCommand command, bool disposeCommand = false, CancellationToken ct = default)
        => Task.FromResult(Query(command, disposeCommand));
    /// <inheritdoc/>
    public Task<IEnumerable<T>> QueryAsync(IDbCommand command, bool disposeCommand = false, CancellationToken ct = default) {
        if (command is DbCommand c)
            return QueryAsync(c, disposeCommand, ct);
        return Task.FromResult(Query(command, disposeCommand));
    }
    /// <inheritdoc/>
    public IEnumerable<T> Query(DbCommand command, ICache cache, bool disposeCommand = false) {
        var cnn = command.Connection ?? throw new Exception("no connections was set with the command");
        var wasClosed = cnn.State != ConnectionState.Open;
        try {
            var behavior = Behavior;
            if (wasClosed) {
                cnn.Open();
                behavior |= CommandBehavior.CloseConnection;
                wasClosed = false;
            }
            using var reader = command.ExecuteReader(behavior);
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
        var cnn = command.Connection ?? throw new Exception("no connections was set with the command");
        var wasClosed = cnn.State != ConnectionState.Open;
        try {
            var behavior = Behavior;
            if (wasClosed) {
                cnn.Open();
                behavior |= CommandBehavior.CloseConnection;
                wasClosed = false;
            }
            var r = command.ExecuteReader(behavior);
            using var reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
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
    /// <inheritdoc/>
    public Task<IEnumerable<T>> QueryAsync(DbCommand command, ICache cache, bool disposeCommand = false, CancellationToken ct = default)
        => Task.FromResult(Query(command, cache, disposeCommand));
    /// <inheritdoc/>
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
