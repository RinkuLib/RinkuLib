# Configuration file

## Complete example

```json
{
  "JsonFile": "appsettings.json",
  "ConnectionExtractionPath": "ConnectionStrings:Default",
  "OutputPath": "Data/Generated",
  "Namespace": "MyApp.Data",
  "IsInternal": false,
  "Queries": [
    {
      "MethodName": "GetAlbumsByArtist",
      "SQLQuery": "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId"
    },
    {
      "MethodName": "ArchiveInvoices",
      "StoredProcName": "dbo.ArchiveInvoices",
      "Parameters": [
        {
          "Name": "@cutoff",
          "Type": "datetime2",
          "IsNullable": false
        }
      ]
    }
  ]
}
```

The Visual Studio configuration manager writes the same settings.

[Configure](configure.md)

## Connection source

One connection source property identifies where the connection string comes from.

```json
{
  "RawConnectionString": "Server=.;Database=Music;Trusted_Connection=True"
}
```

```json
{
  "EnvironmentVariable": "MUSIC_DB"
}
```

```json
{
  "JsonFile": "appsettings.json",
  "ConnectionExtractionPath": "ConnectionStrings:Default"
}
```

[All connection source examples](configure.md)

## Database provider

Without `Database`, Power Tools infers the provider from the resolved connection string when it can do so without ambiguity.

```json
{
  "RawConnectionString": "Data Source=mydatabase"
}
```

`Data Source=mydatabase` can describe more than one provider, so this form needs an explicit `Database`.

```json
{
  "Database": "Sqlite",
  "RawConnectionString": "Data Source=mydatabase"
}
```

Accepted provider values include `SqlServer`, `PostgreSql`, and `Sqlite`. `Postgres` and `PostgreSQL` are accepted when reading configuration. The writer emits `PostgreSql`.

## Output path

```json
{
  "OutputPath": "Data/Generated"
}
```

The path is project relative. An empty path writes the command file at the project root.

## Namespace

```json
{
  "OutputPath": "Data/Generated",
  "Namespace": "Music.Data.Commands"
}
```

Without `Namespace`, the generated namespace is derived from the project namespace and output path.

## Accessibility

```json
{
  "IsInternal": true
}
```

```csharp
internal static class DbCommands
{
}
```

## Query sources

```json
{
  "MethodName": "GetAlbums",
  "SQLQuery": "SELECT AlbumId AS Id, Title FROM albums"
}
```

```json
{
  "MethodName": "GetAlbums",
  "SQLFile": "Sql/GetAlbums.sql"
}
```

```json
{
  "MethodName": "GetAlbums",
  "StoredProcName": "dbo.GetAlbums"
}
```

SQL Server and PostgreSQL support stored procedure entries. SQLite supports SQL queries and SQL files.

[Add queries](queries.md)

## Method name

```json
{
  "MethodName": "GetAlbumsByArtist",
  "SQLQuery": "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId"
}
```

```csharp
using DbCommand command = cnn.GetAlbumsByArtist(artistId: 7);
```

## Result set name

```json
{
  "MethodName": "GetAlbums",
  "ResultSetName": "AlbumRow",
  "SQLQuery": "SELECT AlbumId AS Id, Title FROM albums"
}
```

```csharp
public partial record AlbumRow(int Id, string Title);
```

## Parameter correction

```json
{
  "MethodName": "FindAlbums",
  "SQLQuery": "SELECT AlbumId AS Id, Title FROM albums WHERE Title = @title",
  "Parameters": [
    {
      "Name": "@title",
      "Type": "nvarchar",
      "IsNullable": true
    }
  ]
}
```

`Type` and `IsNullable` replace the discovered values when supplied. Provider type names use the provider database type vocabulary.

SQLite can leave an unresolved parameter as `object?`. A correction can provide a stronger generated type.

PostgreSQL can retain a native PostgreSQL type name in addition to common `DbType` metadata.

[Generated parameters](generated-code.md#postgresql-parameters) · [SQLite parameters](generated-code.md#sqlite-parameters)

## Named configurations

```text
rinkupt.json
rinkupt.Reporting.json
rinkupt.Admin.json
```

[Refresh](refresh.md)
