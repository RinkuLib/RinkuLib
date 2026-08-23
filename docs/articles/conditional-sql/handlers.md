# Value handlers

## Normal database parameter

```csharp
static readonly QueryCommand ByArtist = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

List<Album> albums = ByArtist.Query<List<Album>>(cnn, new { artistId = 7 });
```

## Collection expansion

```csharp
static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (@ids_X)");

int[] ids = [1, 4, 8];
List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { ids });
```

[Collection expansion](collections.md)

## Numeric SQL text

```csharp
static readonly QueryCommand Page = new("SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId OFFSET @skip_N ROWS FETCH NEXT @take_N ROWS ONLY");

List<Album> albums = Page.Query<List<Album>>(cnn, new { skip = 20, take = 10 });
```

`_N` writes invariant numeric text. Boolean values become `1` or `0`. Enum values use their underlying number. Numeric strings are accepted when they represent a valid number.

## Quoted invariant text

```csharp
static readonly QueryCommand Sort = new("SELECT AlbumId AS Id, Title FROM albums ORDER BY @column_S");

List<Album> albums = Sort.Query<List<Album>>(cnn, new { column = "Title" });
```

`_S` writes quoted invariant text and doubles embedded single quotes.

## Raw text

```csharp
static readonly QueryCommand Sort = new("SELECT AlbumId AS Id, Title FROM albums ORDER BY @orderBy_R");

List<Album> albums = Sort.Query<List<Album>>(cnn, new { orderBy = "Title DESC" });
```

`_R` writes `ToString()` without escaping. Keep the supplied value application controlled.

## Custom handler

```csharp
public enum SortDirection
{
    Ascending,
    Descending
}

sealed class SortDirectionHandler : IQuerySegmentHandler
{
    public void Handle(ref ValueStringBuilder query, object value)
    {
        if (value is not SortDirection direction)
            throw new ArgumentException("Expected SortDirection", nameof(value));
        query.Append(direction == SortDirection.Ascending ? "ASC" : "DESC");
    }
}

QueryFactory.BaseHandlerMapper['D'] = _ => new SortDirectionHandler();
```

```csharp
static readonly QueryCommand OrderedAlbums = new("SELECT AlbumId AS Id, Title FROM albums ORDER BY Title @direction_D");

List<Album> albums = OrderedAlbums.Query<List<Album>>(cnn, new { direction = SortDirection.Descending });
// SELECT AlbumId AS Id, Title FROM albums ORDER BY Title DESC
```

[Custom conditional SQL](../customization/conditional-sql.md)
