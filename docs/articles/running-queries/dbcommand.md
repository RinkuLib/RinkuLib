# Existing DbCommand

Mapping also works with a command created by the application.

```csharp
static readonly CachedTypeParser<Album> AlbumParser = new();

using DbCommand command = cnn.CreateCommand();
command.CommandText = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId";

DbParameter parameter = command.CreateParameter();
parameter.ParameterName = "@albumId";
parameter.Value = 12;
command.Parameters.Add(parameter);

Album album = AlbumParser.Query(command);
```

`CachedTypeParser<T>` remembers the parser selected from the first command schema and reuses it for later commands with that result shape.

```csharp
using DbCommand firstCommand = cnn.CreateCommand();
firstCommand.CommandText = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = 1";

using DbCommand secondCommand = cnn.CreateCommand();
secondCommand.CommandText = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = 2";

Album first = AlbumParser.Query(firstCommand);
Album second = AlbumParser.Query(secondCommand);
```

Use a separate cache for a different complete result shape.

```csharp
static readonly CachedTypeParser<List<Album>> AlbumListParser = new();

List<Album> albums = AlbumListParser.Query(command);
```

## Execute commands

Commands that do not need object mapping can execute directly.

```csharp
using DbCommand updateCommand = cnn.CreateCommand();
updateCommand.CommandText = "UPDATE albums SET Title = 'Blue' WHERE AlbumId = 1";
int affected = updateCommand.Execute(disposeCommand: false);
```

A value can be read through the normal result parser.

```csharp
using DbCommand countCommand = cnn.CreateCommand();
countCommand.CommandText = "SELECT COUNT(*) FROM albums";

using var countParser = new CachedTypeParser<int>();
int count = countParser.Query(countCommand, disposeCommand: false);
```

`ExecuteScalar<T>` reads the first value directly without creating a result parser.

```csharp
using DbCommand insertCommand = cnn.CreateCommand();
insertCommand.CommandText = "INSERT INTO albums (Title, ArtistId) VALUES ('Blue', 7) RETURNING AlbumId";
int albumId = insertCommand.ExecuteScalar<int>(disposeCommand: false);
```

## Command ownership

The command remains caller-owned by default.

```csharp
Album album = AlbumParser.Query(command, disposeCommand: false);

// command is still available here.
```

Set `disposeCommand` to `true` to transfer command disposal to the execution call.

```csharp
Album album = AlbumParser.Query(command, disposeCommand: true);

// command has been disposed.
```

Do not wrap a command in `using` after transferring ownership unless the provider explicitly permits repeated disposal.

Streams defer disposal until enumeration finishes.

```csharp
using var streamParser = new CachedTypeParser<IEnumerable<Album>>();
IEnumerable<Album> albums = streamParser.Query(command, disposeCommand: true);

foreach (Album album in albums)
    Show(album);

// command is disposed when enumeration finishes.
```

Stopping early also disposes the enumerator and the owned command when the loop scope exits through normal `foreach` cleanup.

## Connection state

Connection restoration is independent from command ownership.

```csharp
using DbConnection cnn = GetConnection(); // closed
using DbCommand command = cnn.CreateCommand();
command.CommandText = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = 1";

Album album = AlbumParser.Query(command, disposeCommand: false);

// cnn is closed again.
// command remains caller-owned.
```

```csharp
using DbConnection cnn = GetConnection();
cnn.Open();

using DbCommand command = cnn.CreateCommand();
command.CommandText = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = 1";

Album album = AlbumParser.Query(command, disposeCommand: false);

// cnn remains open.
```

A command without a connection raises `RINKU2001`.

```csharp
using DbCommand command = CreateUnboundCommand();
command.CommandText = "SELECT AlbumId AS Id, Title FROM albums";

Album album = AlbumParser.Query(command);
// RINKU2001
```

## Transactions, timeout, and parameters

The application configures these directly on the command before execution.

```csharp
using DbCommand command = cnn.CreateCommand();
command.CommandText = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId";
command.Transaction = transaction;
command.CommandTimeout = 30;

DbParameter albumId = command.CreateParameter();
albumId.ParameterName = "@albumId";
albumId.Value = 1;
command.Parameters.Add(albumId);

Album album = AlbumParser.Query(command);
```

Rinku reads the command as configured. It does not replace its transaction, timeout, SQL, or parameters.

## Output parameters

Output and return values remain on the caller-owned command after execution.

```csharp
using DbCommand command = cnn.CreateCommand();
command.CommandText = "RenumberAlbums";
command.CommandType = CommandType.StoredProcedure;

DbParameter albumId = command.CreateParameter();
albumId.ParameterName = "@albumId";
albumId.DbType = DbType.Int32;
albumId.Value = 7;
command.Parameters.Add(albumId);

DbParameter movedParameter = command.CreateParameter();
movedParameter.ParameterName = "@moved";
movedParameter.DbType = DbType.Int32;
movedParameter.Direction = ParameterDirection.Output;
command.Parameters.Add(movedParameter);

DbParameter returnParameter = command.CreateParameter();
returnParameter.DbType = DbType.Int32;
returnParameter.Direction = ParameterDirection.ReturnValue;
command.Parameters.Add(returnParameter);

command.Execute(disposeCommand: false);

int moved = command.GetOutputValue<int>("@moved");
int returnValue = command.GetReturnValue<int>();
```

For a stream, read output values only after the enumerator has been disposed.

## Async and streaming

Async queries and async streams follow the same ownership rules.

```csharp
Album album = await AlbumParser.QueryAsync(command, disposeCommand: false, ct: cancellationToken);
```

```csharp
await foreach (Album album in AlbumParser.StreamQueryAsync(command, disposeCommand: false, ct: cancellationToken)) {
    Show(album);
}
```

`DbCommand` uses the provider's async methods. An `IDbCommand` that is not a `DbCommand` uses synchronous work through the async surface.

## Invalidate or dispose a cache

Invalidate a learned parser when the command schema or mapping configuration changes.

```csharp
bool removed = AlbumParser.Invalidate();
```

A cache with a limited lifetime should be disposed so it releases its parser reference.

```csharp
using var parser = new CachedTypeParser<Album>();

Album album = parser.Query(command);
```

[Cache ownership](../customization/caches.md) covers learned schemas, global invalidation, and application-owned caches. [Custom result parsing](../customization/result-parsers.md) covers lower-level parsers.
