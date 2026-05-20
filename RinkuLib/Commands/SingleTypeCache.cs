using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using RinkuLib.DbParsing;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.Commands; 

/// <summary>
/// A high-performance, lock-free cache for a single type parser.
/// </summary>
public sealed class SingleTypeCache<T> : ICacheUsingParser<T> {
    private ITypeParser<T>? _parser;
    private sealed class CacheBridge(ICache userCache, ICacheUsingParser<T> parserCache) : ICacheUsingParser<T> {
        private readonly ICache _userCache = userCache;
        private readonly ICacheUsingParser<T> _parserCache = parserCache;
        public void UpdateCache(IDbCommand cmd, DbDataReader reader, [NotNull] ref ITypeParser<T>? parser) {
            _parserCache.UpdateCache(cmd, reader, ref parser);
            _userCache.UpdateCache(cmd);
        }
    }

    /// <summary>Updates the internal parser cache on the first execution.</summary>
    public void UpdateCache(IDbCommand cmd, DbDataReader reader, [NotNull] ref ITypeParser<T>? parser) {
        if (_parser is null) {
            var schema = reader.GetColumnsFast();
            _parser = TypeParser<T>.GetTypeParser(ref schema);
        }
        parser = _parser;
    }

    /// <summary>Executes the command and parses the result.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Query(DbCommand cmd, bool disposeCommand = false) {
        var p = _parser;
        return p is not null ? p.Query(cmd, disposeCommand) : cmd.Query(disposeCommand, null, this);
    }

    /// <summary>Asynchronously executes the command and parses the result.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<T> QueryAsync(DbCommand cmd, bool disposeCommand = false, CancellationToken ct = default) {
        var p = _parser;
        return p is not null ? p.QueryAsync(cmd, disposeCommand, ct) : cmd.QueryAsync(disposeCommand, null, this, ct);
    }

    /// <summary>Executes the command using a custom cache and parses the result.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Query(DbCommand cmd, ICache cache, bool disposeCommand = false) {
        var p = _parser;
        return p is not null ? p.Query(cmd, cache, disposeCommand) : cmd.Query(disposeCommand, null, new CacheBridge(cache, this));
    }

    /// <summary>Asynchronously executes the command using a custom cache and parses the result.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<T> QueryAsync(DbCommand cmd, ICache cache, bool disposeCommand = false, CancellationToken ct = default) {
        var p = _parser;
        return p is not null ? p.QueryAsync(cmd, cache, disposeCommand, ct) : cmd.QueryAsync(disposeCommand, null, new CacheBridge(cache, this), ct);
    }

    /// <summary>Executes the IDbCommand and parses the result.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Query(IDbCommand cmd, bool disposeCommand = false) {
        var p = _parser;
        return p is not null ? p.Query(cmd, disposeCommand) : cmd.Query(disposeCommand, null, this);
    }

    /// <summary>Asynchronously executes the IDbCommand and parses the result.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<T> QueryAsync(IDbCommand cmd, bool disposeCommand = false, CancellationToken ct = default) {
        var p = _parser;
        return p is not null ? p.QueryAsync(cmd, disposeCommand, ct) : cmd.QueryAsync(disposeCommand, null, this, ct);
    }

    /// <summary>Executes the IDbCommand using a custom cache and parses the result.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Query(IDbCommand cmd, ICache cache, bool disposeCommand = false) {
        var p = _parser;
        return p is not null ? p.Query(cmd, cache, disposeCommand) : cmd.Query(disposeCommand, null, new CacheBridge(cache, this));
    }

    /// <summary>Asynchronously executes the IDbCommand using a custom cache and parses the result.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<T> QueryAsync(IDbCommand cmd, ICache cache, bool disposeCommand = false, CancellationToken ct = default) {
        var p = _parser;
        return p is not null ? p.QueryAsync(cmd, cache, disposeCommand, ct) : cmd.QueryAsync(disposeCommand, null, new CacheBridge(cache, this), ct);
    }
}