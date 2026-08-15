# Async execution

The asynchronous methods use the same SQL, values, result shapes, transactions, and timeouts as their synchronous forms.

```csharp
List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(cnn, new { artistId = 7 }, ct: cancellationToken);
```

The result type is still the complete requested shape. `QueryAsync<List<Album>>` returns a task whose result is a `List<Album>`.

```csharp
Album album = await GetAlbum.QueryAsync<Album>(cnn, new { albumId = 1 }, ct: cancellationToken);
Optional<Album> maybe = await GetAlbum.QueryAsync<Optional<Album>>(cnn, new { albumId = 999 }, ct: cancellationToken);
```

## Matching operations

The main execution methods have asynchronous equivalents.

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

## Stream without buffering

An async stream is started with `StreamQueryAsync<T>`. It is not requested through `QueryAsync<IAsyncEnumerable<T>>`.

```csharp
await foreach (Album album in GetAlbums.StreamQueryAsync<Album>(cnn, new { artistId = 7 }, ct: cancellationToken))
    Show(album);
```

The reader and generated command remain active until enumeration finishes or the enumerator is disposed.

```csharp
await foreach (Album album in GetAlbums.StreamQueryAsync<Album>(cnn, new { artistId = 7 }, ct: cancellationToken)) {
    Show(album);
    if (album.Id == wantedId)
        break;
}
```

Leaving `await foreach` disposes the enumerator and releases the reader.

## Cancel an operation

Pass the token through `ct`. The provider receives it for asynchronous command and reader operations.

```csharp
using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));

List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(cnn, new { artistId = 7 }, ct: timeout.Token);
```

Cancellation follows the same cleanup rules as success and failure. A connection opened by Rinku is closed again. A connection that was already open remains open.

## Read several result sets

Create the reader asynchronously, then read each result set with its async query method.

```csharp
using MultiReader results = await GetArtistWithAlbums.ExecuteMultiReaderAsync(cnn, new { artistId = 7 }, ct: cancellationToken);

ArtistWithAlbums artist = await results.QueryAsync<ArtistWithAlbums>(ct: cancellationToken);
artist.Albums = await results.QueryAsync<List<Album>>(ct: cancellationToken);
```

`StreamQueryAsync<T>` can stream one result set and advances to the next set when its enumerator is disposed.

## Read a raw reader

The generated command is returned separately and remains caller-owned.

```csharp
DbDataReader reader = await GetAlbums.ExecuteReaderAsync(cnn, out DbCommand command, new { artistId = 7 }, ct: cancellationToken);

await using (command) {
    await using (reader) {
        while (await reader.ReadAsync(cancellationToken))
            Show(reader.GetInt32(0), reader.GetString(1));
    }
}
```

## IDbConnection behavior

When an `IDbConnection` variable contains a `DbConnection`, Rinku uses the provider's async API. Other `IDbConnection` implementations use the synchronous operation as a fallback and return its result through the async surface.

[Streaming](streaming.md) covers disposal and output parameters. [Execution context](execution-context.md) covers cancellation and connection restoration. [IDbConnection support](idbconnection.md) covers the fallback path.
