# Async execution

Use the async form of the same operation.

```csharp
List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(cnn, new { artistId = 7 }, ct: cancellationToken);
```

The requested result shape stays the same.

```csharp
Album album = await GetAlbum.QueryAsync<Album>(cnn, new { albumId = 1 }, ct: cancellationToken);
Optional<Album> maybe = await GetAlbum.QueryAsync<Optional<Album>>(cnn, new { albumId = 999 }, ct: cancellationToken);
```

## Matching operations

```text
Query<T>             -> QueryAsync<T>
Execute              -> ExecuteAsync
ExecuteScalar<T>     -> ExecuteScalarAsync<T>
ExecuteReader        -> ExecuteReaderAsync
ExecuteMultiReader   -> ExecuteMultiReaderAsync
```

```csharp
int affected = await RenameAlbum.ExecuteAsync(cnn, new { albumId = 1, title = "Blue" }, ct: cancellationToken);
int albumId = await AddAlbum.ExecuteScalarAsync<int>(cnn, new { title = "Blue", artistId = 7 }, ct: cancellationToken);
```

## Stream rows asynchronously

```csharp
await foreach (Album album in GetAlbums.StreamQueryAsync<Album>(cnn, new { artistId = 7 }, ct: cancellationToken))
    Console.WriteLine(album.Title);
```

`StreamQueryAsync<T>` streams elements. It is separate from `QueryAsync<IAsyncEnumerable<T>>`.

See [streaming](streaming.md) for lifetime and early exit behavior.

## Cancel an operation

```csharp
using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));

List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(cnn, new { artistId = 7 }, ct: timeout.Token);
```

Pass cancellation through `ct`. See [transactions, timeouts, and cancellation](execution-context.md) for cleanup and connection ownership.

## Read several result sets

```csharp
using MultiReader results = await GetArtistWithAlbums.ExecuteMultiReaderAsync(cnn, new { artistId = 7 }, ct: cancellationToken);

ArtistWithAlbums artist = await results.QueryAsync<ArtistWithAlbums>(ct: cancellationToken);
artist.Albums = await results.QueryAsync<List<Album>>(ct: cancellationToken);
```

See [multiple result sets](multiple-results.md) for the async forms that read more than one result set.
