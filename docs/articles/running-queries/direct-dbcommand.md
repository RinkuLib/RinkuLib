# Any DbCommand

Mapping runs on a command you built. Hold a `CachedTypeParser<T>`, the parser cache, and call `Query` on it with the command.

```csharp
static readonly CachedTypeParser<Track> Tracks = new();

using var cmd = cnn.CreateCommand();
cmd.CommandText = "SELECT TrackId AS Id, Name FROM tracks WHERE TrackId = @id";
cmd.Parameters.Add(new SqlParameter("@id", 10));

Track track = Tracks.Query(cmd);
```

Writes and scalars sit on the command:

```csharp
int affected = updateCmd.Execute(disposeCommand: true);
int total    = countCmd.ExecuteScalar<int>(disposeCommand: true);
```

Async and streaming:

```csharp
Track track = await Tracks.QueryAsync(cmd, ct: token);

await foreach (Track t in Tracks.StreamQueryAsync(cmd, ct: token))
    Process(t);
```

## Your own cache

`cmd.Query` takes an `ICacheGivingParser<T>`, so a class of your own can be the cache and hold the parser itself. The first call goes through `cmd.Query(this)`, which fills the parser in `UpdateCache`. Later calls use it directly.

```csharp
public sealed class TrackRepo : ICacheGivingParser<List<Track>> {
    private ITypeParser<List<Track>>? parser;

    public List<Track> All(DbCommand cmd)
        => parser is not null ? parser.Query(cmd) : cmd.Query(this);   // derives once, then reuses

    public CommandBehavior Behavior => parser?.Behavior ?? CommandBehavior.SingleResult;
    public ITypeParser<List<Track>> UpdateCache(IDbCommand cmd, DbDataReader reader) {
        var cols = reader.GetColumns();
        return parser ??= TypeParser.GetTypeParser<List<Track>>(ref cols);
    }
    public ValueTask<ITypeParser<List<Track>>> UpdateCacheAsync(IDbCommand cmd, DbDataReader reader, CancellationToken ct = default)
        => new(UpdateCache(cmd, reader));
}
```

One class can hold several shapes this way, a field for `Track`, another for `List<Track>`, implementing `ICacheGivingParser<T>` for each.

## `IDbCommand` support

Mirrored for `IDbCommand`. When it is really a `DbCommand`, async forwards to the real async implementation.
