# SQL string shortcuts

A connection can execute SQL text directly.

```csharp
List<Album> albums = cnn.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId", new { artistId = 7 });
```

The first call creates a `QueryCommand`. Later calls with the exact same SQL string reuse it from the global command cache.

```csharp
const string sql = "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId";

List<Album> first = cnn.Query<List<Album>>(sql, new { artistId = 7 });
List<Album> second = cnn.Query<List<Album>>(sql, new { artistId = 12 });
```

## Get the cached command

```csharp
QueryCommand command = ConnectionQueryExtensions.GetOrCreateCommand("SELECT AlbumId AS Id, Title FROM albums");
```

The returned `QueryCommand` can use the normal command APIs.

## Cache key

The exact string is the normal cache key.

```csharp
QueryCommand first = ConnectionQueryExtensions.GetOrCreateCommand("SELECT AlbumId AS Id FROM albums");
QueryCommand same = ConnectionQueryExtensions.GetOrCreateCommand("SELECT AlbumId AS Id FROM albums");
QueryCommand differentCase = ConnectionQueryExtensions.GetOrCreateCommand("select AlbumId as Id from albums");
```

Whitespace and casing can therefore create different cache entries.

## Remove a cached command

```csharp
const string sql = "SELECT AlbumId AS Id, Title FROM albums";

bool removed = ConnectionQueryExtensions.CommandCache.TryRemove(sql, out QueryCommand? cached);
```

Removing the entry does not dispose the removed `QueryCommand`.

Use an explicitly declared `QueryCommand` when command lifetime or configuration should be directly owned by application code.

See [parameter metadata](parameter-metadata.md) because cached SQL string commands also retain learned parameter metadata.
