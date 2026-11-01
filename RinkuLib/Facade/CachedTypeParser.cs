using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Rinku.Mapping;
using Rinku.Internal;
using Rinku.Mapping.Parsers;

namespace Rinku; 

/// <summary>
/// Reads <typeparamref name="T"/> from database commands created by the caller.
/// Keep one instance and reuse it when those commands return compatible columns.
/// The instance can be shared across threads.
/// </summary>
public sealed class CachedTypeParser<T> : ICacheGivingParser<T>, IDisposable {
    /// <summary>Shared parser for callers whose commands return a compatible shape for this result type. Use a separate instance for another shape.</summary>
    public static CachedTypeParser<T> Default { get; } = new();
    private ITypeParser<T>? _parser;
    private bool _subscribedToParserDisposing;
    private int _disposed;
    private sealed class CacheBridge(ICache userCache, ICacheGivingParser<T> parserCache) : ICacheGivingParser<T> {
        public CommandBehavior Behavior => CommandBehavior.SingleResult;
        public ITypeParser<T> UpdateCache(IDbCommand cmd, DbDataReader reader) {
            var parser = parserCache.UpdateCache(cmd, reader);
            userCache.UpdateCache(cmd);
            return parser;
        }

        public async ValueTask<ITypeParser<T>> UpdateCacheAsync(IDbCommand cmd, DbDataReader reader, CancellationToken ct = default) {
            var parser = await parserCache.UpdateCacheAsync(cmd, reader, ct).ConfigureAwait(false);
            await userCache.UpdateCacheAsync(cmd, ct).ConfigureAwait(false);
            return parser;
        }
    }
    /// <inheritdoc cref="ITypeParser{T}"/>
    public CommandBehavior Behavior => _parser is null ? CommandBehavior.SingleResult : _parser.Behavior;
    /// <inheritdoc cref="ITypeParser{T}"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Query(DbCommand cmd, bool disposeCommand = false)
        => _parser is not null ? _parser.Query(cmd, disposeCommand) : cmd.Query(this, disposeCommand);
    /// <inheritdoc cref="ITypeParser{T}"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<T> QueryAsync(DbCommand cmd, bool disposeCommand = false, CancellationToken ct = default)
        => _parser is not null ? _parser.QueryAsync(cmd, disposeCommand, ct) : cmd.QueryAsync(this, disposeCommand, ct);
    /// <inheritdoc cref="ITypeParser{T}"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Query(DbCommand cmd, ICache cache, bool disposeCommand = false)
        => _parser is not null ? _parser.Query(cmd, cache, disposeCommand) : cmd.Query(new CacheBridge(cache, this), disposeCommand);
    /// <inheritdoc cref="ITypeParser{T}.QueryAsync(DbCommand, ICache, bool, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<T> QueryAsync(DbCommand cmd, ICache cache, bool disposeCommand = false, CancellationToken ct = default)
        => _parser is not null ? _parser.QueryAsync(cmd, cache, disposeCommand, ct) : cmd.QueryAsync(new CacheBridge(cache, this), disposeCommand, ct);
    /// <inheritdoc cref="ITypeParser{T}"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Query(IDbCommand cmd, bool disposeCommand = false)
        => _parser is not null ? _parser.Query(cmd, disposeCommand) : cmd.Query(this, disposeCommand);
    /// <inheritdoc cref="ITypeParser{T}"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<T> QueryAsync(IDbCommand cmd, bool disposeCommand = false, CancellationToken ct = default)
        => _parser is not null ? _parser.QueryAsync(cmd, disposeCommand, ct) : cmd.QueryAsync(this, disposeCommand, ct);
    /// <inheritdoc cref="ITypeParser{T}"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Query(IDbCommand cmd, ICache cache, bool disposeCommand = false)
        => _parser is not null ? _parser.Query(cmd, cache, disposeCommand) : cmd.Query(new CacheBridge(cache, this), disposeCommand);
    /// <inheritdoc cref="ITypeParser{T}"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<T> QueryAsync(IDbCommand cmd, ICache cache, bool disposeCommand = false, CancellationToken ct = default)
        => _parser is not null ? _parser.QueryAsync(cmd, cache, disposeCommand, ct) : cmd.QueryAsync(new CacheBridge(cache, this), disposeCommand, ct);
    /// <summary>
    /// Streams rows from <paramref name="cmd"/> as <typeparamref name="T"/>.
    /// Use the row type for <typeparamref name="T"/> rather than a collection type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IAsyncEnumerable<T> StreamQueryAsync(DbCommand cmd, bool disposeCommand = false, CancellationToken ct = default)
        => _parser is not null
            ? cmd.StreamQueryAsync(_parser, disposeCommand: disposeCommand, ct: ct)
            : cmd.StreamQueryAsync(this, disposeCommand, ct);
    /// <inheritdoc/>
    public ITypeParser<T> UpdateCache(IDbCommand cmd, DbDataReader reader) {
        var parser = _parser;
        if (parser is not null)
            return parser;
        var schema = reader.GetColumnsFast();
        lock (TypeParser.TypeParserMakers) {
            lock (this) {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, nameof(CachedTypeParser<>));
                parser = _parser;
                if (parser is null) {
                    parser = TypeParser.GetTypeParser<T>(schema);
                    if (!_subscribedToParserDisposing) {
                        TypeParser.ParserDisposing += OnParserDisposing;
                        _subscribedToParserDisposing = true;
                    }
                    _parser = parser;
                }
            }
        }
        return parser;
    }
    /// <inheritdoc/>
    public ValueTask<ITypeParser<T>> UpdateCacheAsync(IDbCommand cmd, DbDataReader reader, CancellationToken ct = default)
        => new(UpdateCache(cmd, reader));
    /// <summary>Forgets the parser held by this instance.</summary>
    /// <returns><see langword="true"/> when a parser was removed.</returns>
    public bool Invalidate() {
        ITypeParser<T>? parser;
        lock (this) {
            parser = Interlocked.Exchange(ref _parser, null);
            if (parser is null)
                return false;
            UnsubscribeFromParserDisposing();
        }
        TypeParser.TryDisposeParser(parser, ParserInvalidationMode.CheckUsage);
        return true;
    }
    private void OnParserDisposing(object? sender, ParserDisposingEventArgs args) {
        lock (this) {
            if (!ReferenceEquals(_parser, args.Parser))
                return;
            if (args.Mode == ParserInvalidationMode.CheckUsage) {
                args.Cancel = true;
                return;
            }
            _parser = null;
            UnsubscribeFromParserDisposing();
        }
    }
    private void UnsubscribeFromParserDisposing() {
        if (!_subscribedToParserDisposing)
            return;
        TypeParser.ParserDisposing -= OnParserDisposing;
        _subscribedToParserDisposing = false;
    }
    /// <summary>Stops this instance from receiving parser invalidation notices and releases its parser.</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Invalidate();
        GC.SuppressFinalize(this);
    }
}
