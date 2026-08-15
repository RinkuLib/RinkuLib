# Code generation

RinkuPowerTools is an early Visual Studio extension for SQL Server. It inspects configured queries and generates strongly typed ADO.NET command methods and result records that can be checked into the application.

## Use a generated command

```csharp
static readonly CachedTypeParser<List<GetAlbumsByArtistResult>> AlbumParser = new();

using SqlConnection cnn = new(connectionString);

List<GetAlbumsByArtistResult> albums = AlbumParser.Query(cnn.GetAlbumsByArtist(artistId: 7));
```

The generated method creates a `DbCommand` without depending on Rinku mapping. `CachedTypeParser<T>` maps the returned rows like any other [existing DbCommand](../running-queries/dbcommand.md).

## Configure, generate, and use

The Visual Studio managers write a `rinkupt.json` file beside the project.

```text
Rinku Power Tools -> configure connection and queries
Refresh           -> regenerate the selected item
Refresh all       -> regenerate every item
Update            -> refresh the generated file
```

Commit the configuration and generated C# with the application.

## Generated command

The following configured query produces one command method and one result record.

```sql
SELECT AlbumId AS Id, Title, ReleaseYear FROM albums WHERE ArtistId = @artistId
```

```csharp
namespace MyApp.Data;

public static class DbCommands {
    /// <Command cref="GetAlbumsByArtistResult" />
    public static DbCommand GetAlbumsByArtist(this DbConnection connection, int artistId) {
        DbCommand command = connection.CreateCommand();
        command.CommandText = @"SELECT AlbumId AS Id, Title, ReleaseYear FROM albums WHERE ArtistId = @artistId";
        command.CommandType = CommandType.Text;

        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "@artistId";
        parameter.DbType = DbType.Int32;
        parameter.Value = artistId;
        command.Parameters.Add(parameter);
        return command;
    }
}

/// <Schema LastUpdated="2026-06-28T14:05" />
public partial record GetAlbumsByArtistResult(int Id, string Title, int? ReleaseYear);
```

Generated records mirror the inspected columns, so normal name, type, and null mapping applies directly.

## Columns that are not C# identifiers

The generator keeps the database name through `[TrueName]`.

```sql
SELECT AlbumId AS Id, Title AS [Album Title] FROM albums
```

```csharp
public partial record GetAlbumsResult(int Id, [TrueName("Album Title")] string Album_Title);
```

## Result output

The inspected result decides which generated type accompanies the command.

```text
no result columns    -> command method only
one simple column   -> scalar result type
several columns     -> {MethodName}Result record
ResultSetName set   -> record uses the configured name
```

Regeneration rewrites only result records whose inspected columns changed. A query that cannot be inspected emits an `#error` block for that item while other configured queries continue to generate.

The `<Schema>` timestamp is consumed by the [schema analyzers](analyzers.md).
