# IDbConnection support

The connection based execution methods also accept `IDbConnection`.

```csharp
static List<Album> LoadAlbums(IDbConnection cnn)
{
    List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
    RenameAlbum.Execute(cnn, new { albumId = 1, title = "Blue" });
    return albums;
}
```

SQL string shortcuts use the same interface.

```csharp
static List<Album> LoadAlbums(IDbConnection cnn)
    => cnn.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId", new { artistId = 7 });
```

## Transactions

```csharp
cnn.Open();
using IDbTransaction transaction = cnn.BeginTransaction();

RenameAlbum.Execute(cnn, new { albumId = 1, title = "Blue" }, transaction: transaction);

transaction.Commit();
```

Use a transaction created by the same connection.

## Get the generated IDbCommand

```csharp
RenameAlbum.Execute(cnn, out IDbCommand command, new { albumId = 1, title = "Blue" });

using (command)
{
    Console.WriteLine(command.Parameters.Count);
}
```

This form is useful for output values and provider command details.

## Async behavior

If the runtime connection is a `DbConnection`, the `IDbConnection` overload can use its real async methods.

```csharp
IDbConnection cnn = new SqlConnection(connectionString);

List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(cnn, new { artistId = 7 }, ct: cancellationToken);
```

If the runtime object only implements `IDbConnection`, Rinku falls back to the synchronous provider operation and returns through the async method. Cancellation cannot interrupt that synchronous provider work.

See [execution context](execution-context.md) for connection opening and closing rules.
