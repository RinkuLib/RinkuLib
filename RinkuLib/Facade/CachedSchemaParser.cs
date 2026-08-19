using System.Data;
using System.Data.Common;
using System.Reflection;
using Rinku.Internal;
using Rinku.Mapping;
using Rinku.Mapping.Parsers;

namespace Rinku;

/// <summary>
/// Holds one result schema and caches a parser for every result type requested over it.
/// A parameterless instance learns the schema from its first query.
/// The instance can be shared across threads.
/// </summary>
public sealed class CachedTypeParser : IDisposable {
    private readonly record struct ParserKey(Type Type, INullColHandler NullColHandler);

    private sealed class CacheBridge<T>(CachedTypeParser owner, ICache? userCache = null) : ICacheGivingParser<T> {
        private readonly CachedTypeParser _owner = owner;
        private readonly ICache? _userCache = userCache;

        public CommandBehavior Behavior => _owner.GetBehavior<T>();

        public ITypeParser<T> UpdateCache(IDbCommand cmd, DbDataReader reader) {
            var parser = _owner.Learn<T>(reader);
            _userCache?.UpdateCache(cmd);
            return parser;
        }

        public async ValueTask<ITypeParser<T>> UpdateCacheAsync(IDbCommand cmd, DbDataReader reader, CancellationToken ct = default) {
            var parser = _owner.Learn<T>(reader);
            if (_userCache is not null)
                await _userCache.UpdateCacheAsync(cmd, ct).ConfigureAwait(false);
            return parser;
        }
    }
    private sealed class RuntimeCacheBridge(CachedTypeParser owner, Type type, ICache? userCache = null) : ICacheGivingParser {
        public CommandBehavior Behavior => owner.HasSchema ? owner.Get(type).Behavior : CommandBehavior.SingleResult;
        public ITypeParser UpdateCache(IDbCommand cmd, DbDataReader reader) {
            var parser = owner.Learn(type, reader);
            userCache?.UpdateCache(cmd);
            return parser;
        }
        public async ValueTask<ITypeParser> UpdateCacheAsync(IDbCommand cmd, DbDataReader reader, CancellationToken ct = default) {
            var parser = owner.Learn(type, reader);
            if (userCache is not null) await userCache.UpdateCacheAsync(cmd, ct).ConfigureAwait(false);
            return parser;
        }
    }

    private ColumnInfo[]? _schema;
    private readonly Dictionary<ParserKey, ITypeParser> _parsers = [];
    private bool _subscribedToParserDisposing;
    private int _disposed;

    /// <summary>Creates a cache that learns its result columns from the first query.</summary>
    public CachedTypeParser() { }
    /// <summary>Creates a cache for the supplied result columns.</summary>
    public CachedTypeParser(ColumnInfo[] schema) => _schema = [.. (schema ?? throw new ArgumentNullException(nameof(schema)))];
    /// <summary>Creates a cache for columns derived from <paramref name="schemaType"/>.</summary>
    public CachedTypeParser(Type schemaType) : this(SchemaExtractor.FromType(schemaType)) { }
    /// <summary>Creates a cache for columns derived from a method's parameters.</summary>
    public CachedTypeParser(MethodBase schemaMethod) : this(SchemaExtractor.FromMethod(schemaMethod)) { }
    /// <summary>Creates a cache for columns derived from a constructor's parameters.</summary>
    public CachedTypeParser(ConstructorInfo schemaConstructor) : this(SchemaExtractor.FromConstructor(schemaConstructor)) { }
    /// <summary>Creates a cache for columns derived from a delegate's parameters.</summary>
    public CachedTypeParser(Delegate schemaFactory) : this(SchemaExtractor.FromMethod(schemaFactory.Method)) { }

    /// <summary>Whether this instance already knows its fixed result columns.</summary>
    public bool HasSchema => Volatile.Read(ref _schema) is not null;
    /// <summary>The fixed columns every parser from this cache accepts.</summary>
    /// <exception cref="InvalidOperationException">The parameterless instance has not run a query yet.</exception>
    public ColumnInfo[] Schema => Volatile.Read(ref _schema) is { } schema
        ? [.. schema]
        : throw new InvalidOperationException("The schema is not known until this CachedTypeParser runs its first query.");

    /// <summary>Creates a cache for the columns derived from <typeparamref name="TSchema"/>.</summary>
    public static CachedTypeParser From<TSchema>() => new(TypeSchema<TSchema>.Schema);

    /// <summary>Gets the parser for <typeparamref name="T"/> over this cache's fixed schema.</summary>
    public ITypeParser<T> Get<T>(INullColHandler? nullColHandler = null) {
        var schema = Volatile.Read(ref _schema)
            ?? throw new InvalidOperationException("The schema is not known until this CachedTypeParser runs its first query.");
        nullColHandler ??= TypeParser.GetDefaultNullColHandler<T>();
        var key = new ParserKey(typeof(T), nullColHandler);
        lock (TypeParser.TypeParserMakers) {
            lock (this) {
                if (Volatile.Read(ref _disposed) != 0)
                    throw new ObjectDisposedException(nameof(CachedTypeParser));
                if (_parsers.TryGetValue(key, out var existing))
                    return (ITypeParser<T>)existing;

                var parser = TypeParser.GetTypeParser<T>(schema, nullColHandler);
                if (!_subscribedToParserDisposing) {
                    TypeParser.ParserDisposing += OnParserDisposing;
                    _subscribedToParserDisposing = true;
                }
                _parsers.Add(key, parser);
                return parser;
            }
        }
    }

