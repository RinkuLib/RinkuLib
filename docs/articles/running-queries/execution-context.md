# Execution context

```csharp
static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");
static readonly QueryCommand UpdateAlbum = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId");
static readonly QueryCommand InsertAudit = new("INSERT INTO album_audit (AlbumId) VALUES (@albumId)");
```

## Transaction

```csharp
using DbTransaction transaction = cnn.BeginTransaction();

UpdateAlbum.Execute(cnn, new { albumId = 12, title = "Kind of Blue" }, transaction: transaction);
InsertAudit.Execute(cnn, new { albumId = 12 }, transaction: transaction);

transaction.Commit();
```

The caller owns the transaction.

## Timeout

```csharp
List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 7 }, timeout: 30);
// DbCommand.CommandTimeout receives 30.
```

Omitting the timeout keeps the provider default. Passing zero assigns zero.

## Cancellation

```csharp
List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(cnn, new { artistId = 7 }, ct: cancellationToken);
```

The token is passed to provider async operations.

[Async execution](async.md)

## Closed connection

```csharp
await using SqlConnection cnn = new(connectionString);

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
// Rinku opens the closed connection for the operation and closes it afterward.
```

## Already open connection

```csharp
await using SqlConnection cnn = new(connectionString);
await cnn.OpenAsync(cancellationToken);

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
// cnn stays open because it was already open.
```

A streamed operation keeps its reader active until enumeration ends.

[Streaming](streaming.md)
