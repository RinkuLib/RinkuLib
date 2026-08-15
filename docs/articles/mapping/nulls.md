# Database NULL

A nullable value type accepts database `NULL`.

```csharp
public record Album(int Id, decimal? Price);

Album album = GetAlbum.Query<Album>(cnn);
// Id | Price
// 1  | NULL  -> album.Price is null
```

A non-nullable value type rejects it.

```csharp
public record Album(int Id, decimal Price);

Album album = GetAlbum.Query<Album>(cnn);
// Id | Price
// 1  | NULL  -> RINKU4003
```

Nullable reference annotations do not exist at runtime. Both reference slots accept database `NULL` by default.

```csharp
public record Labels(string First, string? Second);

Labels labels = GetLabels.Query<Labels>(cnn);
// First | Second
// NULL  | NULL   -> both values are null
```

Use `[NotNull]` when a reference slot must reject database `NULL`.

```csharp
public record Album(int Id, [NotNull] string Title);

Album album = GetAlbum.Query<Album>(cnn);
// Id | Title
// 1  | NULL  -> RINKU4003
```

`[MaybeNull]` makes any slot accept database `NULL`.

```csharp
public record InventoryItem(int Id, [MaybeNull] int Count);

InventoryItem item = GetInventoryItem.Query<InventoryItem>(cnn);
// Id | Count
// 1  | NULL  -> item.Count is 0
```

## Missing objects from a join

A `LEFT JOIN` returns `NULL` columns when no nested row exists.

```csharp
public record Album([AbortOnNull] int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, Album? LatestAlbum);
```

```sql
SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS LatestAlbumId, al.Title AS LatestAlbumTitle FROM artists ar LEFT JOIN albums al ON al.AlbumId = ar.LatestAlbumId
```

```text
Id  Name   LatestAlbumId  LatestAlbumTitle
1   Queen  NULL           NULL
```

```csharp
Artist artist = GetArtists.Query<Artist>(cnn);
// artist.LatestAlbum is null.
```

`[AbortOnNull]` stops construction when that slot reads `NULL`. Without it, the remaining slot rules decide what happens.

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, Album? LatestAlbum);

Artist artist = GetArtists.Query<Artist>(cnn);
// RINKU4003. Album.Id cannot accept NULL.
```

An aborted object reaches the slot that contains it. A nullable slot accepts the missing object.

```csharp
public record Package([AbortOnNull] int TrackingId, double Weight) : IDbReadable;
public record Shipment(int Id, Package? Contents);

Shipment shipment = GetShipment.Query<Shipment>(cnn);
// A NULL ContentsTrackingId makes Contents null.
```

A non-nullable containing slot raises `RINKU4003`. Marking that slot with `[AbortOnNull]` carries the missing value to its parent.

```csharp
public record Bottom([AbortOnNull] int Key, string Name) : IDbReadable;
public record Middle(int Id, [AbortOnNull] Bottom Bottom) : IDbReadable;
public record Top(int Id, Middle? Middle);

Top top = GetTop.Query<Top>(cnn);
// NULL BottomKey aborts Bottom, then Middle, so Top.Middle is null.
```

## Null elements in collections

Null elements are skipped by default.

```csharp
public record Palette(int Id, List<string> Colors);

Palette palette = GetPalette.Query<Palette>(cnn);
// Colors: red | NULL | blue -> ["red", "blue"]
```

`[KeepNullElements]` retains the null elements in the collection.

```csharp
public record Palette(int Id, [KeepNullElements] List<string?> Colors);

Palette palette = GetPalette.Query<Palette>(cnn);
// Colors: red | NULL | blue -> ["red", null, "blue"]
```

`[NotNull]` rejects a null element instead of skipping it.

```csharp
public record Palette(int Id, [NotNull] List<string> Colors);

Palette palette = GetPalette.Query<Palette>(cnn);
// Colors: red | NULL | blue -> RINKU4003
```

For a missing object inside a collection, put `[AbortOnNull]` on the nested type’s identifying slot.

```csharp
public record Album([AbortOnNull] int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, List<Album> Albums);

List<Artist> artists = GetArtistsWithOptionalAlbums.Query<List<Artist>>(cnn);
// A row with NULL AlbumsId adds no Album.
```

## NULL at the root

At the root, only `Nullable<T>` accepts database `NULL` directly.

```csharp
int? count = GetNullableCount.Query<int?>(cnn);
// A returned NULL becomes null.
```

Use `MaybeNull<T>` for a reference result that may contain database `NULL`.

```csharp
string? title = GetNullableTitle.Query<MaybeNull<string>>(cnn);
```

No rows and database `NULL` remain different outcomes.

```csharp
Optional<string> noRow = FindTitle.Query<Optional<string>>(cnn);
MaybeNull<string> nullValue = GetNullableTitle.Query<MaybeNull<string>>(cnn);
OptionalNullable<string> either = FindNullableTitle.Query<OptionalNullable<string>>(cnn);
```

The result-shape reference lists the matching struct and cardinality forms.
