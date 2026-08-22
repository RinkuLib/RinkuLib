# Value handlers

A suffix can change how a value is written into the generated SQL.

Normal values should use database parameters.

```csharp
static readonly QueryCommand ByArtist = new("""
SELECT AlbumId AS Id, Title
FROM albums
WHERE ArtistId = @artistId
""");

List<Album> albums = ByArtist.Query<List<Album>>(cnn, new { artistId = 7 });
```

The built in handlers are useful when the SQL shape itself depends on a value.

## Collection expansion

`_X` expands an enumerable into database parameters.

```sql
WHERE AlbumId IN (@ids_X)
```

```csharp
int[] ids = [1, 4, 8];
List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { ids });
```

See [Collections](collections.md) for empty collection behavior.

## Numeric text

`_N` writes an invariant numeric value directly into the SQL text.

```sql
SELECT AlbumId AS Id, Title
FROM albums
ORDER BY AlbumId
OFFSET @skip_N ROWS
FETCH NEXT @take_N ROWS ONLY
```

```csharp
static readonly QueryCommand Page = new("""
SELECT AlbumId AS Id, Title
FROM albums
ORDER BY AlbumId
OFFSET @skip_N ROWS
FETCH NEXT @take_N ROWS ONLY
""");

List<Album> albums = Page.Query<List<Album>>(cnn, new
{
    skip = 20,
    take = 10
});
```

`bool` values are written as `1` or `0`. Enum values use their underlying numeric value. Numeric strings are accepted when they contain a valid numeric value.

## Quoted invariant text

`_S` writes a quoted invariant string into the SQL text.

```sql
ORDER BY @column_S
```

Single quotes inside the value are doubled.

Use this only when writing text into the SQL is actually required. A normal query value should remain a database parameter.

## Raw text

`_R` writes `ToString()` directly into the SQL without escaping.

```sql
ORDER BY @orderBy_R
```

Use `_R` only with values controlled by the application.

Do not pass user input to `_R`.

## Custom handlers

Applications can add handlers for another value format.

See [Custom conditional SQL](../customization/conditional-sql.md) for one complete registration example.
