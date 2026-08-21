# SQL-string shortcuts

Connection extension methods accept SQL text without a separately declared command.

```csharp
List<Album> albums = cnn.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId", new { artistId = 7 });
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

The first call creates a `QueryCommand`. Later calls using the exact same string reuse it.

```csharp
const string sql = "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId";

List<Album> first = cnn.Query<List<Album>>(sql, new { artistId = 7 });
List<Album> second = cnn.Query<List<Album>>(sql, new { artistId = 12 });
// Both calls use the same cached QueryCommand.
```

## One global cache

SQL-string calls share one global cache.

```csharp
const string sql = "SELECT AlbumId AS Id, Title FROM albums";

using DbConnection firstConnection = new SqlConnection(connectionString);
using DbConnection secondConnection = new SqlConnection(connectionString);

List<Album> first = firstConnection.Query<List<Album>>(sql);
List<Album> second = secondConnection.Query<List<Album>>(sql);
// Both calls use the same cached QueryCommand.
```

The cached command also retains its [learned parameter metadata](parameter-metadata.md).

## The exact string is the key

Whitespace and casing make different normal cache entries.

```csharp
QueryCommand first = ConnectionQueryExtensions.GetOrCreateCommand("SELECT AlbumId AS Id FROM albums");

QueryCommand same = ConnectionQueryExtensions.GetOrCreateCommand("SELECT AlbumId AS Id FROM albums");

QueryCommand differentCase = ConnectionQueryExtensions.GetOrCreateCommand("select AlbumId as Id from albums");

QueryCommand differentSpacing = ConnectionQueryExtensions.GetOrCreateCommand("SELECT  AlbumId AS Id FROM albums");

// first and same are the same command.
// differentCase and differentSpacing are separate commands.
```

`GetOrCreateCommand` exposes the command used by the string extensions.

```csharp
static readonly QueryCommand GetAlbums =
    ConnectionQueryExtensions.GetOrCreateCommand("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
```

## Remove an entry

Entries remain until the application removes them.

```csharp
const string sql = "SELECT AlbumId AS Id, Title FROM albums";

List<Album> albums = cnn.Query<List<Album>>(sql);

bool removed = ConnectionQueryExtensions.CommandCache.TryRemove(sql, out QueryCommand? cached);

// Removing the entry does not dispose cached.
```

The next string call creates another command.

```csharp
List<Album> albums = cnn.Query<List<Album>>(sql);
```

## Use an application key

Direct dictionary entries may use a key that is not SQL.

```csharp
ConnectionQueryExtensions.CommandCache["albums.for-artist"] =
    new QueryCommand("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

List<Album> albums = cnn.Query<List<Album>>("albums.for-artist", new { artistId = 7 });
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

The dictionary key selects the manually stored command. Its SQL remains the text passed to the `QueryCommand` constructor.

## Other execution methods

The shortcuts use the same execution and result-shape rules as a declared `QueryCommand`.

```csharp
int affected = cnn.Execute("UPDATE albums SET Title = @title WHERE AlbumId = @albumId", new { albumId = 12, title = "Kind of Blue" });

int count = cnn.Query<int>("SELECT COUNT(*) FROM albums");

List<Album> albums = await cnn.QueryAsync<List<Album>>("SELECT AlbumId AS Id, Title FROM albums", ct: cancellationToken);
```

```sql
UPDATE albums SET Title = @title WHERE AlbumId = @albumId
```

```sql
SELECT COUNT(*) FROM albums
```

```sql
SELECT AlbumId AS Id, Title FROM albums
```

`ExecuteScalar<T>`, `StreamQueryAsync<T>`, `ExecuteReader`, and `ExecuteMultiReader` have matching string forms.

## Avoid unbounded cache keys

Every distinct normal string becomes another cache entry.

```csharp
string sql = $"SELECT AlbumId AS Id, Title FROM albums ORDER BY {userSelectedColumn}";
List<Album> albums = cnn.Query<List<Album>>(sql);
// Every distinct sql value becomes a distinct global cache key.
```

When the set of possible strings is not bounded, validate the dynamic SQL and manage its commands explicitly instead of retaining every variation in the global cache.
