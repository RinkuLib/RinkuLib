# Parameter metadata

Rinku can learn parameter metadata from the provider after a command is prepared and executed.

```csharp
static readonly QueryCommand UpdateAlbum = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId");

UpdateAlbum.Execute(cnn, new { albumId = 12, title = "Blue" });
```

Later calls can reuse learned database type information for the same command parameter.

## Pin parameter metadata

Configure a parameter when the provider must receive a specific database type or direction before the first execution.

```csharp
UpdateAlbum.UpdateParamCache("title", TypedDbParamCache.Get(DbType.String));
```

Use the index overload when the command is positional.

```csharp
positional.UpdateParamCache(0, new PositionalDbParamInfo());
```

## Reset learned metadata

```csharp
UpdateAlbum.Parameters.Reset();
```

Reset the command parameter metadata after external schema or provider behavior changes make the learned values invalid.

## Stored procedures

```csharp
QueryCommand command = QueryCommand.FromProc("RenumberAlbums", setupConnection);
```

`FromProc` copies provider declared types, sizes, directions, and return value metadata into the reusable command.

See [stored procedures](stored-procedures.md) for output parameters and procedure command setup.

## Custom parameter behavior

Use [parameter customization](../customization/parameters.md) when a value needs conversion or custom binding. Use [parameter member rules](../customization/parameter-members.md) when parameter source members need different discovery behavior.
