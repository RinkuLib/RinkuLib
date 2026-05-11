using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace RinkuLib.TypeAccessing; 

/// <summary>
/// Class that parse a <typeparamref name="T"/> object from the db
/// </summary>
public abstract class BaseTypeParser<T> : ITypeParser<T> {
    bool ITypeParser<T>.InternalProtect => true;
    /// <inheritdoc/>
    public abstract CommandBehavior Behavior { get; }
    /// <inheritdoc/>
    public abstract bool SupportsParsingAsync { get; }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract T Default();
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract T Parse(DbDataReader reader);
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract Task<T> ParseAsync(DbDataReader reader, CancellationToken ct = default);

    /// <inheritdoc/>
    public T Query(DbCommand command, bool disposeCommand = false) {
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
            if (!reader.Read())
                return Default();
            return Parse(reader);
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
    public T Query(IDbCommand command, bool disposeCommand = false) {
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
            if (!reader.Read())
                return Default();
            return Parse(reader);
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
    public async Task<T> QueryAsync(DbCommand command, bool disposeCommand = false, CancellationToken ct = default) {
        var cnn = command.Connection ?? throw new Exception("no connections was set with the command");
        var wasClosed = cnn.State != ConnectionState.Open;
        try {
            var behavior = Behavior;
            if (wasClosed) {
                await cnn.OpenAsync(ct).ConfigureAwait(false);
                behavior |= CommandBehavior.CloseConnection;
                wasClosed = false;
            }
            using var reader = await command.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                return Default();
            return await ParseAsync(reader, ct).ConfigureAwait(false);
        }
        finally {
            if (disposeCommand) {
                command.Parameters.Clear();
                command.Dispose();
            }
            if (wasClosed)
                await cnn.CloseAsync().ConfigureAwait(false);
        }
    }
    /// <inheritdoc/>
    public Task<T> QueryAsync(IDbCommand command, bool disposeCommand = false, CancellationToken ct = default) {
        if (command is DbCommand c)
            return QueryAsync(c, disposeCommand, ct);
        return Task.FromResult(Query(command, disposeCommand));
    }


    /// <inheritdoc/>
    public T Query(DbCommand command, ICache cache, bool disposeCommand = false) {
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
            if (!reader.Read())
                return Default();
            return Parse(reader);
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
    public T Query(IDbCommand command, ICache cache, bool disposeCommand = false) {
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
            if (!reader.Read())
                return Default();
            return Parse(reader);
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
    public async Task<T> QueryAsync(DbCommand command, ICache cache, bool disposeCommand = false, CancellationToken ct = default) {
        var cnn = command.Connection ?? throw new Exception("no connections was set with the command");
        var wasClosed = cnn.State != ConnectionState.Open;
        try {
            var behavior = Behavior;
            if (wasClosed) {
                await cnn.OpenAsync(ct).ConfigureAwait(false);
                behavior |= CommandBehavior.CloseConnection;
                wasClosed = false;
            }
            using var reader = await command.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
            cache.UpdateCache(command);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                return Default();
            return await ParseAsync(reader, ct).ConfigureAwait(false);
        }
        finally {
            if (disposeCommand) {
                command.Parameters.Clear();
                command.Dispose();
            }
            if (wasClosed)
                await cnn.CloseAsync().ConfigureAwait(false);
        }
    }
    /// <inheritdoc/>
    public Task<T> QueryAsync(IDbCommand command, ICache cache, bool disposeCommand = false, CancellationToken ct = default) {
        if (command is DbCommand c)
            return QueryAsync(c, cache, disposeCommand, ct);
        return Task.FromResult(Query(command, cache, disposeCommand));
    }
}