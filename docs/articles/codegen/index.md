# Code generation

![Rinku Power Tools project menu](../../images/codegen/project-menu.png)

![Rinku Power Tools configuration manager](../../images/codegen/configuration-manager.png)

![Rinku Power Tools query manager](../../images/codegen/query-manager.png)

A configured database command generates a typed `DbCommand` method and result metadata.

```csharp
static readonly CachedTypeParser<List<GetAlbumsByArtistResult>> AlbumParser = new();

List<GetAlbumsByArtistResult> albums = AlbumParser.Query(cnn.GetAlbumsByArtist(artistId: 7));
```

The generated method returns a `DbCommand`.

```csharp
using DbCommand command = cnn.GetAlbumsByArtist(artistId: 7);
using DbDataReader reader = command.ExecuteReader();
```

[Configure](configure.md) · [Add queries](queries.md) · [Generated commands](generated-code.md) · [Refresh](refresh.md)

## SQL file command

```json
{
  "MethodName": "GetAlbums",
  "SQLFile": "Sql/GetAlbums.sql"
}
```

```csharp
using DbCommand command = cnn.GetAlbums();
```

The generated command reads the current SQL through `RinkuPowerTools.GetSqlFile`.

[SQL file generation and runtime replacement](generated-code.md#sql-files)

## Analyzers

```csharp
/// <Schema LastUpdated="2026-08-21T14:00Z" />
public partial record GetAlbumResult(int Id, string Title);

/// <BasedOn cref="GetAlbumResult" LastUpdated="2026-08-21T14:00Z" />
public record AlbumDto(int Id, string Title);
```

The analyzers ship in the `Rinku` package. Power Tools is not required for them.

[Analyzers and code fixes](analyzers.md)
