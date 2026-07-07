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
QueryCommand ByAlbum = ConnectionQueryExtensions.GetOrCreateCommand(
    "SELECT TrackId AS Id, Name FROM tracks WHERE AlbumId = @albumId");

List<Track> tracks = ByAlbum.Query<List<Track>>(cnn, new { albumId = 1 });
```

Declaring a `QueryCommand` up front stays the primary form and skips the by-string lookup; the string form is the lighter road.
