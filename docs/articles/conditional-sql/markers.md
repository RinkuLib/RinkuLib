# Conditional markers

## Named marker

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE /*ByArtist*/ ArtistId = @artistId");

var builder = SearchAlbums.StartBuilder();
builder.Use("ByArtist");
builder.Use("@artistId", 7);

List<Album> albums = builder.Query<List<Album>>(cnn);
// SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

Without the marker value, its SQL is absent.

```csharp
List<Album> albums = SearchAlbums.Query<List<Album>>(cnn);
// SELECT AlbumId AS Id, Title FROM albums
```

## Marker tied to parameter presence

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE /*@artistId*/ ArtistId = @artistId");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
```

The marker follows the presence of `artistId`.

## Boolean member to marker

```csharp
public sealed class AlbumFilter
{
    [ForBoolCond]
    public bool OnlyReleased { get; init; }
}

static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE /*OnlyReleased*/ ReleaseYear IS NOT NULL");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new AlbumFilter { OnlyReleased = true });
```

Several markers can be attached to one parameter source.

```csharp
[UsesBoolConds("IncludeYear", "IncludeArtist")]
public sealed class AlbumFilter
{
    public bool IncludeDetails { get; init; }
}
```

## One term inside parentheses

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE (/*ByTitle*/ Title = @title OR IsFeatured = 1)
```

When `ByTitle` is absent, `IsFeatured = 1` remains.

## Larger SQL section

```sql
SELECT a.AlbumId AS Id, a.Title FROM albums a /*WithArtist*/ JOIN artists ar ON ar.ArtistId = a.ArtistId
```

The marker owns the removable `JOIN` section.

## Marker logic

```sql
WHERE /*A*//*B*/ IsPublished = 1
WHERE /*A&B*/ IsPublished = 1
WHERE /*A|B*/ IsPublished = 1
WHERE /*!All*/ IsPublished = 1
```

Adjacent markers are an implicit `AND`. Explicit expressions support `&`, `|`, and `!` and are evaluated from left to right.

```sql
/*A|B&C*/
```

```text
(A OR B) AND C
```

## Marker owned separator

```sql
SELECT AlbumId, Title FROM albums ORDER BY /*Title*/ Title&, /*Year*/ ReleaseYear&, AlbumId
```

The owned separators disappear with their optional entries.

[Cheat sheet](cheatsheet.md)
