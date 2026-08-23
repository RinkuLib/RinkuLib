# Conditional variables

## Optional WHERE values

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@artistId AND Title LIKE ?@title");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
// SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

```csharp
List<Album> albums = SearchAlbums.Query<List<Album>>(cnn);
// SELECT AlbumId AS Id, Title FROM albums
```

The SQL controlled by a conditional variable is removed when that value is absent. Empty SQL structure such as the unused `WHERE` is removed with it.

## Required value inside optional SQL

```csharp
static readonly QueryCommand SearchAroundYear = new("SELECT AlbumId AS Id, Title FROM albums WHERE ReleaseYear BETWEEN ?@fromYear AND @toYear");

List<Album> albums = SearchAroundYear.Query<List<Album>>(cnn, new { fromYear = 1990, toYear = 2000 });
```

`toYear` is required while the `fromYear` condition remains.

```csharp
List<Album> albums = SearchAroundYear.Query<List<Album>>(cnn);
// The complete BETWEEN condition is absent, so toYear is not required by the generated SQL.
```

## Optional SET entries

```csharp
static readonly QueryCommand UpdateAlbum = new("UPDATE albums SET Title = ?@title, ReleaseYear = ?@releaseYear WHERE AlbumId = @albumId");

UpdateAlbum.Execute(cnn, new { albumId = 12, title = "New title" });
// UPDATE albums SET Title = @title WHERE AlbumId = @albumId
```

## Parenthesized condition

```csharp
static readonly QueryCommand Search = new("SELECT AlbumId AS Id, Title FROM albums WHERE (?@title IS NULL OR Title = @title)");
```

When `title` is absent, the complete parenthesized condition can disappear.

[Markers](markers.md) shows a marker that controls only one term inside parentheses.

## Null and database NULL

```csharp
string? title = null;
List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new { title });
// title is absent.
```

```csharp
List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new { title = DBNull.Value });
// title remains present and carries database NULL.
```

[Supplying values](../running-queries/values.md)

## Collection expansion

```csharp
static readonly QueryCommand SearchIds = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (?@ids_X)");
```

[Collection expansion](collections.md)