    /// <summary>Gets the parser for a runtime result type over this cache's fixed schema.</summary>
    public ITypeParser Get(Type type, INullColHandler? nullColHandler = null) {
        ArgumentNullException.ThrowIfNull(type);
        var schema = Volatile.Read(ref _schema)
            ?? throw new InvalidOperationException("The schema is not known until this CachedTypeParser runs its first query.");
        nullColHandler ??= TypeParser.GetDefaultNullColHandler(type);
        var key = new ParserKey(type, nullColHandler);
        lock (TypeParser.TypeParserMakers) {
            lock (this) {
                if (Volatile.Read(ref _disposed) != 0)
                    throw new ObjectDisposedException(nameof(CachedTypeParser));
                if (_parsers.TryGetValue(key, out var existing))
                    return existing;

                var parser = TypeParser.GetTypeParser(type, schema, nullColHandler);
                if (!_subscribedToParserDisposing) {
                    TypeParser.ParserDisposing += OnParserDisposing;
                    _subscribedToParserDisposing = true;
                }
                _parsers.Add(key, parser);
                return parser;
            }
        }
    }

    /// <summary>Queries <paramref name="cmd"/> as <typeparamref name="T"/> and learns the fixed schema when needed.</summary>
    public T Query<T>(DbCommand cmd, bool disposeCommand = false)
        => HasSchema ? Get<T>().Query(cmd, disposeCommand) : cmd.Query(new CacheBridge<T>(this), disposeCommand);
    /// <summary>Asynchronously queries <paramref name="cmd"/> as <typeparamref name="T"/> and learns the fixed schema when needed.</summary>
    public Task<T> QueryAsync<T>(DbCommand cmd, bool disposeCommand = false, CancellationToken ct = default)
        => HasSchema ? Get<T>().QueryAsync(cmd, disposeCommand, ct) : cmd.QueryAsync(new CacheBridge<T>(this), disposeCommand, ct);
    /// <summary>Queries <paramref name="cmd"/> while updating <paramref name="cache"/>.</summary>
    public T Query<T>(DbCommand cmd, ICache cache, bool disposeCommand = false)
        => HasSchema ? Get<T>().Query(cmd, cache, disposeCommand) : cmd.Query(new CacheBridge<T>(this, cache), disposeCommand);
    /// <summary>Asynchronously queries <paramref name="cmd"/> while updating <paramref name="cache"/>.</summary>
    public Task<T> QueryAsync<T>(DbCommand cmd, ICache cache, bool disposeCommand = false, CancellationToken ct = default)
        => HasSchema ? Get<T>().QueryAsync(cmd, cache, disposeCommand, ct) : cmd.QueryAsync(new CacheBridge<T>(this, cache), disposeCommand, ct);
    /// <summary>Queries <paramref name="cmd"/> as <typeparamref name="T"/> and learns the fixed schema when needed.</summary>
    public T Query<T>(IDbCommand cmd, bool disposeCommand = false)
        => HasSchema ? Get<T>().Query(cmd, disposeCommand) : cmd.Query(new CacheBridge<T>(this), disposeCommand);
    /// <summary>Asynchronously queries <paramref name="cmd"/> as <typeparamref name="T"/> and learns the fixed schema when needed.</summary>
    public Task<T> QueryAsync<T>(IDbCommand cmd, bool disposeCommand = false, CancellationToken ct = default)
        => HasSchema ? Get<T>().QueryAsync(cmd, disposeCommand, ct) : cmd.QueryAsync(new CacheBridge<T>(this), disposeCommand, ct);
    /// <summary>Queries <paramref name="cmd"/> while updating <paramref name="cache"/>.</summary>
    public T Query<T>(IDbCommand cmd, ICache cache, bool disposeCommand = false)
        => HasSchema ? Get<T>().Query(cmd, cache, disposeCommand) : cmd.Query(new CacheBridge<T>(this, cache), disposeCommand);
    /// <summary>Asynchronously queries <paramref name="cmd"/> while updating <paramref name="cache"/>.</summary>
    public Task<T> QueryAsync<T>(IDbCommand cmd, ICache cache, bool disposeCommand = false, CancellationToken ct = default)
        => HasSchema ? Get<T>().QueryAsync(cmd, cache, disposeCommand, ct) : cmd.QueryAsync(new CacheBridge<T>(this, cache), disposeCommand, ct);
    /// <summary>Queries <paramref name="cmd"/> while mapping the result to a runtime type.</summary>
    public object? Query(Type type, DbCommand cmd, bool disposeCommand = false)
        => HasSchema ? Get(type).QueryObject(cmd, null, disposeCommand) : cmd.Query(type, new RuntimeCacheBridge(this, type), disposeCommand);
    /// <summary>Asynchronously queries <paramref name="cmd"/> while mapping the result to a runtime type.</summary>
    public Task<object?> QueryAsync(Type type, DbCommand cmd, bool disposeCommand = false, CancellationToken ct = default)
        => HasSchema ? Get(type).QueryObjectAsync(cmd, null, disposeCommand, ct) : cmd.QueryAsync(type, new RuntimeCacheBridge(this, type), disposeCommand, ct);
    /// <summary>Queries <paramref name="cmd"/> while mapping the result to a runtime type.</summary>
    public object? Query(Type type, IDbCommand cmd, bool disposeCommand = false)
        => HasSchema ? Get(type).QueryObject(cmd, null, disposeCommand) : cmd.Query(type, new RuntimeCacheBridge(this, type), disposeCommand);
    /// <summary>Asynchronously queries <paramref name="cmd"/> while mapping the result to a runtime type.</summary>
    public Task<object?> QueryAsync(Type type, IDbCommand cmd, bool disposeCommand = false, CancellationToken ct = default)
        => HasSchema ? Get(type).QueryObjectAsync(cmd, null, disposeCommand, ct) : cmd.QueryAsync(type, new RuntimeCacheBridge(this, type), disposeCommand, ct);
    /// <summary>Streams rows from <paramref name="cmd"/> as <typeparamref name="T"/> over this cache's fixed schema.</summary>
    public IAsyncEnumerable<T> StreamQueryAsync<T>(DbCommand cmd, bool disposeCommand = false, CancellationToken ct = default)
        => HasSchema
            ? cmd.StreamQueryAsync(Get<T>(), disposeCommand: disposeCommand, ct: ct)
            : cmd.StreamQueryAsync(new CacheBridge<T>(this), disposeCommand, ct);

