# The SQL string

Skip declaring a `QueryCommand` and hand the SQL to the connection. The command is built once and cached by the string, so repeating the exact string reuses it.

```csharp
List<Track> tracks = cnn.Query<List<Track>>(
    "SELECT TrackId AS Id, Name FROM tracks WHERE AlbumId = @albumId",
    new { albumId = 1 });
```

Every method has the string form, `Execute`, `ExecuteScalar<T>`, `QueryAsync`, `StreamQueryAsync`, `ExecuteReader`, `ExecuteMultiReader`.

```csharp
int total = cnn.ExecuteScalar<int>("SELECT COUNT(*) FROM tracks");

await foreach (Track t in cnn.StreamQueryAsync<Track>(
    "SELECT TrackId AS Id, Name FROM tracks WHERE AlbumId = @albumId", new { albumId = 1 }, ct: token))
    Process(t);
```

## Own the cached command

The cached command is yours to reach. `GetOrCreateCommand` hands back the same `QueryCommand` the string calls reuse, so you can hold and configure it, its [parameter metadata](parameter-metadata.md) and the rest, exactly as a declared one.

```csharp
QueryCommand ByAlbum = ConnectionQueryExtensions.GetOrCreateCommand("SELECT TrackId AS Id, Name FROM tracks WHERE AlbumId = @albumId");

List<Track> tracks = ByAlbum.Query<List<Track>>(cnn, new { albumId = 1 });
```

Declaring a `QueryCommand` up front stays the primary form and skips the by-string lookup. The string form skips the declaration and pays that lookup.

The string cache retains one command for every distinct key until it is removed. Use this form for a bounded set of repeated strings, not for SQL text assembled with unbounded values or combinations. Declare and retain a `QueryCommand` when the set is not naturally bounded; that makes its lifetime explicit and avoids growing the global cache.

`CommandCache` is the actual `ConcurrentDictionary<string, QueryCommand>`. Use the dictionary directly to inspect, replace, or remove entries:

```csharp
ConnectionQueryExtensions.CommandCache.TryRemove(sql, out QueryCommand? removed);

foreach (var entry in ConnectionQueryExtensions.CommandCache)
    if (entry.Key.StartsWith("tenant:", StringComparison.Ordinal))
        ConnectionQueryExtensions.CommandCache.TryRemove(entry.Key, out _);
```

Removal does not dispose or mutate the command because a caller may already hold the instance returned by `GetOrCreateCommand`. A later string call creates a new command for that SQL.

The key need not equal the command's SQL. This lets an application bind a stable logical key to a preconfigured command:

```csharp
ConnectionQueryExtensions.CommandCache["tracks:active"] = new QueryCommand("SELECT TrackId AS Id, Name FROM tracks WHERE Active = 1");
```

Calling a string extension with `"tracks:active"` now runs that command's SQL. The caches inside each `QueryCommand` remain independent; the dictionary only makes commands currently stored in it reachable, and an independently owned command is unaffected.
