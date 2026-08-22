# RinkuLib

Rinku is a database mapping library for .NET. SQL stays in application code and the requested result type controls how returned rows are read.

## Install

```bash
dotnet add package Rinku
```

## Run a query

```csharp
using Rinku;

public record Album(int Id, string Title) : IDbReadable;

static readonly QueryCommand GetAlbums = new(
    "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

List<Album> albums = GetAlbums.Query<List<Album>>(
    cnn,
    new { artistId = 7 });
```

A `QueryCommand` is reusable. Values are supplied for each call.

## Choose the result shape

```csharp
Album first = GetAlbum.Query<Album>(cnn, new { albumId = 12 });
Optional<Album> maybe = GetAlbum.Query<Optional<Album>>(cnn, new { albumId = 12 });
Single<Album> one = GetAlbum.Query<Single<Album>>(cnn, new { albumId = 12 });
List<Album> all = GetAlbums.Query<List<Album>>(cnn);
IEnumerable<Album> streamed = GetAlbums.Query<IEnumerable<Album>>(cnn);
```

The requested type selects count behavior and buffering.

## Map nested results

```csharp
public record Artist(int Id, string Name) : IDbReadable;
public record AlbumWithArtist(int Id, string Title, Artist Artist);

static readonly QueryCommand GetAlbums = new(
    "SELECT al.AlbumId AS Id, al.Title, ar.ArtistId AS ArtistId, ar.Name AS ArtistName FROM albums al JOIN artists ar ON ar.ArtistId = al.ArtistId");

List<AlbumWithArtist> albums = GetAlbums.Query<List<AlbumWithArtist>>(cnn);
```

Rinku can also fold repeated joined rows into nested collections.

## Make SQL conditional

```csharp
static readonly QueryCommand SearchAlbums = new(
    "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@artistId AND Title LIKE ?@title");

List<Album> albums = SearchAlbums.Query<List<Album>>(
    cnn,
    new { artistId = 7 });
```

The missing `title` removes its condition.

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

## Run asynchronously

```csharp
List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(
    cnn,
    ct: cancellationToken);
```

Streaming is available when rows should be consumed as they are read.

```csharp
await foreach (Album album in GetAlbums.StreamQueryAsync<Album>(cnn, ct: cancellationToken))
    Console.WriteLine(album.Title);
```

## Generate database commands

Rinku Power Tools can inspect configured database commands and generate typed `DbCommand` methods and result records.

```csharp
static readonly CachedTypeParser<List<GetAlbumsByArtistResult>> Parser = new();

List<GetAlbumsByArtistResult> albums =
    Parser.Query(cnn.GetAlbumsByArtist(artistId: 7));
```

The generated methods return normal `DbCommand` instances. See [code generation](docs/articles/codegen/index.md) for the Visual Studio workflow and configuration.

## Analyzers and code fixes

The `Rinku` package includes analyzers and code fixes for schema links, constructor contracts, and incomplete method invocations. No separate analyzer package or PowerTools installation is required.

```csharp
/// <Schema LastUpdated="2026-08-21T14:00Z" />
public record AlbumSchema(int Id, string Title);

/// <BasedOn cref="AlbumSchema" LastUpdated="2026-08-21T14:00Z" />
public record AlbumDto(int Id, string Title);
```

See [analyzers and code fixes](docs/articles/codegen/analyzers.md) for `BasedOn`, `MatchConstructor`, schema acknowledgements, constructor generation, and method invocation completion.

## Track edits

```csharp
using Rinku.Tracking.Runtime;

Album original = new(12, "Blue");
IRuntimeTrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(original);

edit.Set(nameof(Album.Title), "Kind of Blue");

foreach (TrackingChange change in edit.GetChanges())
    Console.WriteLine($"{change.Name} {change.OriginalValue} -> {change.Value}");
```

Tracking reports application changes. Persistence remains application code.

## Documentation

Start with the [documentation index](docs/articles/index.md) or the [overview](docs/articles/overview.md).

The main guides cover [running queries](docs/articles/running-queries/execution.md), [mapping](docs/articles/mapping/objects.md), [conditional SQL](docs/articles/conditional-sql/variables.md), [advanced customization](docs/articles/customization/index.md), [code generation](docs/articles/codegen/index.md), [analyzers and code fixes](docs/articles/codegen/analyzers.md), [tracking](docs/articles/tracking/index.md), and [coming from Dapper](docs/articles/reference/dapper.md).
