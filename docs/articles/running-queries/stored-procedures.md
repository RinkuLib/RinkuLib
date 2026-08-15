# Stored procedures and output values

## Declare the parameter names

A procedure name and its parameters can be declared without inspecting the database.

```csharp
static readonly QueryCommand GetAlbumsForArtist = new("GetAlbumsForArtist", ["artistId"]);

List<Album> albums = GetAlbumsForArtist.Query<List<Album>>(cnn, new { artistId = 7 });
```

The constructor uses `CommandType.StoredProcedure` by default.

```text
CommandText: GetAlbumsForArtist
CommandType: StoredProcedure
Parameters: @artistId = 7
```

The same constructor can describe text when its parameter names cannot be found from SQL.

```csharp
static readonly QueryCommand UpdateAlbum = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId", ["title", "albumId"], CommandType.Text);

int affected = UpdateAlbum.Execute(cnn, new { albumId = 12, title = "Kind of Blue" });
```

```sql
UPDATE albums SET Title = @title WHERE AlbumId = @albumId
```

## Discover the parameter metadata

`FromProc` reads the procedure declaration once during application setup.

```csharp
static readonly QueryCommand RenumberAlbums = CreateRenumberAlbums();

static QueryCommand CreateRenumberAlbums() {
    using DbConnection cnn = GetConnection(); // closed
    QueryCommand command = QueryCommand.FromProc("RenumberAlbums", cnn);
    return command; // cnn is closed again when the method exits.
}
```

Discovery copies the procedure name, command type, parameter names, and parameter metadata into the returned `QueryCommand`.

```text
Copied: names, types, sizes, directions, return-value metadata
Not retained: connection, transaction, temporary command, provider parameters
```

An initially open setup connection remains open.

```csharp
using DbConnection cnn = GetConnection();
cnn.Open();

QueryCommand command = QueryCommand.FromProc("RenumberAlbums", cnn);

// cnn remains open.
```

The temporary provider command is disposed. When discovery fails, a connection opened by Rinku is still closed before the exception leaves `FromProc`.

## Read an output parameter

Supply a placeholder so the named output parameter is included in the execution command.

```csharp
RenumberAlbums.Execute(cnn, out DbCommand command, new { albumId = 12, moved = 0 });

using (command) {
    int moved = command.GetOutputValue<int>("@moved");
}
```

`FromProc` already copied the parameter's output direction and database metadata.

## Read the return value

The discovered return-value parameter is added automatically and does not need a placeholder.

```csharp
RenumberAlbums.Execute(cnn, out DbCommand command, new { albumId = 12, moved = 0 });

using (command) {
    int moved = command.GetOutputValue<int>("@moved");
    int returnValue = command.GetReturnValue<int>();
}
```

## When discovery is unavailable

`FromProc` fails when the provider cannot derive procedure parameters. It does not fall back to guessed metadata.

```csharp
static readonly QueryCommand GetAlbumsForArtist = new("GetAlbumsForArtist", ["artistId"]);
```

The explicit-name form remains available. Direction, size, precision, and scale can be supplied through [parameter metadata](parameter-metadata.md) when the procedure needs them.

## Async

```csharp
List<Album> albums = await GetAlbumsForArtist.QueryAsync<List<Album>>(cnn, new { artistId = 7 }, ct: cancellationToken);
```

Output values are available after the reader is disposed. For a streamed result, that includes [stopping enumeration early](streaming.md#output-parameters).
