# Stored procedures

## Declare parameter names

```csharp
static readonly QueryCommand GetAlbumsForArtist = new("GetAlbumsForArtist", ["artistId"]);

List<Album> albums = GetAlbumsForArtist.Query<List<Album>>(cnn, new { artistId = 7 });
```

The command targets a stored procedure and carries the parameter names used by Rinku.

## Discover provider metadata

```csharp
QueryCommand renumberAlbums = QueryCommand.FromProc("RenumberAlbums", cnn);

renumberAlbums.Execute(cnn, new { albumId = 12 });
```

`FromProc` reads the procedure parameter metadata exposed by the provider. The metadata is stored on the returned `QueryCommand`.

A known parameter list avoids that discovery step.

```csharp
static readonly QueryCommand RenumberAlbums = new("RenumberAlbums", ["albumId", "moved"]);
```

[Parameter metadata](parameter-metadata.md)

## Output values

```csharp
QueryCommand renumberAlbums = QueryCommand.FromProc("RenumberAlbums", cnn);

renumberAlbums.Execute(cnn, out DbCommand command, new { albumId = 12 });

using (command)
{
    int moved = command.GetOutputValue<int>("@moved");
}
```

The output value is read from the executed command.

## Input output defaults

```csharp
QueryCommand updateAlbum = QueryCommand.FromProc("UpdateAlbum", cnn, inputOutputHasDefault: false);
// Discovered InputOutput parameters now require an incoming value.
```

With the default `true`, discovered input output parameters can be omitted when the provider metadata allows Rinku to supply their default.

## Materialize defaults on a bound command

```csharp
QueryCommand updateAlbum = QueryCommand.FromProc("UpdateAlbum", cnn);

using DbCommand command = cnn.CreateCommand();
var call = updateAlbum.StartBuilder(command);

call.UseWith(new { albumId = 12 });
call.SetDefaults();
call.Execute();
```

`SetDefaults()` fills only missing parameters whose metadata can provide a default.

[Builders](builders.md)

## Discovered parameter metadata

```csharp
QueryCommand updateAlbum = QueryCommand.FromProc("UpdateAlbum", cnn);

updateAlbum.Execute(cnn, out DbCommand command, new { albumId = 12 });

using (command)
{
    DbParameter moved = (DbParameter)command.Parameters["@moved"];
    Console.WriteLine(moved.Direction);
    Console.WriteLine(moved.Size);
}
```

`FromProc` carries provider parameter names, database types, sizes, directions, and return value metadata into the reusable command.

[Parameter metadata](parameter-metadata.md)
