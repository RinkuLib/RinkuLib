# Async execution

```csharp
public record Album(int Id, string Title);
public record Artist(int Id, string Name);

static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");
static readonly QueryCommand RenameAlbum = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId");
static readonly QueryCommand AddAlbum = new("INSERT INTO albums (ArtistId, Title) VALUES (@artistId, @title); SELECT CAST(SCOPE_IDENTITY() AS int);");
static readonly QueryCommand GetDashboard = new("SELECT ArtistId AS Id, Name FROM artists WHERE ArtistId = @artistId; SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");
```

## Buffered query

```csharp
List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(cnn, new { artistId = 7 }, ct: cancellationToken);
```

The result shape is the same result shape used by the synchronous query.

[Result shapes](result-shapes.md)

## Execute

```csharp
int affected = await RenameAlbum.ExecuteAsync(cnn, new { albumId = 12, title = "Kind of Blue" }, ct: cancellationToken);
```

## Scalar

```csharp
int albumId = await AddAlbum.ExecuteScalarAsync<int>(cnn, new { artistId = 7, title = "Blue" }, ct: cancellationToken);
```

## Async stream

```csharp
await foreach (Album album in GetAlbums.StreamQueryAsync<Album>(cnn, new { artistId = 7 }, ct: cancellationToken))
    Console.WriteLine(album.Title);
```

`StreamQueryAsync<T>` returns the element stream directly.

`QueryAsync<T>` can also request an async stream as its complete result shape.

```csharp
IAsyncEnumerable<Album> albums = await GetAlbums.QueryAsync<IAsyncEnumerable<Album>>(cnn, new { artistId = 7 }, ct: cancellationToken);

await foreach (Album album in albums.WithCancellation(cancellationToken))
    Console.WriteLine(album.Title);
```

[Result shapes](result-shapes.md) · [Streaming](streaming.md)

## Reader

```csharp
DbDataReader reader = await GetAlbums.ExecuteReaderAsync(cnn, out DbCommand command, new { artistId = 7 }, ct: cancellationToken);

await using (command)
await using (reader)
{
    while (await reader.ReadAsync(cancellationToken))
        Console.WriteLine(reader.GetString(1));
}
```

[Raw readers](readers.md)

## Several result sets

```csharp
await using MultiReader results = await GetDashboard.ExecuteMultiReaderAsync(cnn, new { artistId = 7 }, ct: cancellationToken);

Artist artist = await results.QueryAsync<Artist>(ct: cancellationToken);
List<Album> albums = await results.QueryAsync<List<Album>>(ct: cancellationToken);
```

[Multiple results](multiple-results.md)
