# Cheat sheet

## Optional value

```sql
WHERE ArtistId = ?@artistId
```

Missing or `null` removes the surrounding condition.

## Named marker

```sql
WHERE /*ByArtist*/ ArtistId = @artistId
```

```csharp
builder.Use("ByArtist");
```

## Parameter marker

```sql
WHERE /*@artistId*/ ArtistId = @artistId
```

The SQL follows the presence of `artistId`.

## Marker logic

```sql
/*A&B*/
/*A|B*/
/*!A*/
```

`&` means AND, `|` means OR, and `!` means NOT.

## Collection

```sql
WHERE AlbumId IN (@ids_X)
```

The collection is expanded into database parameters.

## Optional collection

```sql
WHERE AlbumId IN (?@ids_X)
```

An empty or missing collection removes the condition.

## Numeric text

```sql
OFFSET @skip_N ROWS
```

The value is written as invariant numeric SQL text.

## Quoted text

```sql
ORDER BY @name_S
```

The value is quoted and embedded into the SQL text.

## Raw text

```sql
ORDER BY @orderBy_R
```

The value is embedded without escaping. Use only application controlled values.

## Dynamic projection

```sql
?SELECT
    AlbumId AS Id!,
    Title,
    ReleaseYear AS Year
FROM albums
```

`!` keeps a projection entry even when it was not selected.

## Projection group

```sql
?SELECT
    ArtistId&,
    ArtistName AS Artist,
    AlbumId AS Id!
FROM albums
```

Selecting `Artist` keeps both grouped columns.

## Optional modifier

```sql
???Distinct DISTINCT
?SELECT AlbumId AS Id!, Title
FROM albums
```

## Keep a normal block comment

```sql
/*~ sent to the database */
```

See [Conditional variables](variables.md), [Markers](markers.md), [Collections](collections.md), and [Dynamic projection](dynamic-projection.md) for complete examples.
