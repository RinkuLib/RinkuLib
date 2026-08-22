# RinkuLib

Rinku is a micro ORM for .NET built directly on ADO.NET. SQL stays explicit instead of being generated from an object model. Mapping and configuration adapt between database shapes and .NET shapes so neither side has to be designed around the mapper.

## Install

```bash
dotnet add package Rinku
```

```csharp
using Rinku;

public record Album(int Id, string Title) : IDbReadable;

static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
```

## Adapt either side

```sql
SELECT customer_id AS Id, display_name AS Name FROM customers
```

Or leave both SQL and the model unchanged and register the boundary rule.

```csharp
TypeParsingInfo.GetOrAdd<Customer>().UpdateAltName(names =>
    names.GetDefaultName() switch
    {
        "Id" => new NameComparer("customer_id"),
        "Name" => new NameComparer("display_name"),
        _ => null
    });
```

See [adapt names](https://rinkulib.github.io/RinkuLib/articles/mapping/names.html).

## Result shapes

```csharp
Album first = GetAlbum.Query<Album>(cnn, new { albumId = 12 });
Optional<Album> maybe = GetAlbum.Query<Optional<Album>>(cnn, new { albumId = 12 });
List<Album> all = GetAlbums.Query<List<Album>>(cnn);
IEnumerable<Album> streamed = GetAlbums.Query<IEnumerable<Album>>(cnn);
```

See [result shapes](https://rinkulib.github.io/RinkuLib/articles/running-queries/result-shapes.html).

## Conditional SQL

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId AND Title LIKE ?@title");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
// title is absent:
// SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

See [conditional SQL](https://rinkulib.github.io/RinkuLib/articles/conditional-sql/variables.html).

## Nested mapping

```csharp
public record Artist(int Id, string Name) : IDbReadable;
public record AlbumWithArtist(int Id, string Title, Artist Artist);

static readonly QueryCommand GetAlbumsWithArtist = new("SELECT al.AlbumId AS Id, al.Title, ar.ArtistId AS ArtistId, ar.Name AS ArtistName FROM albums al JOIN artists ar ON ar.ArtistId = al.ArtistId");

List<AlbumWithArtist> albums = GetAlbumsWithArtist.Query<List<AlbumWithArtist>>(cnn);
```

See [nested objects](https://rinkulib.github.io/RinkuLib/articles/mapping/nesting.html), [collections](https://rinkulib.github.io/RinkuLib/articles/mapping/collections.html), and [grouping](https://rinkulib.github.io/RinkuLib/articles/mapping/grouping.html).

## Code generation

```csharp
static readonly CachedTypeParser<List<GetAlbumsByArtistResult>> Parser = new();

List<GetAlbumsByArtistResult> albums = Parser.Query(cnn.GetAlbumsByArtist(artistId: 7));
```

See [code generation](https://rinkulib.github.io/RinkuLib/articles/codegen/index.html) and [analyzers](https://rinkulib.github.io/RinkuLib/articles/codegen/analyzers.html).

## Tracking

```csharp
Album original = new(12, "Blue");
IRuntimeTrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(original);

edit.Set(nameof(Album.Title), "Kind of Blue");

foreach (TrackingChange change in edit.GetChanges())
{
    // Computed from accepted value + current edit state.
    // No separate per-member mutation history is stored.
    Console.WriteLine($"{change.Name} {change.OriginalValue} -> {change.Value}");
}
```

See [tracking](https://rinkulib.github.io/RinkuLib/articles/tracking/index.html).

## Async

```csharp
List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(cnn, ct: cancellationToken);
```

See the full [Rinku documentation](https://rinkulib.github.io/RinkuLib/articles/index.html).
