# RinkuLib

[![NuGet](https://img.shields.io/nuget/v/Rinku)](https://www.nuget.org/packages/Rinku/) [![NuGet downloads](https://img.shields.io/nuget/dt/Rinku)](https://www.nuget.org/packages/Rinku/) [![Documentation](https://img.shields.io/badge/docs-documentation-blue)](https://rinkulib.github.io/RinkuLib/)

Rinku is a micro ORM for .NET built directly on ADO.NET. SQL stays explicit instead of being generated from an object model. Rinku adapts between database-facing and .NET-facing shapes so each side can keep the form that fits it best.

## Install

```bash
dotnet add package Rinku
```

## Run a query

```csharp
using Rinku;

public record Album(int Id, string Title) : IDbReadable;

static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
```

A `QueryCommand` is reusable. Values are supplied for each call.

See [running queries](https://rinkulib.github.io/RinkuLib/articles/running-queries/execution.html).

## Adapt either side

If both sides already match, nothing else is needed.

```csharp
public record Album(int AlbumId, string Title);

static readonly QueryCommand GetAlbums = new("SELECT AlbumId, Title FROM albums");

List<Album> albums = GetAlbums.Query<List<Album>>(cnn);
```

If SQL is the cleanest side to change, alias it.

```sql
SELECT customer_id AS Id, display_name AS Name FROM customers
```

If the returned names should stay unchanged, adapt the .NET side.

```csharp
public record Customer([Alt("customer_id")] int Id, [Alt("display_name")] string Name);
```

The rule can also live outside both SQL and the model.

See [adapt names](https://rinkulib.github.io/RinkuLib/articles/mapping/names.html).

## Choose the result shape

```csharp
Album first = GetAlbum.Query<Album>(cnn, new { albumId = 12 });
Optional<Album> maybe = cnn.Query<Optional<Album>>("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
Single<Album> one = GetAlbum.Query<Single<Album>>(cnn, new { albumId = 12 });
List<Album> all = GetAlbums.Query<List<Album>>(cnn);
IEnumerable<Album> streamed = GetAlbums.Query<IEnumerable<Album>>(cnn);
```

The same result parsers are available from the SQL-string shortcuts.

See [result shapes](https://rinkulib.github.io/RinkuLib/articles/running-queries/result-shapes.html).

## Map nested results

```csharp
public record Artist(int Id, string Name) : IDbReadable;
public record AlbumWithArtist(int Id, string Title, Artist Artist);

static readonly QueryCommand GetAlbumsWithArtist = new("SELECT al.AlbumId AS Id, al.Title, ar.ArtistId AS ArtistId, ar.Name AS ArtistName FROM albums al JOIN artists ar ON ar.ArtistId = al.ArtistId");

List<AlbumWithArtist> albums = GetAlbumsWithArtist.Query<List<AlbumWithArtist>>(cnn);
// ArtistId and ArtistName fill AlbumWithArtist.Artist
```

See [nested objects](https://rinkulib.github.io/RinkuLib/articles/mapping/nesting.html).

Repeated join rows can also fill a nested collection.

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record ArtistWithAlbums(int Id, string Name, List<Album> Albums);

static readonly QueryCommand GetArtists = new("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumsId, al.Title AS AlbumsTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId");

List<ArtistWithAlbums> artists = GetArtists.Query<List<ArtistWithAlbums>>(cnn);
// Consecutive rows with the same artist group into one ArtistWithAlbums.
// AlbumsId and AlbumsTitle fill its Albums collection.
```

See [collections](https://rinkulib.github.io/RinkuLib/articles/mapping/collections.html) and [grouping](https://rinkulib.github.io/RinkuLib/articles/mapping/grouping.html).

## Make SQL conditional

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@artistId AND Title LIKE ?@title");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
// title is absent:
// SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

See [conditional variables](https://rinkulib.github.io/RinkuLib/articles/conditional-sql/variables.html).

## Run asynchronously

```csharp
List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(cnn, ct: cancellationToken);

await foreach (Album album in GetAlbums.StreamQueryAsync<Album>(cnn, ct: cancellationToken))
    Console.WriteLine(album.Title);
```

See [async execution](https://rinkulib.github.io/RinkuLib/articles/running-queries/async.html) and [streaming](https://rinkulib.github.io/RinkuLib/articles/running-queries/streaming.html).

## Use an existing DbCommand

A cached parser can read a `DbCommand` regardless of where the command was created.

```csharp
public record Album(int Id, string Title);

static readonly CachedTypeParser<Album> GetAlbumParser = new();

using DbCommand command = cnn.CreateCommand();
command.CommandText = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = 7";

Album album = GetAlbumParser.Query(command);
```

See [existing DbCommand](https://rinkulib.github.io/RinkuLib/articles/running-queries/dbcommand.html) and [stored procedures](https://rinkulib.github.io/RinkuLib/articles/running-queries/stored-procedures.html).

## Generate database commands

Rinku Power Tools can inspect configured SQL, SQL files, and stored procedures and generate typed `DbCommand` methods and result records.

```csharp
static readonly CachedTypeParser<List<GetAlbumsByArtistResult>> Parser = new();

List<GetAlbumsByArtistResult> albums = Parser.Query(cnn.GetAlbumsByArtist(artistId: 7));
```

See [code generation](https://rinkulib.github.io/RinkuLib/articles/codegen/index.html).

## Analyzers and code fixes

The `Rinku` package includes analyzers and code fixes.

```csharp
/// <Schema LastUpdated="2026-08-21T14:00Z" />
public record AlbumSchema(int Id, string Title);

/// <BasedOn cref="AlbumSchema" LastUpdated="2026-08-21T14:00Z" />
public record AlbumDto(int Id, string Title);
```

See [analyzers and code fixes](https://rinkulib.github.io/RinkuLib/articles/codegen/analyzers.html).

## Track edits

```csharp
using Rinku.Tracking.Runtime;

Album original = new(12, "Blue");
IRuntimeTrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(original);

edit.Set(nameof(Album.Title), "Kind of Blue");

foreach (TrackingChange change in edit.GetChanges())
{
    // Generated over the configured editable members.
    // Current edit state is compared with the accepted value when enumerated.
    // No separate per-member mutation history is stored.
    Console.WriteLine($"{change.Name} {change.OriginalValue} -> {change.Value}");
}
```

See [tracking](https://rinkulib.github.io/RinkuLib/articles/tracking/index.html), [editable items](https://rinkulib.github.io/RinkuLib/articles/tracking/items.html), and [persistence](https://rinkulib.github.io/RinkuLib/articles/tracking/persistence.html).

## Documentation

Start with the [documentation index](https://rinkulib.github.io/RinkuLib/articles/index.html) or the [overview](https://rinkulib.github.io/RinkuLib/articles/overview.html).

The main guides cover [running queries](https://rinkulib.github.io/RinkuLib/articles/running-queries/execution.html), [mapping](https://rinkulib.github.io/RinkuLib/articles/mapping/objects.html), [conditional SQL](https://rinkulib.github.io/RinkuLib/articles/conditional-sql/variables.html), [advanced customization](https://rinkulib.github.io/RinkuLib/articles/customization/index.html), [code generation](https://rinkulib.github.io/RinkuLib/articles/codegen/index.html), [analyzers](https://rinkulib.github.io/RinkuLib/articles/codegen/analyzers.html), [tracking](https://rinkulib.github.io/RinkuLib/articles/tracking/index.html), and [coming from Dapper](https://rinkulib.github.io/RinkuLib/articles/reference/dapper.html).
