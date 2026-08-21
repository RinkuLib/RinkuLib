# Transactions, timeouts, and cancellation

## Use a transaction

Create the transaction from the same open connection passed to the command.

```csharp
using DbConnection cnn = db.Open();
using DbTransaction transaction = cnn.BeginTransaction();

UpdateAlbum.Execute(cnn, new { albumId = 12, title = "Kind of Blue" }, transaction: transaction);

transaction.Commit();
```

Several operations can share the same transaction.

```csharp
AddAlbum.Execute(cnn, new { title = "Blue", artistId = 7 }, transaction: transaction);
UpdateArtist.Execute(cnn, new { artistId = 7, modifiedAt = DateTime.UtcNow }, transaction: transaction);

transaction.Commit();
```

Rinku creates the command from `cnn` and assigns the supplied transaction.

```csharp
DbCommand cmd = cnn.CreateCommand();
cmd.Transaction = transaction;
```

It does not switch to `transaction.Connection` or compare the two connections. A mismatched, completed, or disconnected transaction is rejected by the provider.

Async methods accept the same transaction.

```csharp
int affected = await UpdateAlbum.ExecuteAsync(cnn, new { albumId = 12, title = "Kind of Blue" }, transaction: transaction, ct: cancellationToken);
```

The caller still commits or rolls back the transaction.

## Set a timeout

`timeout` is passed to `DbCommand.CommandTimeout` in seconds.

```csharp
List<Album> albums = GetAlbums.Query<List<Album>>(cnn, timeout: 60);
```

When `timeout` is omitted, `CommandTimeout` is not set and the provider default remains in effect.

```csharp
List<Album> albums = GetAlbums.Query<List<Album>>(cnn);
// No CommandTimeout value is assigned by Rinku.
```

An explicit zero is still assigned to the command.

```csharp
List<Album> albums = GetAlbums.Query<List<Album>>(cnn, timeout: 0);
// cmd.CommandTimeout = 0
```

The provider decides what zero means.

Timeouts apply to every generated command form, including readers and multi-result readers.

```csharp
using MultiReader results = GetDashboard.ExecuteMultiReader(cnn, new { artistId = 7 }, timeout: 60);
```

## Cancel an asynchronous operation

Pass the cancellation token to the async method.

```csharp
List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(cnn, ct: cancellationToken);
```

```csharp
await foreach (Album album in GetAlbums.StreamQueryAsync<Album>(cnn, ct: cancellationToken)) {
    Console.WriteLine(album.Title);
}
```

The token is passed to connection opening, command execution, reader operations, and async disposal where the provider exposes those operations.

Synchronous methods do not accept a cancellation token.

Cancellation follows the normal cleanup rules.

```csharp
using DbConnection cnn = new SqlConnection(connectionString); // closed

await GetAlbums.QueryAsync<List<Album>>(cnn, ct: cancellationToken);

// After completion, cancellation, or failure, a connection opened by Rinku is closed.
```

```csharp
using DbConnection cnn = new SqlConnection(connectionString);
cnn.Open();

await GetAlbums.QueryAsync<List<Album>>(cnn, ct: cancellationToken);

// An initially open connection remains open.
```

## Streaming keeps the context alive

A streamed result keeps its command, reader, transaction, and any connection opened by Rinku until enumeration ends.

```csharp
IEnumerable<Album> albums = GetAlbums.Query<IEnumerable<Album>>(cnn, new { artistId = 7 }, transaction: transaction, timeout: 60);

using (IEnumerator<Album> iterator = albums.GetEnumerator()) {
    while (iterator.MoveNext())
        Console.WriteLine(iterator.Current.Title);
}
```

Disposing the enumerator releases the reader immediately, including after early termination.

## Failures restore connection state

Cleanup follows the same rules after a provider error, mapping error, or cancellation.

```csharp
using DbConnection cnn = new SqlConnection(connectionString);

try {
    List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(cnn, ct: cancellationToken);
}
finally {
    // cnn is closed if Rinku opened it for the failed operation.
}
```

Rinku does not close a connection that was already open when the operation began.

## Existing commands

For a caller-created `DbCommand`, configure the transaction and timeout directly.

```csharp
using DbCommand command = cnn.CreateCommand();
command.CommandText = "UPDATE albums SET Title = @title WHERE AlbumId = @albumId";
command.Transaction = transaction;
command.CommandTimeout = 30;

int affected = command.Execute(disposeCommand: false);
```

[Streaming](streaming.md) covers deferred command ownership and output values. [Existing DbCommand](dbcommand.md) covers caller-configured commands.
