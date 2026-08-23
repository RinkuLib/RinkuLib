# Dynamic projection

## Select returned columns

```csharp
static readonly QueryCommand SearchAlbums = new("""
?SELECT
    AlbumId AS Id!,
    Title,
    ReleaseYear
FROM albums
""");

var builder = SearchAlbums.StartBuilder();
builder.Use("Title");

List<DynaObject> rows = builder.Query<List<DynaObject>>(cnn);
```

```sql
SELECT
    AlbumId AS Id,
    Title
FROM albums
```

## Projection key from final alias

```sql
?SELECT
    AlbumId AS Id,
    Title,
    ReleaseYear AS Year
FROM albums
```

```csharp
var builder = SearchAlbums.StartBuilder();
builder.Use("Year");
```

Calculated columns can expose an explicit key with an alias.

```sql
?SELECT
    AlbumId AS Id!,
    Price * Quantity AS Total
FROM invoice_lines
```

## Required projection entry

```sql
?SELECT
    AlbumId AS Id!,
    Title,
    ReleaseYear
FROM albums
```

`Id` remains even when it is not selected by the caller.

## Supporting column group

```sql
?SELECT
    ArtistId&,
    ArtistName AS Artist,
    AlbumId AS Id!
FROM albums
```

Selecting `Artist` keeps both `ArtistId` and `ArtistName`.

## Projection entry with a marker

```sql
?SELECT
    AlbumId AS Id!,
    /*WithTitle*/ Title
FROM albums
```

The projection key and the marker remain separate conditions.

[Markers](markers.md)

## UNION

```sql
?SELECT Id!, Name
FROM active_artists
UNION ALL
?SELECT Id!, Name
FROM archived_artists
```

The same alias key controls matching projection entries in both branches.

## CTE

```sql
WITH albums_cte AS
(
    ?SELECT AlbumId AS Id!, Title, ReleaseYear
    FROM albums
)
SELECT * FROM albums_cte
```

## Optional modifier

```sql
???Distinct DISTINCT
?SELECT AlbumId AS Id!, Title
FROM albums
```

```csharp
var builder = SearchAlbums.StartBuilder();
builder.Use("Distinct");
builder.Use("Title");
```

[Template syntax](template-syntax.md)