    private CommandBehavior GetBehavior<T>() => HasSchema ? Get<T>().Behavior : CommandBehavior.SingleResult;

    private ITypeParser<T> Learn<T>(DbDataReader reader) {
        if (!HasSchema) {
            var discoveredSchema = reader.GetColumnsFast();
            lock (this) {
                if (Volatile.Read(ref _disposed) != 0)
                    throw new ObjectDisposedException(nameof(CachedTypeParser));
                _schema ??= [.. discoveredSchema];
            }
        }
        return Get<T>();
    }
    private ITypeParser Learn(Type type, DbDataReader reader) {
        if (!HasSchema) {
            var discoveredSchema = reader.GetColumnsFast();
            lock (this) {
                if (Volatile.Read(ref _disposed) != 0)
                    throw new ObjectDisposedException(nameof(CachedTypeParser));
                _schema ??= [.. discoveredSchema];
            }
        }
        return Get(type);
    }

    /// <summary>Forgets the parser for <typeparamref name="T"/> held by this cache.</summary>
    /// <returns><see langword="true"/> when a parser was removed.</returns>
    public bool Invalidate<T>(INullColHandler? nullColHandler = null) {
        nullColHandler ??= TypeParser.GetDefaultNullColHandler<T>();
        ITypeParser? parser;
        lock (this) {
            if (!_parsers.Remove(new(typeof(T), nullColHandler), out parser))
                return false;
            UnsubscribeWhenEmpty();
        }
        TypeParser.TryDisposeParser(parser, ParserInvalidationMode.CheckUsage);
        return true;
    }

    /// <summary>Forgets every parser held by this cache.</summary>
    /// <returns>The number of distinct parsers removed.</returns>
    public int Invalidate() {
        List<ITypeParser> parsers;
        lock (this) {
            if (_parsers.Count == 0)
                return 0;
            parsers = [];
            foreach (var parser in _parsers.Values)
                if (!parsers.Any(held => ReferenceEquals(held, parser)))
                    parsers.Add(parser);
            _parsers.Clear();
            UnsubscribeWhenEmpty();
        }
        for (int i = 0; i < parsers.Count; i++)
            TypeParser.TryDisposeParser(parsers[i], ParserInvalidationMode.CheckUsage);
        return parsers.Count;
    }

    private void OnParserDisposing(object? sender, ParserDisposingEventArgs args) {
        lock (this) {
            bool contains = _parsers.Values.Any(parser => ReferenceEquals(parser, args.Parser));
            if (!contains)
                return;
            if (args.Mode == ParserInvalidationMode.CheckUsage) {
                args.Cancel = true;
                return;
            }
            foreach (var key in _parsers.Where(pair => ReferenceEquals(pair.Value, args.Parser)).Select(pair => pair.Key).ToArray())
                _parsers.Remove(key);
            UnsubscribeWhenEmpty();
        }
    }

    private void UnsubscribeWhenEmpty() {
        if (_parsers.Count != 0 || !_subscribedToParserDisposing)
            return;
        TypeParser.ParserDisposing -= OnParserDisposing;
        _subscribedToParserDisposing = false;
    }

    /// <summary>Releases every parser held by this instance and stops it from receiving invalidation notices.</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Invalidate();
        GC.SuppressFinalize(this);
    }
}
