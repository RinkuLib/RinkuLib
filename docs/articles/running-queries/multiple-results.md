# Multiple result sets

```csharp
public record Artist(int Id, string Name);
public record Album(int Id, string Title);

static readonly QueryCommand GetDashboard = new("SELECT ArtistId AS Id, Name FROM artists WHERE ArtistId = @artistId; SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");
```

```csharp
using MultiReader results = GetDashboard.ExecuteMultiReader(cnn, new { artistId = 7 });

Artist artist = results.Query<Artist>();
List<Album> albums = results.Query<List<Album>>();
```

Each `Query<T>()` reads the current readable result set and advances to the next one.

## Skip non returning results

```csharp
static readonly QueryCommand UpdateAndRead = new("UPDATE albums SET LastViewed = CURRENT_TIMESTAMP WHERE ArtistId = @artistId; SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

using MultiReader results = UpdateAndRead.ExecuteMultiReader(cnn, new { artistId = 7 });
List<Album> albums = results.Query<List<Album>>();
// The UPDATE result is skipped before the readable result set.
```

## Inspect the current set parser

```csharp
using MultiReader results = GetDashboard.ExecuteMultiReader(cnn, new { artistId = 7 });
ITypeParser<Artist> parser = results.GetCurrentSetParser<Artist>();
```

`GetCurrentSetParser<T>()` does not advance to the next result set.

## Read rows with the parser

`Get<T>()` starts at the current row and returns whether another row remains in the current set.

```csharp
using MultiReader results = GetDashboard.ExecuteMultiReader(cnn, new { artistId = 7 });

bool reading = results.Read();
while (reading)
{
    (reading, var artist) = results.Get<Artist>();
    Console.WriteLine(artist.Name);
}
```

## Use the underlying reader

`MultiReader` is also a `DbDataReader`, so direct ADO.NET access remains available.

```csharp
static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId");

using MultiReader results = GetAlbums.ExecuteMultiReader(cnn);

while (results.Read())
{
    int id = results.GetInt32(0);
    string title = results.GetString(1);
}
```

## Stream one set

```csharp
using MultiReader results = GetDashboard.ExecuteMultiReader(cnn, new { artistId = 7 });

foreach (Artist artist in results.Query<IEnumerable<Artist>>())
    Console.WriteLine(artist.Name);

List<Album> albums = results.Query<List<Album>>();
```

The next result set is available after the stream finishes.

## Async

```csharp
await using MultiReader results = await GetDashboard.ExecuteMultiReaderAsync(cnn, new { artistId = 7 }, ct: cancellationToken);

Artist artist = await results.QueryAsync<Artist>(ct: cancellationToken);

await foreach (Album album in results.StreamQueryAsync<Album>(ct: cancellationToken))
    Console.WriteLine(album.Title);
```
