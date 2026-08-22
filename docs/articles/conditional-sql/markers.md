# Markers

A marker gives a piece of SQL a name that can be enabled by the caller.

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE /*ByArtist*/ ArtistId = @artistId");

var builder = SearchAlbums.StartBuilder();
builder.Use("ByArtist");
builder.Use("@artistId", 7);

List<Album> albums = builder.Query<List<Album>>(cnn);
```

Without `ByArtist`, the condition is removed.

```sql
SELECT AlbumId AS Id, Title FROM albums
```

Supplying `ByArtist` keeps the condition in the SQL.

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

## Tie a marker to a parameter

Use `/*@name*/` when the SQL should follow parameter presence.

```csharp
static readonly QueryCommand SearchAlbums = new("""
SELECT AlbumId AS Id, Title
FROM albums
WHERE /*@artistId*/ ArtistId = @artistId
""");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new
{
    artistId = 7
});
```

This is useful when the SQL has a larger shape than a single `?@artistId` footprint.

## Bool conditions from a value source

`ForBoolCond` maps a member to a marker.

```csharp
public sealed class AlbumFilter
{
    [ForBoolCond]
    public bool OnlyReleased { get; init; }
}
```

```sql
SELECT AlbumId AS Id, Title
FROM albums
WHERE /*OnlyReleased*/ ReleaseYear IS NOT NULL
```

```csharp
List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new AlbumFilter
{
    OnlyReleased = true
});
```

`UsesBoolConds` enables several markers whenever the parameter object is used.

```csharp
[UsesBoolConds("IncludeYear", "IncludeArtist")]
public sealed class AlbumFilter
{
    public bool IncludeDetails { get; init; }
}
```

## Parenthesized expressions

An explicit marker can remove only one term inside parentheses.

```sql
SELECT AlbumId AS Id, Title
FROM albums
WHERE (/*ByTitle*/ Title = @title OR IsFeatured = 1)
```

When `ByTitle` is disabled, `IsFeatured = 1` remains.

## Larger SQL sections

Put the marker before the section that it owns.

```sql
SELECT a.AlbumId AS Id, a.Title
FROM albums a
/*WithArtist*/ JOIN artists ar ON ar.ArtistId = a.ArtistId
```

The same pattern can control `JOIN`, `GROUP BY`, `HAVING`, and other removable sections.

## Several markers

Adjacent markers use an implicit `AND`.

```sql
WHERE /*A*//*B*/ IsPublished = 1
```

The condition remains only when both markers are active.

Use `&`, `|`, and `!` to write explicit marker logic.

```sql
WHERE /*A&B*/ IsPublished = 1
WHERE /*A|B*/ IsPublished = 1
WHERE /*!All*/ IsPublished = 1
```

Expressions are evaluated from left to right.

```sql
/*A|B&C*/
```

The equivalent expression is shown below.

```text
(A OR B) AND C
```

## Marker groups in lists

Markers can own separators as well as values.

```sql
SELECT AlbumId, Title
FROM albums
ORDER BY /*Title*/ Title&, /*Year*/ ReleaseYear&, AlbumId
```

This keeps the comma list valid when optional entries are removed.

See [Cheat sheet](cheatsheet.md) for the compact marker forms.
