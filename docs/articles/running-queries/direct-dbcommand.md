# Any DbCommand

Mapping runs on a command you built. Hold a `CachedTypeParser<T>`, the parser cache, and call `Query` on it with the command.

```csharp
static readonly CachedTypeParser<Track> Tracks = new();

using var cmd = cnn.CreateCommand();
cmd.CommandText = "SELECT TrackId AS Id, Name FROM tracks WHERE TrackId = @id";
cmd.Parameters.Add(new SqlParameter("@id", 10));

Track track = Tracks.Query(cmd);
```

Writes and returned values sit on the command. For a `SELECT` that reads one
scalar, use the query parser instead:

```csharp
int affected = updateCmd.Execute(disposeCommand: true);
int total    = countCmd.Query<int>(disposeCommand: true);
```

Async and streaming:

```csharp
Track track = await Tracks.QueryAsync(cmd, ct: token);

await foreach (Track t in Tracks.StreamQueryAsync(cmd, ct: token))
    Process(t);
```

`Tracks.Invalidate()` drops the parser held by that one cache and asks `TypeParser.Release` to dispose it when no other cache retains it. `CachedTypeParser<T>` also follows global parser invalidation after learning its first parser. Dispose a cache whose lifetime ends; that releases its parser and removes its ordinary instance-method event subscription.

## Your own cache

`cmd.Query` takes an `ICacheGivingParser<T>`, so a class of your own can be the cache and hold the parser itself. The first call goes through `cmd.Query(this)`, which fills the parser in `UpdateCache`. Later calls use it directly.

```csharp
public sealed class TrackRepo : ICacheGivingParser<List<Track>>, IDisposable {
    private ITypeParser<List<Track>>? parser;
    private bool subscribed;

    public List<Track> All(DbCommand cmd)
        => parser is not null ? parser.Query(cmd) : cmd.Query(this);   // derives once, then reuses

    public CommandBehavior Behavior => parser?.Behavior ?? CommandBehavior.SingleResult;
    public ITypeParser<List<Track>> UpdateCache(IDbCommand cmd, DbDataReader reader) {
        var cols = reader.GetColumns();
        var result = parser ??= TypeParser.GetTypeParser<List<Track>>(cols);
        if (!subscribed) {
            TypeParser.ParserDisposing += OnParserDisposing;
            subscribed = true;
        }
        return result;
    }
    public ValueTask<ITypeParser<List<Track>>> UpdateCacheAsync(IDbCommand cmd, DbDataReader reader, CancellationToken ct = default)
        => new(UpdateCache(cmd, reader));

    private void OnParserDisposing(object? sender, ParserDisposingEventArgs args) {
        if (!ReferenceEquals(parser, args.Parser))
            return;
        if (args.Mode == ParserInvalidationMode.CheckUsage) {
            args.Cancel = true;
            return;
        }
        parser = null;
        TypeParser.ParserDisposing -= OnParserDisposing;
        subscribed = false;
    }

    public void Dispose() {
        var released = parser;
        parser = null;
        if (subscribed)
            TypeParser.ParserDisposing -= OnParserDisposing;
        subscribed = false;
        if (released is not null)
            TypeParser.Release(released);
    }
}
```

One class can hold several shapes this way, a field for `Track`, another for `List<Track>`, implementing `ICacheGivingParser<T>` for each.

The example shows the invalidation protocol explicitly. A shared cache must also synchronize its first update, invalidation handler, and disposal around its fields. The built-in `CachedTypeParser<T>` already provides that thread-safe implementation when one cached shape is enough.

## `IDbCommand` support

Mirrored for `IDbCommand`. When it is really a `DbCommand`, async forwards to the real async implementation.
