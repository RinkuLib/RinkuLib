# Code generation

Rinku Power Tools reads configured database commands and generates typed `DbCommand` methods and result records.

```csharp
static readonly CachedTypeParser<List<GetAlbumsByArtistResult>> AlbumParser = new();

using SqlConnection cnn = new(connectionString);

List<GetAlbumsByArtistResult> albums = AlbumParser.Query(cnn.GetAlbumsByArtist(artistId: 7));
```

The generated method creates a normal `DbCommand`. Rinku can read it through a cached parser, or application code can execute it directly.

A query stored in `SQLFile` stays a file reference in generated C#. CodeGen reads the file during generation for schema discovery, while runtime command creation gets the current SQL through `RinkuPowerTools.GetSqlFile`. The shared `SqlFiles` dictionary remains directly accessible when the application wants to replace or reload that SQL.

```csharp
using DbCommand command = cnn.GetAlbumsByArtist(artistId: 7);
using DbDataReader reader = command.ExecuteReader();
```

Schema discovery supports SQL Server, PostgreSQL, and SQLite. PowerTools normally infers the provider from the resolved connection string. A configuration can set the database explicitly when a connection string is ambiguous.

## Workflow

```text
Create a configuration
Add one or more queries
Generate the command file
Use the generated methods
Refresh after database changes
```

Start with [Configure CodeGen](configure.md), then add commands in [Add queries](queries.md).

See [Generated commands](generated-code.md) for the generated method and result shapes.

The `Rinku` package also ships compile time analyzers and code fixes. [Analyzers and code fixes](analyzers.md) covers generated schema tracking, constructor contracts, and method invocation generation. PowerTools is not required for those analyzers.

See [Refresh generated code](refresh.md) for individual refresh, project refresh, and generation failures.

See [Configuration file](configuration.md) when you want to edit or review `rinkupt.json` directly.
