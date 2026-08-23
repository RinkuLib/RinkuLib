# Parameter metadata

## Pin database metadata

```csharp
static readonly QueryCommand UpdateAlbum = CreateUpdateAlbum();

static QueryCommand CreateUpdateAlbum()
{
    QueryCommand command = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId");
    command.UpdateParamCache("@title", TypedDbParamCache.Get(DbType.String));
    return command;
}

UpdateAlbum.Execute(cnn, new { albumId = 12, title = "Kind of Blue" });
```

## Positional parameter metadata

```csharp
static readonly QueryCommand FindUser = CreateFindUser();

static QueryCommand CreateFindUser()
{
    QueryCommand command = new("SELECT UserId, Name FROM users WHERE UserId = ? AND Status = ?", ["userId", "status"], CommandType.Text);
    command.UpdateParamCache(0, new PositionalDbParamInfo());
    command.UpdateParamCache(1, new PositionalDbParamInfo());
    return command;
}
```

## Provider learned metadata

```csharp
UpdateAlbum.Execute(cnn, new { albumId = 12, title = "Blue" });
UpdateAlbum.Execute(cnn, new { albumId = 13, title = "Green" });
// The second execution can reuse parameter metadata retained by UpdateAlbum.
```

## Reset learned metadata

```csharp
UpdateAlbum.Parameters.Reset();
```

## Stored procedure metadata

```csharp
QueryCommand updateAlbum = QueryCommand.FromProc("UpdateAlbum", cnn);
```

`FromProc` copies the procedure parameter metadata exposed by the provider.

[Stored procedures](stored-procedures.md) · [Custom parameter binding](../customization/parameters.md)
