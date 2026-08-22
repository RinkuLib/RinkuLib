# Execute and query SQL

Use `Query<T>` when rows should be mapped.

```csharp
public record Album(int Id, string Title) : IDbReadable;

static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
```

Use [result shapes](result-shapes.md) to choose how many results are accepted and whether they are buffered.

## Execute without mapped rows

```csharp
static readonly QueryCommand RenameAlbum = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId");

int affected = RenameAlbum.Execute(cnn, new { albumId = 1, title = "New title" });
```

`Execute` returns the affected row count reported by the provider.

```csharp
int affected = RenameAlbum.Execute(cnn, new { albumId = 999, title = "Missing" });

if (affected == 0)
    Console.WriteLine("Album not found");
```

## Execute and return one value

```csharp
static readonly QueryCommand AddAlbum = new("INSERT INTO albums (Title, ArtistId) VALUES (@title, @artistId) RETURNING AlbumId");

int albumId = AddAlbum.ExecuteScalar<int>(cnn, new { title = "Blue", artistId = 7 });
```

`ExecuteScalar<T>` reads the first value returned by `DbCommand.ExecuteScalar` and converts it to `T`.

SQL Server can use its normal output syntax.

```csharp
static readonly QueryCommand AddAlbum = new("INSERT INTO albums (Title, ArtistId) OUTPUT INSERTED.AlbumId VALUES (@title, @artistId)");

int albumId = AddAlbum.ExecuteScalar<int>(cnn, new { title = "Blue", artistId = 7 });
```

## Query a scalar result

```csharp
static readonly QueryCommand CountAlbums = new("SELECT COUNT(*) FROM albums WHERE ArtistId = @artistId");

int count = CountAlbums.Query<int>(cnn, new { artistId = 7 });
```

A scalar `SELECT` can use `Query<T>`. It then follows the normal result parser and null rules.

## Run asynchronously

```csharp
int affected = await RenameAlbum.ExecuteAsync(cnn, new { albumId = 1, title = "New title" }, ct: cancellationToken);
int albumId = await AddAlbum.ExecuteScalarAsync<int>(cnn, new { title = "Blue", artistId = 7 }, ct: cancellationToken);
```

See [async execution](async.md) for the matching async operations.

## Use a transaction or timeout

```csharp
using DbTransaction transaction = cnn.BeginTransaction();

int affected = RenameAlbum.Execute(cnn, new { albumId = 1, title = "New title" }, transaction: transaction, timeout: 30);

transaction.Commit();
```

See [transactions, timeouts, and cancellation](execution-context.md).

## Execute from a SQL string

```csharp
int affected = cnn.Execute("UPDATE albums SET Title = @title WHERE AlbumId = @albumId", new { albumId = 1, title = "New title" });
```

The [SQL string shortcuts](sql-string.md) use the same command behavior through a global SQL string cache.
