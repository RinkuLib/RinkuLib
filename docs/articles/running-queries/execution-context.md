# Transactions timeouts and cancellation

Create a transaction from the same connection used for the command.

```csharp
using DbTransaction transaction = cnn.BeginTransaction();

UpdateAlbum.Execute(cnn, new { albumId = 12, title = "Kind of Blue" }, transaction: transaction);

transaction.Commit();
```

Several Rinku operations can share the same transaction.

```csharp
AddAlbum.Execute(cnn, new { title = "Blue", artistId = 7 }, transaction: transaction);
UpdateArtist.Execute(cnn, new { artistId = 7, modifiedAt = DateTime.UtcNow }, transaction: transaction);

transaction.Commit();
```

The caller still commits or rolls back the transaction.

## Set a timeout

```csharp
List<Album> albums = GetAlbums.Query<List<Album>>(cnn, timeout: 60);
```

`timeout` is assigned to `DbCommand.CommandTimeout` in seconds.

Omitting it leaves the provider default unchanged.

```csharp
List<Album> albums = GetAlbums.Query<List<Album>>(cnn);
```

An explicit zero is still assigned.

```csharp
List<Album> albums = GetAlbums.Query<List<Album>>(cnn, timeout: 0);
```

The provider decides what zero means.

## Cancel async work

```csharp
List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(cnn, ct: cancellationToken);
```

```csharp
await foreach (Album album in GetAlbums.StreamQueryAsync<Album>(cnn, ct: cancellationToken))
    Console.WriteLine(album.Title);
```

The token is passed to async provider operations where the provider exposes them.

## Connection ownership

A closed connection is opened for the operation and closed again after Rinku owned work finishes.

```csharp
using DbConnection cnn = new SqlConnection(connectionString);

List<Album> albums = GetAlbums.Query<List<Album>>(cnn);
// cnn is closed again.
```

An already open connection remains open.

```csharp
using DbConnection cnn = new SqlConnection(connectionString);
cnn.Open();

List<Album> albums = GetAlbums.Query<List<Album>>(cnn);
// cnn is still open.
```

Streaming keeps the operation active until enumeration finishes or the enumerator is disposed. See [streaming](streaming.md).

See [IDbConnection support](idbconnection.md) when the connection is typed through the older ADO.NET interface.
