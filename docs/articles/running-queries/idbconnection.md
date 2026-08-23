# IDbConnection

```csharp
public record Album(int Id, string Title);

static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");
static readonly QueryCommand UpdateAlbum = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId");
```

## Query

```csharp
IDbConnection connection = cnn;
List<Album> albums = GetAlbums.Query<List<Album>>(connection, new { artistId = 7 });
```

## SQL string access

```csharp
IDbConnection connection = cnn;
List<Album> albums = connection.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId", new { artistId = 7 });
```

## Transaction

```csharp
IDbConnection connection = cnn;
connection.Open();

using IDbTransaction transaction = connection.BeginTransaction();
UpdateAlbum.Execute(connection, new { albumId = 12, title = "Kind of Blue" }, transaction: transaction);
transaction.Commit();
```

## Raw command

```csharp
IDataReader reader = GetAlbums.ExecuteReader(connection, out IDbCommand command, new { artistId = 7 });

using (command)
using (reader)
{
    while (reader.Read())
        Console.WriteLine(reader.GetString(1));
}
```

## Async runtime behavior

```csharp
IDbConnection connection = sqlConnection;
List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(connection, new { artistId = 7 }, ct: cancellationToken);
```

A runtime `DbConnection` uses the provider async path. A runtime value that only implements `IDbConnection` uses the synchronous interface fallback.

[Async execution](async.md)
