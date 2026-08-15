# Execute SQL

Use `Execute` to run SQL when no result rows need to be mapped. It returns the affected-row count reported by the database provider.

```csharp
static readonly QueryCommand RenameAlbum = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId");

int affected = RenameAlbum.Execute(cnn, new { albumId = 1, title = "New title" });
```

The provider supplies the affected-row count. An update that matches no rows normally returns `0`.

```csharp
int affected = RenameAlbum.Execute(cnn, new { albumId = 999, title = "Missing" });

if (affected == 0)
    Console.WriteLine("Album not found");
```

## Return one value while executing

Use `ExecuteScalar<T>` when the command must execute and it also returns one value. It asks the provider for the first column of the first returned row and converts that value to `T`.

The SQL syntax used to return the value is chosen by the database provider.

PostgreSQL and SQLite can use `RETURNING`.

```csharp
static readonly QueryCommand AddAlbum = new("INSERT INTO albums (Title, ArtistId) VALUES (@title, @artistId) RETURNING AlbumId");

int albumId = AddAlbum.ExecuteScalar<int>(cnn, new { title = "Blue", artistId = 7 });
```

SQL Server can use `OUTPUT INSERTED`.

```csharp
static readonly QueryCommand AddAlbum = new("INSERT INTO albums (Title, ArtistId) OUTPUT INSERTED.AlbumId VALUES (@title, @artistId)");

int albumId = AddAlbum.ExecuteScalar<int>(cnn, new { title = "Blue", artistId = 7 });
```

The returned value is converted to `T`.

## ExecuteScalar or Query

`ExecuteScalar<T>` is an execution operation. It executes the command and reads its first returned value without creating a result parser.

`Query<T>` is a result-set operation. It reads through the normal parser and does not provide the same execution guarantee. For a query such as `SELECT COUNT(*)`, `Query<int>` better expresses the intent and is faster.

```csharp
static readonly QueryCommand CountAlbums = new("SELECT COUNT(*) FROM albums WHERE ArtistId = @artistId");

int count = CountAlbums.Query<int>(cnn, new { artistId = 7 });
```

The same SQL can still be sent through `ExecuteScalar<int>`.

```csharp
int count = CountAlbums.ExecuteScalar<int>(cnn, new { artistId = 7 });
```

`Query<int>` applies its [no-result behavior](result-shapes.md#first-result) and [database `NULL` rules](../mapping/nulls.md). `ExecuteScalar<int>` converts the value returned by `DbCommand.ExecuteScalar`.

## Run asynchronously

`ExecuteAsync` and `ExecuteScalarAsync<T>` are the matching asynchronous operations.

```csharp
int affected = await RenameAlbum.ExecuteAsync(cnn, new { albumId = 1, title = "New title" }, ct: cancellationToken);
int albumId = await AddAlbum.ExecuteScalarAsync<int>(cnn, new { title = "Blue", artistId = 7 }, ct: cancellationToken);
```

## Use a transaction, timeout, or cancellation token

Execution context is supplied after the values object.

```csharp
using DbTransaction transaction = cnn.BeginTransaction();

int affected = RenameAlbum.Execute(cnn, new { albumId = 1, title = "New title" }, transaction: transaction, timeout: 30);

transaction.Commit();
```

The transaction must belong to the same open connection. 

Async methods accept cancellation through `ct`.

```csharp
int affected = await RenameAlbum.ExecuteAsync(cnn, new { albumId = 1, title = "New title" }, timeout: 30, ct: cancellationToken);
```

## Execute SQL directly from a connection

The [SQL-string shortcuts](sql-string.md) expose the same operations.

```csharp
int affected = cnn.Execute("UPDATE albums SET Title = @title WHERE AlbumId = @albumId", new { albumId = 1, title = "New title" });
int albumId = cnn.ExecuteScalar<int>("INSERT INTO albums (Title, ArtistId) VALUES (@title, @artistId) RETURNING AlbumId", new { title = "Blue", artistId = 7 });
```

[Transactions, timeouts, and cancellation](execution-context.md) covers connection ownership and cleanup. [Result shapes](result-shapes.md) covers values read through `Query<T>`.
