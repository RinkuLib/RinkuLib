# Multi-row mapping

## List inside a mapped value

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, List<Album> Albums);

static readonly QueryCommand GetArtists = new("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumsId, al.Title AS AlbumsTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId");

List<Artist> artists = GetArtists.Query<List<Artist>>(cnn);
```

`List<Album>` folds consecutive rows while `Album` keeps its mapping.

Rows for one parent group stay consecutive.

[Grouping](grouping.md)

## Name adaptation on the same path

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, [Alt("Album")] List<Album> Albums);

List<Artist> artists = cnn.Query<List<Artist>>("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumId, al.Title AS AlbumTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId");
// AlbumId reaches Albums.Id through Alt("Album").
```

[Name adaptation](names.md)

## Left join with no child

```csharp
public record Album([AbortOnNull] int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, List<Album> Albums);

List<Artist> artists = cnn.Query<List<Artist>>("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumsId, al.Title AS AlbumsTitle FROM artists ar LEFT JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId");
// NULL AlbumsId keeps the Artist and contributes no Album.
```

[Database NULL](nulls.md)

## Scalar elements

```csharp
public record Palette(int Id, List<string> Colors);

Palette palette = cnn.Query<Palette>("SELECT PaletteId AS Id, Color AS Colors FROM palette_colors WHERE PaletteId = @paletteId ORDER BY SortOrder", new { paletteId = 3 });
// Database NULL Colors elements are skipped.
```

```csharp
public record Palette(int Id, [KeepNullElements] List<string?> Colors);
// Database NULL Colors elements remain in the collection.
```

## Another multi-row mapping inside the element shape

```csharp
public record Track(int Id, string Name) : IDbReadable;
public record Album(int Id, string Title, List<Track> Tracks) : IDbReadable;
public record Artist(int Id, string Name, List<Album> Albums);

static readonly QueryCommand GetArtists = new("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumsId, al.Title AS AlbumsTitle, tr.TrackId AS AlbumsTracksId, tr.Name AS AlbumsTracksName FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId JOIN tracks tr ON tr.AlbumId = al.AlbumId ORDER BY ar.ArtistId, al.AlbumId");

List<Artist> artists = GetArtists.Query<List<Artist>>(cnn);
// Artist.Albums folds rows at the Artist level.
// Album.Tracks folds rows while each Album is mapped.
```

Each multi-row mapping negotiates its own boundary at the point where it appears in the type shape.

[Grouping](grouping.md)

## Side by side multi-row mappings

```csharp
public record OrderItem([AbortOnNull] int Id, decimal Price) : IDbReadable;
public record OrderNote([AbortOnNull] int Id, string Text) : IDbReadable;
public record Order(int Id, List<OrderItem> Items, List<OrderNote> Notes);

List<Order> orders = cnn.Query<List<Order>>("SELECT o.OrderId AS Id, i.ItemId AS ItemsId, i.Price AS ItemsPrice, n.NoteId AS NotesId, n.Text AS NotesText FROM orders o LEFT JOIN order_items i ON i.OrderId = o.OrderId LEFT JOIN order_notes n ON n.OrderId = o.OrderId ORDER BY o.OrderId");
// Each row is offered to both multi-row mappings.
// AbortOnNull lets either side contribute no element on that row.
```

The two mappings still use their own element mapping and grouping behavior.

[Grouping](grouping.md)

## Built in collection result shapes

```csharp
const string sql = "SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId";

List<Album> list = cnn.Query<List<Album>>(sql);
Album[] array = cnn.Query<Album[]>(sql);
IEnumerable<Album> stream = cnn.Query<IEnumerable<Album>>(sql);
```

`List<T>`, arrays, and `IEnumerable<T>` have built in multi-row mappings.

[Custom multi-row mappings](../customization/multi-row.md)
