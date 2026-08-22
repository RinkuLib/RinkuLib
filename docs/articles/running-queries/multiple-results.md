# Multiple result sets

Read each result set with its own result shape.

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name) : IDbReadable;

static readonly QueryCommand GetDashboard = new("SELECT ArtistId AS Id, Name FROM artists WHERE ArtistId = @artistId; SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

using MultiReader results = GetDashboard.ExecuteMultiReader(cnn, new { artistId = 7 });

Artist artist = results.Query<Artist>();
List<Album> albums = results.Query<List<Album>>();
```

Each `Query<T>()` reads the current readable result set and advances to the next one.

```csharp
using MultiReader results = GetDashboard.ExecuteMultiReader(cnn, new { artistId = 7 });

Optional<Artist> artist = results.Query<Optional<Artist>>();
Album[] albums = results.Query<Album[]>();
```

## Control parser advancement

```csharp
using MultiReader results = GetDashboard.ExecuteMultiReader(cnn, new { artistId = 7 });

var parser = results.GetCurrentSetParser<Artist>();
```

Use the current set parser directly when normal `Query<T>()` advancement is not wanted.

## Skip non returning results

```csharp
static readonly QueryCommand UpdateAndRead = new("UPDATE artists SET LastViewed = @now WHERE ArtistId = @artistId; SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

using MultiReader results = UpdateAndRead.ExecuteMultiReader(cnn, new { artistId = 7, now = DateTime.UtcNow });

List<Album> albums = results.Query<List<Album>>();
```

`MultiReader` moves past non returning results to the next readable set.

## Stream one result set

```csharp
using MultiReader results = GetDashboard.ExecuteMultiReader(cnn, new { artistId = 7 });

IEnumerable<Artist> artists = results.Query<IEnumerable<Artist>>();

using (IEnumerator<Artist> iterator = artists.GetEnumerator())
{
    if (iterator.MoveNext())
        Console.WriteLine(iterator.Current.Name);
}

List<Album> albums = results.Query<List<Album>>();
```

Disposing the stream advances the multi reader.

## Async result sets

```csharp
using MultiReader results = await GetDashboard.ExecuteMultiReaderAsync(cnn, new { artistId = 7 }, ct: cancellationToken);

Artist artist = await results.QueryAsync<Artist>(ct: cancellationToken);
List<Album> albums = await results.QueryAsync<List<Album>>(ct: cancellationToken);
```

Async streaming is also available on `MultiReader`.

```csharp
await foreach (Artist artist in results.StreamQueryAsync<Artist>(ct: cancellationToken))
    Console.WriteLine(artist.Name);
```
