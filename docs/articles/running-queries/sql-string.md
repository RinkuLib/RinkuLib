# SQL string access

```csharp
const string sql = "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId";

List<Album> albums = cnn.Query<List<Album>>(sql, new { artistId = 7 });
```

The first access for an exact SQL string creates its `QueryCommand`. Later accesses with that exact string reuse the cached command.

## Access the command

```csharp
QueryCommand command = ConnectionQueryExtensions.GetOrCreateCommand(sql);
List<Album> albums = command.Query<List<Album>>(cnn, new { artistId = 7 });
```

The string and direct command forms now access the same cached command instance.

## Exact cache key

```csharp
QueryCommand first = ConnectionQueryExtensions.GetOrCreateCommand("SELECT AlbumId FROM albums");
QueryCommand second = ConnectionQueryExtensions.GetOrCreateCommand("select AlbumId FROM albums");

Console.WriteLine(ReferenceEquals(first, second));
// False. The SQL text is not the same exact string value.
```

Whitespace differences also produce different keys.

## Remove one cached command

```csharp
const string sql = "SELECT AlbumId AS Id, Title FROM albums";

ConnectionQueryExtensions.CommandCache.TryRemove(sql, out QueryCommand? cached);
```

Removing the cache entry does not dispose the removed `QueryCommand`.

[Command parser caches](../customization/caches.md)
