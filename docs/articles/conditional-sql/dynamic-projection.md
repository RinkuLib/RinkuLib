# Dynamic projection

`?SELECT` lets the caller choose columns while keeping a valid projection.

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

The generated projection keeps the required `Id` column and the selected `Title` column.

```sql
SELECT
    AlbumId AS Id,
    Title
FROM albums
```

## Derived keys

The final column name becomes the key used by the builder.

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

Calculated columns should use an explicit alias so their key is clear.

```sql
?SELECT
    AlbumId AS Id!,
    Price * Quantity AS Total
FROM invoice_lines
```

## Required columns

Add `!` to a projection entry that must always remain.

```sql
?SELECT
    AlbumId AS Id!,
    Title,
    ReleaseYear
FROM albums
```

`Id` stays in the generated SQL even when the caller does not select it.

## Group a supporting column

`&,` groups a column with the next projection key.

```sql
?SELECT
    ArtistId&,
    ArtistName AS Artist,
    AlbumId AS Id!
FROM albums
```

Selecting `Artist` keeps both `ArtistId` and `ArtistName`.

## Add another condition

A projection entry can also use an explicit marker.

```sql
?SELECT
    AlbumId AS Id!,
    /*WithTitle*/ Title
FROM albums
```

The projection key and marker can then be controlled separately when that is useful.

## UNION

Matching aliases can share the same projection key across a union.

```sql
?SELECT Id!, Name
FROM active_artists
UNION ALL
?SELECT Id!, Name
FROM archived_artists
```

Selecting `Name` applies to both projections.

## CTEs

Dynamic projection can be used inside a common table expression.

```sql
WITH albums_cte AS
(
    ?SELECT AlbumId AS Id!, Title, ReleaseYear
    FROM albums
)
SELECT *
FROM albums_cte
```

## Optional modifiers

Use `???` when a modifier should be controlled separately from the projection.

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

See [Template syntax](template-syntax.md) for the syntax rules used by the parser.
