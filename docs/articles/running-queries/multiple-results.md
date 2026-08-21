# Multiple result sets

A command can return several result sets, each with its own result shape.

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record class ArtistWithAlbums(int Id, string Name) {
    public List<Album> Albums { get; set; } = [];
}

static readonly QueryCommand GetDashboard = new(
    "SELECT ArtistId AS Id, Name FROM artists WHERE ArtistId = @artistId; SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

using MultiReader results = GetDashboard.ExecuteMultiReader(cnn, new { artistId = 7 });

ArtistWithAlbums artist = results.Query<ArtistWithAlbums>();
artist.Albums = results.Query<List<Album>>();
```

Each `Query<T>()` reads the current result set and advances to the next one.

```csharp
using MultiReader results = GetDashboard.ExecuteMultiReader(cnn, new { artistId = 7 });

Optional<Artist> artist = results.Query<Optional<Artist>>();
// results now points to the albums result set.

Album[] albums = results.Query<Album[]>();
```

This also advances when the selected shape only needs the first complete result. Get the current parser when application code needs control over advancement.

```csharp
using MultiReader results = GetDashboard.ExecuteMultiReader(cnn, new { artistId = 7 });

var parser = results.GetCurrentSetParser<Artist>();
// Run the parser directly when Query<T>() advancing is not wanted.
```

`MultiReader` skips non-returning results while moving to the next readable set.

```csharp
static readonly QueryCommand UpdateAndRead = new(
    "UPDATE artists SET LastViewed = @now WHERE ArtistId = @artistId; SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

using MultiReader results = UpdateAndRead.ExecuteMultiReader(cnn, new { artistId = 7, now = DateTime.UtcNow });

List<Album> albums = results.Query<List<Album>>();
```

## Stream one result set

A synchronous stream advances when its enumerator is disposed, including when enumeration stops early.

```csharp
using MultiReader results = GetDashboard.ExecuteMultiReader(cnn, new { artistId = 7 });

IEnumerable<Artist> artists = results.Query<IEnumerable<Artist>>();

using (IEnumerator<Artist> iterator = artists.GetEnumerator()) {
    if (iterator.MoveNext())
        Console.WriteLine(iterator.Current.Name);
}

// The artists result set was stopped early and results advanced.
List<Album> albums = results.Query<List<Album>>();
```

`StreamQueryAsync<T>` follows the same rule.

```csharp
using MultiReader results = await GetDashboard.ExecuteMultiReaderAsync(cnn, new { artistId = 7 }, ct: cancellationToken);

await foreach (Artist artist in results.StreamQueryAsync<Artist>(ct: cancellationToken)) {
    Console.WriteLine(artist.Name);
    break;
}

// Disposing the async enumerator advanced to the albums result set.
List<Album> albums = await results.QueryAsync<List<Album>>(ct: cancellationToken);
```

Set `goToNextResultSet` to `false` when application code advances the reader itself.

```csharp
using MultiReader results = await GetDashboard.ExecuteMultiReaderAsync(cnn, new { artistId = 7 }, ct: cancellationToken);

await foreach (Artist artist in results.StreamQueryAsync<Artist>(goToNextResultSet: false, ct: cancellationToken)) {
    Console.WriteLine(artist.Name);
}

await results.NextResultAsync(cancellationToken);

List<Album> albums = await results.QueryAsync<List<Album>>(ct: cancellationToken);
```

## Command ownership

Without the `out` overload, disposing `MultiReader` disposes its generated command.

```csharp
using (MultiReader results = GetDashboard.ExecuteMultiReader(cnn, new { artistId = 7 })) {
    Artist artist = results.Query<Artist>();
}
// The reader and generated command are disposed.
```

Use `out DbCommand` when output parameters or the command itself are needed afterward.

```csharp
MultiReader results = GetDashboard.ExecuteMultiReader(cnn, out DbCommand command, new { artistId = 7 });

using (command) {
    using (results) {
        Artist artist = results.Query<Artist>();
        List<Album> albums = results.Query<List<Album>>();
    }
}
```

Disposal closes a connection opened by Rinku. An initially open connection remains open.

## Async

```csharp
using MultiReader results = await GetDashboard.ExecuteMultiReaderAsync(cnn, new { artistId = 7 }, ct: cancellationToken);

Artist artist = await results.QueryAsync<Artist>(ct: cancellationToken);
List<Album> albums = await results.QueryAsync<List<Album>>(ct: cancellationToken);
```
