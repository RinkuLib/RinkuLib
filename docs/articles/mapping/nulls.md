# Database NULL

## Nullable value type

```csharp
public record Album(int Id, decimal? Price);

Album album = cnn.Query<Album>("SELECT AlbumId AS Id, Price FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
```

A non nullable value type rejects a database `NULL`.

```csharp
public record Album(int Id, decimal Price);

Album album = cnn.Query<Album>("SELECT AlbumId AS Id, NULL AS Price FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
// RINKU4003
```

[Errors](../reference/errors.md#rinku4003-database-null-not-allowed)

## Reference slots

```csharp
public record Album(int Id, string? Title);

Album album = cnn.Query<Album>("SELECT AlbumId AS Id, NULL AS Title FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
```

Reference slots accept database `NULL` by default. Nullable reference annotations do not change runtime mapping behavior.

`[NotNull]` rejects database `NULL` for a reference slot.

```csharp
public record Album(int Id, [NotNull] string Title);
```

`[MaybeNull]` accepts database `NULL` for any slot.

```csharp
public record InventoryItem(int Id, [MaybeNull] int Count);
// Database NULL Count becomes default(int).
```

## Abort nested construction

```csharp
public record Album([AbortOnNull] int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, Album? LatestAlbum);

Artist artist = cnn.Query<Artist>("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS LatestAlbumId, al.Title AS LatestAlbumTitle FROM artists ar LEFT JOIN albums al ON al.AlbumId = ar.LatestAlbumId WHERE ar.ArtistId = @artistId", new { artistId = 7 });
// NULL LatestAlbumId aborts the nested Album.
```

Abort can propagate through another nested slot.

```csharp
public record Bottom([AbortOnNull] int Key, string Name) : IDbReadable;
public record Middle(int Id, [AbortOnNull] Bottom Bottom) : IDbReadable;
public record Top(int Id, Middle? Middle);
```

## Multi-row elements

```csharp
public record Palette(int Id, List<string> Colors);
// Database NULL elements are skipped.
```

```csharp
public record Palette(int Id, [KeepNullElements] List<string?> Colors);
// Database NULL elements are retained.
```

[Multi-row mapping](collections.md)

## Whole result

```csharp
string? title = cnn.Query<MaybeNull<string>>("SELECT Title FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
// A row is required. Its value may be database NULL.
```

```csharp
OptionalNullable<string> title = cnn.Query<OptionalNullable<string>>("SELECT Title FROM albums WHERE AlbumId = @albumId", new { albumId = 999 });
// No row and a row containing database NULL remain represented separately.
```

[Result shapes](../running-queries/result-shapes.md)

## Parameter database NULL

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE Title = ?@title");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new { title = (string?)null });
// null is absent, so the optional condition disappears.
```

```csharp
static readonly QueryCommand UpdateTitle = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId");

UpdateTitle.Execute(cnn, new { albumId = 12, title = DBNull.Value });
// DBNull.Value is sent as database NULL.
```

A nullable member can map its `null` value to database `NULL`.

```csharp
public record AlbumTitleUpdate(int AlbumId, [property: UseDbNull] string? Title);

UpdateTitle.Execute(cnn, new AlbumTitleUpdate(12, null));
```

[Supplying values](../running-queries/values.md)
