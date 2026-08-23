# Result shapes

```csharp
public record Album(int Id, string Title);
public record NestedAlbum(int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, List<NestedAlbum> Albums);

const string albumsSql = "SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId";
const string albumSql = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId";
```

The requested result type controls how complete mapped values are consumed.

## First result

```csharp
Album album = cnn.Query<Album>(albumsSql);
```

Only the first complete mapped `Album` is returned.

A complete mapped value can itself consume several database rows.

```csharp
const string artistSql = "SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumsId, al.Title AS AlbumsTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId WHERE ar.ArtistId = @artistId ORDER BY ar.ArtistId";

Artist artist = cnn.Query<Artist>(artistSql, new { artistId = 7 });
// Artist is one complete result even when Albums folds several rows.
```

[Multi-row mapping](../mapping/collections.md)

## First result or none

```csharp
Album? album = cnn.Query<Optional<Album>>(albumSql, new { albumId = 999 });
```

For a value type the struct wrapper carries the absent state.

```csharp
int? count = cnn.Query<OptionalStruct<int>>("SELECT COUNT(*) FROM albums WHERE 1 = 0");
```

## Exactly one result

```csharp
Album album = cnn.Query<Single<Album>>(albumSql, new { albumId = 12 });
// No complete result produces RINKU4001.
// A second complete result produces RINKU4002.
```

## Zero or one result

```csharp
Album? album = cnn.Query<SingleOrDefault<Album>>(albumSql, new { albumId = 999 });
int? count = cnn.Query<SingleOrDefaultStruct<int>>("SELECT COUNT(*) FROM albums WHERE 1 = 0");
```

## Buffered collections

```csharp
List<Album> list = cnn.Query<List<Album>>(albumsSql);
Album[] array = cnn.Query<Album[]>(albumsSql);
```

No complete results produce an empty collection.

## Synchronous stream

```csharp
IEnumerable<Album> albums = cnn.Query<IEnumerable<Album>>(albumsSql);

foreach (Album album in albums)
    Console.WriteLine(album.Title);
```

[Streaming lifetime](streaming.md)

## Present database NULL

```csharp
string? title = cnn.Query<MaybeNull<string>>("SELECT Title FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
int? year = cnn.Query<int?>("SELECT ReleaseYear FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
// A row is required. Database NULL is accepted.
```

## No row or database NULL

```csharp
OptionalNullable<string> title = cnn.Query<OptionalNullable<string>>("SELECT Title FROM albums WHERE AlbumId = @albumId", new { albumId = 999 });
OptionalNullableStruct<int> year = cnn.Query<OptionalNullableStruct<int>>("SELECT ReleaseYear FROM albums WHERE AlbumId = @albumId", new { albumId = 999 });
```

No returned row and a returned database `NULL` remain separate states.

[Database NULL](../mapping/nulls.md)

## Exactly one result including database NULL

```csharp
Single<MaybeNull<string>> title = cnn.Query<Single<MaybeNull<string>>>("SELECT Title FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
Single<int?> year = cnn.Query<Single<int?>>("SELECT ReleaseYear FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
```

## At most one result including database NULL

```csharp
SingleOrDefaultNullable<string> title = cnn.Query<SingleOrDefaultNullable<string>>("SELECT Title FROM albums WHERE AlbumId = @albumId", new { albumId = 999 });
SingleOrDefaultNullableStruct<int> year = cnn.Query<SingleOrDefaultNullableStruct<int>>("SELECT ReleaseYear FROM albums WHERE AlbumId = @albumId", new { albumId = 999 });
```

## Scalars

```csharp
int count = cnn.Query<int>("SELECT COUNT(*) FROM albums");
```

## Tuples

```csharp
(int id, string title) = cnn.Query<(int, string)>("SELECT AlbumId, Title FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
```

[Tuple mapping](../mapping/tuples.md)

## Runtime result type

```csharp
Type resultType = typeof(List<Album>);
object? result = cnn.Query(resultType, albumsSql);
```

[Fixed result schema](fixed-result-schema.md) · [Custom complete result parsers](../customization/result-parsers.md)
