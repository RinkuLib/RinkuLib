# IDbConnection support

Every connection-based execution method has an `IDbConnection` overload. Use it when the application or provider exposes the older ADO.NET interface.

```csharp
IDbConnection cnn = GetLegacyConnection();

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
int affected = RenameAlbum.Execute(cnn, new { albumId = 1, title = "Blue" });
```

SQL-string shortcuts work through the same interface.

```csharp
List<Album> albums = cnn.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId", new { artistId = 7 });
```

## Transactions use IDbTransaction

The transaction overload follows the connection abstraction.

```csharp
IDbConnection cnn = GetLegacyConnection();
cnn.Open();

using IDbTransaction transaction = cnn.BeginTransaction();

RenameAlbum.Execute(cnn, new { albumId = 1, title = "Blue" }, transaction: transaction);

transaction.Commit();
```

The transaction must come from the same connection passed to the execution method.

## Generated commands use IDbCommand

An overload returning the generated command exposes `IDbCommand` when the connection is typed as `IDbConnection`.

```csharp
RenameAlbum.Execute(cnn, out IDbCommand command, new { albumId = 1, title = "Blue" });

using (command) {
    int parameterCount = command.Parameters.Count;
}
```

This form is used when output values or provider command details are needed after execution.

## Async dispatch

If the runtime object is a `DbConnection`, the `IDbConnection` overload uses its real async methods.

```csharp
IDbConnection cnn = GetConnection();

List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(cnn, new { artistId = 7 }, ct: cancellationToken);
```

If the runtime object implements only `IDbConnection`, Rinku runs the synchronous operation and returns its result through the async method.

```csharp
IDbConnection cnn = GetLegacyConnection();

List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(cnn, new { artistId = 7 }, ct: cancellationToken);
// The provider work was synchronous because cnn is not a DbConnection.
```

Cancellation cannot interrupt synchronous provider work on that fallback path.

## Connection state

Rinku opens a closed connection for the operation and closes it afterward.

```csharp
IDbConnection cnn = GetLegacyConnection();

List<Album> albums = GetAlbums.Query<List<Album>>(cnn);

// cnn is closed again.
```

An initially open connection remains open.

```csharp
IDbConnection cnn = GetLegacyConnection();
cnn.Open();

List<Album> albums = GetAlbums.Query<List<Album>>(cnn);

// cnn remains open.
```

Streamed results keep a connection opened by Rinku in use until enumeration finishes or the enumerator is disposed.

[Async execution](async.md) covers the async result shapes. [Execution context](execution-context.md) covers cleanup, transactions, timeouts, and cancellation.
