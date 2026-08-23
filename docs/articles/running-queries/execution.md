# Execution

## Query rows

```csharp
public record Album(int Id, string Title);

static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
```

The requested result type controls how mapped results are consumed.

[Result shapes](result-shapes.md)

## Execute SQL

```csharp
static readonly QueryCommand RenameAlbum = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId");

int affected = RenameAlbum.Execute(cnn, new { albumId = 12, title = "Kind of Blue" });
```

## Execute a scalar

```csharp
static readonly QueryCommand AddAlbum = new("INSERT INTO albums (ArtistId, Title) VALUES (@artistId, @title); SELECT CAST(SCOPE_IDENTITY() AS int);");

int albumId = AddAlbum.ExecuteScalar<int>(cnn, new { artistId = 7, title = "Blue" });
```

A scalar can also use result mapping.

```csharp
static readonly QueryCommand CountAlbums = new("SELECT COUNT(*) FROM albums WHERE ArtistId = @artistId");

int count = CountAlbums.Query<int>(cnn, new { artistId = 7 });
```

## SQL string form

```csharp
List<Album> albums = cnn.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId", new { artistId = 7 });

int affected = cnn.Execute("UPDATE albums SET Title = @title WHERE AlbumId = @albumId", new { albumId = 12, title = "Kind of Blue" });
```

The exact SQL string accesses its cached `QueryCommand`.

[SQL string access](sql-string.md)

## Async

```csharp
List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(cnn, new { artistId = 7 }, ct: cancellationToken);
int affected = await RenameAlbum.ExecuteAsync(cnn, new { albumId = 12, title = "Kind of Blue" }, ct: cancellationToken);
```

[Async execution](async.md)

## Execution context

```csharp
using DbTransaction transaction = cnn.BeginTransaction();

RenameAlbum.Execute(cnn, new { albumId = 12, title = "Kind of Blue" }, transaction: transaction, timeout: 30);
transaction.Commit();
```

[Transactions, timeouts, connections, and cancellation](execution-context.md)
