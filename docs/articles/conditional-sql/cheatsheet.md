# Conditional SQL cheat sheet

## Optional value

```sql
WHERE ArtistId = ?@artistId
```

[Conditional variables](variables.md)

## Named marker

```sql
WHERE /*ByArtist*/ ArtistId = @artistId
```

```csharp
builder.Use("ByArtist");
```

[Markers](markers.md)

## Parameter marker

```sql
WHERE /*@artistId*/ ArtistId = @artistId
```

[Markers](markers.md)

## Marker logic

```sql
/*A&B*/
/*A|B*/
/*!A*/
```

[Markers](markers.md)

## Collection

```sql
WHERE AlbumId IN (@ids_X)
```

[Collection expansion](collections.md)

## Optional collection

```sql
WHERE AlbumId IN (?@ids_X)
```

[Collection expansion](collections.md)

## Numeric text

```sql
OFFSET @skip_N ROWS
```

[Value handlers](handlers.md)

## Quoted text

```sql
ORDER BY @name_S
```

[Value handlers](handlers.md)

## Raw text

```sql
ORDER BY @orderBy_R
```

[Value handlers](handlers.md)

## Dynamic projection

```sql
?SELECT
    AlbumId AS Id!,
    Title,
    ReleaseYear AS Year
FROM albums
```

[Dynamic projection](dynamic-projection.md)

## Projection group

```sql
?SELECT
    ArtistId&,
    ArtistName AS Artist,
    AlbumId AS Id!
FROM albums
```

[Dynamic projection](dynamic-projection.md)

## Optional modifier

```sql
???Distinct DISTINCT
?SELECT AlbumId AS Id!, Title FROM albums
```

[Dynamic projection](dynamic-projection.md)

## Normal block comment

```sql
/*~ sent to the database */
```

[Template syntax](template-syntax.md)
