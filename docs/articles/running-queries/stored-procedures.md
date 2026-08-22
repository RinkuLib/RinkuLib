# Stored procedures and output values

Declare the procedure name and parameter names when the application already knows them.

```csharp
static readonly QueryCommand GetAlbumsForArtist = new("GetAlbumsForArtist", ["artistId"]);

List<Album> albums = GetAlbumsForArtist.Query<List<Album>>(cnn, new { artistId = 7 });
```

This constructor uses `CommandType.StoredProcedure` by default.

## Discover procedure metadata

```csharp
QueryCommand renumberAlbums = QueryCommand.FromProc("RenumberAlbums", setupConnection);
```

`FromProc` reads the provider procedure metadata during setup and returns a reusable `QueryCommand`.

The returned command does not keep the setup connection or temporary provider command.

## Read an output parameter

```csharp
renumberAlbums.Execute(cnn, out DbCommand command, new { albumId = 12 });

using (command)
{
    int moved = command.GetOutputValue<int>("@moved");
}
```

The generated command is returned so provider output values can be read after execution.

## InputOutput parameters

When a discovered `InputOutput` parameter needs an incoming value, create the command with `inputOutputHasDefault: false`.

```csharp
QueryCommand command = QueryCommand.FromProc("RenumberAlbums", setupConnection, inputOutputHasDefault: false);
```

Supply the incoming value through the normal parameter object or builder.

## Return values and provider metadata

`FromProc` copies names, database types, sizes, directions, and return value metadata from the provider declaration.

See [parameter metadata](parameter-metadata.md) when metadata is configured without procedure discovery.
