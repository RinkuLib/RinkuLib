# Collections from database results

A joined result can fill a nested collection directly.

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, List<Album> Albums);

static readonly QueryCommand GetArtists = new("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumsId, al.Title AS AlbumsTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId");

List<Artist> artists = GetArtists.Query<List<Artist>>(cnn);
```

Rows that belong to one parent must be consecutive. Order by the parent key when the database does not already guarantee that order.

## Collection prefixes

The collection member name prefixes the element columns.

```text
AlbumsId
AlbumsTitle
```

Use `[Alt]` when another prefix should also be accepted.

```csharp
public record Artist(int Id, string Name, [Alt("Album")] List<Album> Albums);
```

Now `AlbumId` and `AlbumTitle` can fill the collection.

## Keep parents with no children

Use `[AbortOnNull]` on the child identity when a left join should produce no child object.

```csharp
public record Album([AbortOnNull] int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, List<Album> Albums);
```

A row with database `NULL` in `AlbumsId` keeps the parent and adds no child.

## Null scalar elements

Null collection elements are skipped by default.

```csharp
public record Palette(int Id, List<string> Colors);
```

Use `[KeepNullElements]` when null elements should remain.

```csharp
public record Palette(int Id, [KeepNullElements] List<string?> Colors);
```

See [database NULL](nulls.md) for collection element null handling.

## Nested collections

A collection element can contain another collection.

```csharp
public record Track(int Id, string Name) : IDbReadable;
public record Album(int Id, string Title, List<Track> Tracks) : IDbReadable;
public record Artist(int Id, string Name, List<Album> Albums);
```

The returned columns use the complete path such as `AlbumsTracksId` and `AlbumsTracksName`.

Each level needs a usable grouping boundary.

## Side by side collections

One parent can fill several child collections from the same joined rows.

```csharp
public record OrderItem([AbortOnNull] int Id, decimal Price) : IDbReadable;
public record OrderNote([AbortOnNull] int Id, string Text) : IDbReadable;
public record Order(int Id, List<OrderItem> Items, List<OrderNote> Notes);
```

A row can contribute to one collection while the other collection receives no element.

## Several result sets

Several result sets avoid repeating large parent columns.

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record class Artist(int Id, string Name)
{
    public List<Album> Albums { get; } = [];
}

using MultiReader results = GetArtistsAndAlbums.ExecuteMultiReader(cnn);

List<Artist> artists = results.Query<List<Artist>>();
using IEnumerator<(int ArtistId, Album Album)> albums = results.Query<IEnumerable<(int, Album)>>().GetEnumerator();
```

Application code can merge the two ordered sets by parent key. See [multiple result sets](../running-queries/multiple-results.md).

## Supported collection shapes

`List<T>`, arrays, and `IEnumerable<T>` have built in multi row mappings.

Use [custom multi row types](../customization/multi-row.md) when another collection shape needs its own mapping behavior.

See [grouping](grouping.md) for parent boundaries.
