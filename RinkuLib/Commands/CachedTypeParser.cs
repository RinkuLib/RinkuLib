using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using RinkuLib.DbParsing;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.Commands; 

/// <summary>
/// A high-performance, lock-free cache for a single type parser.
/// </summary>
public sealed class CachedTypeParser<T> : ICacheGivingParser<T> {
    private ITypeParser<T>? _parser;
    private sealed class CacheBridge(ICache userCache, ICacheGivingParser<T> parserCache) : ICacheGivingParser<T> {
        private readonly ICache _userCache = userCache;
        private readonly ICacheGivingParser<T> _parserCache = parserCache;
        public CommandBehavior Behavior => CommandBehavior.SingleResult;
        public ITypeParser<T> UpdateCache(IDbCommand cmd, DbDataReader reader) {
            var parser = _parserCache.UpdateCache(cmd, reader);
            _userCache.UpdateCache(cmd);
            return parser;
        }

        public ValueTask<ITypeParser<T>> UpdateCacheAsync(IDbCommand cmd, DbDataReader reader, CancellationToken ct = default)
            => new(UpdateCache(cmd, reader));
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
    /// Streams the rows of <paramref name="cmd"/> as <typeparamref name="T"/>. Because the cache is keyed on
    /// <typeparamref name="T"/>, a <c>CachedTypeParser&lt;List&lt;X&gt;&gt;</c> yields whole lists; use a
    /// <c>CachedTypeParser&lt;X&gt;</c> to stream elements. Reuses the cached parser once learned.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IAsyncEnumerable<T> StreamQueryAsync(DbCommand cmd, bool disposeCommand = true, CancellationToken ct = default)
        => _parser is not null
            ? cmd.StreamQueryAsync(_parser, disposeCommand: disposeCommand, ct: ct)
            : cmd.StreamQueryAsync(this, disposeCommand, ct);
    /// <inheritdoc/>
    public ITypeParser<T> UpdateCache(IDbCommand cmd, DbDataReader reader) {
        if (_parser is null) {
            var schema = reader.GetColumnsFast();
            _parser = TypeParser.GetTypeParser<T>(ref schema);
        }
        return _parser;
    }
    /// <inheritdoc/>
    public ValueTask<ITypeParser<T>> UpdateCacheAsync(IDbCommand cmd, DbDataReader reader, CancellationToken ct = default)
        => new(UpdateCache(cmd, reader));
}