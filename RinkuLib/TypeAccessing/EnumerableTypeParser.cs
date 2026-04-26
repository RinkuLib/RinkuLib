using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace RinkuLib.TypeAccessing;

/// <summary>
/// Parses an IEnumerable of <typeparamref name="T"/> lazily using yield return.
/// </summary>
public sealed class EnumerableTypeParser<T>(ITypeParser<T> elementParser) : ITypeParser<IEnumerable<T>>, IHasBehavior, ILazyTypeParser<IEnumerable<T>> {
    private readonly ITypeParser<T> _elementParser = elementParser;


    /// <inheritdoc/>
    public IEnumerable<T> ParseAndOwn(DbDataReader reader, IDbCommand command, bool wasClosed, bool disposeCommand) {
        try {
            do {
                yield return _elementParser.Parse(reader);
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
    public CommandBehavior Behavior { get; } = (elementParser as IHasBehavior)?.Behavior ?? CommandBehavior.Default;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerable<T>? Default() => [];

    /// <inheritdoc/>
    public IEnumerable<T> Parse(DbDataReader reader) {
        do {
            yield return _elementParser.Parse(reader);
        } while (reader.Read());
    }
    /// <inheritdoc/>
    public Task<IEnumerable<T>> ParseAsync(DbDataReader reader, CancellationToken ct = default) 
        => Task.FromResult(Parse(reader));

    /// <inheritdoc/>
    public IEnumerable<T>? Parse(DbCommand command, bool disposeCommand = false) {
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
                yield return _elementParser.Parse(reader);
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
    public IEnumerable<T>? Parse(IDbCommand command, bool disposeCommand = false) {
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
                yield return _elementParser.Parse(reader);
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
    public Task<IEnumerable<T>?> ParseAsync(DbCommand command, bool disposeCommand = false, CancellationToken ct = default)
        => Task.FromResult(Parse(command, disposeCommand));
    /// <inheritdoc/>
    public Task<IEnumerable<T>?> ParseAsync(IDbCommand command, bool disposeCommand = false, CancellationToken ct = default) {
        if (command is DbCommand c)
            return ParseAsync(c, disposeCommand, ct);
        return Task.FromResult(Parse(command, disposeCommand));
    }


    /// <inheritdoc/>
    public IEnumerable<T>? Parse(DbCommand command, ICache cache, bool disposeCommand = false) {
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
                yield return _elementParser.Parse(reader);
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
    public IEnumerable<T>? Parse(IDbCommand command, ICache cache, bool disposeCommand = false) {
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
                yield return _elementParser.Parse(reader);
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
    public Task<IEnumerable<T>?> ParseAsync(DbCommand command, ICache cache, bool disposeCommand = false, CancellationToken ct = default)
        => Task.FromResult(Parse(command, cache, disposeCommand));
    /// <inheritdoc/>
    public Task<IEnumerable<T>?> ParseAsync(IDbCommand command, ICache cache, bool disposeCommand = false, CancellationToken ct = default) {
        if (command is DbCommand c)
            return ParseAsync(c, cache, disposeCommand, ct);
        return Task.FromResult(Parse(command, cache, disposeCommand));
    }
}