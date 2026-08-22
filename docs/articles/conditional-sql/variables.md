# Conditional variables

Conditional variables remove the SQL that depends on a value when that value is not supplied.

```csharp
static readonly QueryCommand SearchAlbums = new("""
SELECT AlbumId AS Id, Title
FROM albums
WHERE ArtistId = ?@artistId
AND Title LIKE ?@title
""");

var builder = SearchAlbums.StartBuilder();
builder.UseWith(new { artistId = 7 });

List<Album> albums = builder.Query<List<Album>>(cnn);
```

The SQL used by this call is equivalent to the following SQL.

```sql
SELECT AlbumId AS Id, Title
FROM albums
WHERE ArtistId = @artistId
```

If neither value is supplied, Rinku also removes the empty `WHERE` clause.

```csharp
List<Album> albums = SearchAlbums
    .StartBuilder()
    .Query<List<Album>>(cnn);
```

```sql
SELECT AlbumId AS Id, Title
FROM albums
```

## Required values inside an optional part

A normal variable can live inside SQL controlled by a conditional variable.

```csharp
static readonly QueryCommand SearchAroundYear = new("""
SELECT AlbumId AS Id, Title
FROM albums
WHERE ReleaseYear BETWEEN ?@fromYear AND @toYear
""");

var builder = SearchAroundYear.StartBuilder();
builder.UseWith(new { fromYear = 1990, toYear = 2000 });
```

`toYear` is required when `fromYear` keeps the condition.

When `fromYear` is absent, the complete condition is removed and `toYear` is not required.

## Updates

Conditional variables can remove assignments too.

```csharp
static readonly QueryCommand UpdateAlbum = new("""
UPDATE albums
SET Title = ?@title,
    ReleaseYear = ?@releaseYear
WHERE AlbumId = @albumId
""");

var builder = UpdateAlbum.StartBuilder();
builder.UseWith(new
{
    albumId = 12,
    title = "New title"
});

builder.Execute(cnn);
```

The assignment without a supplied value is removed.

```sql
UPDATE albums
SET Title = @title
WHERE AlbumId = @albumId
```

## Parentheses

A conditional variable can remove the surrounding expression when that expression only exists for the value.

```csharp
static readonly QueryCommand Search = new("""
SELECT AlbumId AS Id, Title
FROM albums
WHERE (?@title IS NULL OR Title = @title)
""");
```

If `title` is absent, Rinku can remove the complete parenthesized condition.

Use an [explicit marker](markers.md) when only one term inside parentheses should be optional.

## Values that count as absent

A source member with `null` is absent by default.

```csharp
string? title = null;

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new { title });
```

Use `DBNull.Value` when the parameter must remain present and carry database `NULL`.

```csharp
List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new
{
    title = DBNull.Value
});
```

More value rules are shown in [Supplying values](../running-queries/values.md).

## Collections

Collections use the `_X` handler for parameter expansion.

```sql
SELECT AlbumId AS Id, Title
FROM albums
WHERE AlbumId IN (?@ids_X)
```

See [Collections](collections.md) for expansion and empty collection behavior.
