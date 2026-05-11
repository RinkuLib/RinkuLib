using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace RinkuLib.TypeAccessing;

/// <summary>
/// Parses an IEnumerable of <typeparamref name="T"/> lazily using yield return.
/// </summary>
public abstract class BaseEnumerableTypeParser<T> : ITypeParser<IEnumerable<T>>, ILazyTypeParser<IEnumerable<T>> {
    bool ITypeParser<IEnumerable<T>>.InternalProtect => true;
    /// <summary>
    /// Used to parse a single item
    /// </summary>
    protected abstract T ParseOne(DbDataReader reader);

    /// <inheritdoc/>
    public IEnumerable<T> ParseAndOwn(DbDataReader reader, IDbCommand command, bool wasClosed, bool disposeCommand) {
        try {
            do {
                yield return ParseOne(reader);
            } while (reader.Read());
        }
        finally {
            reader.Dispose();
            if (wasClosed)
                command.Connection?.Close();
            if (disposeCommand) {
                command.Parameters.Clear();
                command.Dispose();
            }
        }
    }
    /// <inheritdoc/>
    public abstract CommandBehavior Behavior { get; }
    /// <inheritdoc/>
    public virtual bool SupportsParsingAsync => false;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerable<T> Default() => [];

    /// <inheritdoc/>
    public IEnumerable<T> Parse(DbDataReader reader) {
        do {
            yield return ParseOne(reader);
        } while (reader.Read());
    }
    /// <inheritdoc/>
    public Task<IEnumerable<T>> ParseAsync(DbDataReader reader, CancellationToken ct = default) 
        => Task.FromResult(Parse(reader));

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
            while (reader.Read())
                yield return ParseOne(reader);
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
            while (reader.Read())
                yield return ParseOne(reader);
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
            while (reader.Read())
                yield return ParseOne(reader);
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
            while (reader.Read())
                yield return ParseOne(reader);
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
/// Parses an IEnumerable of <typeparamref name="T"/> lazily using yield return.
/// </summary>
public sealed class EnumerableTypeParser<T>(ITypeParser<T> elementParser) : BaseEnumerableTypeParser<T> {
    private readonly ITypeParser<T> ElementParser = elementParser;
    /// <inheritdoc/>
    public override CommandBehavior Behavior => ElementParser.Behavior & ~CommandBehavior.SingleRow;
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override T ParseOne(DbDataReader reader) => ElementParser.Parse(reader);
}

/// <summary>Optimized Enumerable parser that uses a direct delegate.</summary>
public sealed class FastEnumerableTypeParser<T>(CommandBehavior behavior, Func<DbDataReader, T> parser) : BaseEnumerableTypeParser<T> {
    private readonly Func<DbDataReader, T> Parser = parser;
    /// <inheritdoc/>
    public override CommandBehavior Behavior { get; } = behavior & ~CommandBehavior.SingleRow;
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override T ParseOne(DbDataReader reader) => Parser(reader);
}