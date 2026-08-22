# Configuration file

The Visual Studio configuration manager writes the same settings that can be edited in `rinkupt.json`.

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

## Connection source

Exactly one connection source property identifies where the connection string comes from.

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

See [Configure CodeGen](configure.md) for all connection sources exposed by the current configuration manager.

## Database provider

The `Database` property is optional. When it is absent, PowerTools infers the provider from the resolved connection string.

```json
{
  "JsonFile": "appsettings.json",
  "ConnectionExtractionPath": "ConnectionStrings:Default"
}
```

Inference is conservative. A connection string with strong provider specific keys is detected automatically. A string such as `Data Source=mydatabase` can describe SQL Server or SQLite, so PowerTools asks for an explicit provider instead of guessing.

```json
{
  "Database": "Sqlite",
  "RawConnectionString": "Data Source=mydatabase"
}
```

Supported values are `SqlServer`, `PostgreSql`, and `Sqlite`. `Postgres` and `PostgreSQL` are also accepted when reading configuration. The writer uses `PostgreSql`.

An explicit provider always wins over inference.

## Output path

`OutputPath` is relative to the project.

```json
{
  "OutputPath": "Data/Generated"
}
```

An empty output path writes the command file at the project root.

## Namespace

Set `Namespace` when the generated namespace should not be derived from the project and output path.

```json
{
  "OutputPath": "Data/Generated",
  "Namespace": "Music.Data.Commands"
}
```

Without `Namespace`, an output path such as `Data/Generated` is appended to the project namespace.

## Accessibility

```json
{
  "IsInternal": true
}
```

This generates an internal command class.

```csharp
internal static class DbCommands
{
}
```

The default value generates a public class.

## Query source

A query uses one source property. SQL Server and PostgreSQL support stored procedure entries. SQLite supports SQL queries and SQL files.

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

A relative `SQLFile` is read from the project during generation and packaged at the same relative path for runtime use. An absolute `SQLFile` keeps its absolute path and is not packaged.

The configured path remains the runtime dictionary key. The generated method calls `RinkuPowerTools.GetSqlFile` and application code can access `RinkuPowerTools.SqlFiles` directly. See [Generated commands](generated-code.md) for the runtime behavior.

```json
{
  "MethodName": "GetAlbums",
  "StoredProcName": "dbo.GetAlbums"
}
```

`StoredProcName` is available for SQL Server and PostgreSQL. SQLite has no stored procedures.

Do not put more than one query source property on the same query entry.

## Method name

`MethodName` becomes the generated C# method name.

```json
{
  "MethodName": "GetAlbumsByArtist",
  "SQLQuery": "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId"
}
```

```csharp
cnn.GetAlbumsByArtist(artistId: 7);
```

Use a name that is a valid C# identifier.

## Result name

`ResultSetName` changes the generated record name when the result requires a record.

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

See [Generated commands](generated-code.md) for the cases that produce scalar results or no record.

## Parameter corrections

`Parameters` changes discovered metadata for matching parameter names.

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

`Type` and `IsNullable` are optional correction values. Leave a value absent when the discovered value should be kept.

Type names are provider specific. SQL Server accepts SQL Server declarations, PostgreSQL accepts PostgreSQL declarations including custom type names, and SQLite accepts SQLite declared types and affinity names.

SQLite cannot infer a declared type for a query parameter from the database schema. Without a correction, the generated method uses `object?` and lets `Microsoft.Data.Sqlite` infer the runtime parameter type from the supplied value. Add a parameter correction when the generated method should expose a stronger C# type.

PostgreSQL keeps the discovered PostgreSQL type name in addition to common `DbType` metadata. This preserves distinctions such as `jsonb`, arrays, timestamp variants, enums, composites, and domains in generated Npgsql commands.

See [Add queries](queries.md) for the query manager form of the same options.

## Named configurations

A project can contain several configuration files.

```text
rinkupt.json
rinkupt.Reporting.json
rinkupt.Admin.json
```

Use [Refresh generated code](refresh.md) to refresh one configuration or all project configurations.
