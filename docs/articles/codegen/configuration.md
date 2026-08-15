# Code-generation configuration

The Visual Studio managers read and write `rinkupt.json` in the project directory. A named configuration uses `rinkupt.{name}.json`, so one project can keep several configurations.

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
      "SQLQuery": "SELECT AlbumId AS Id, Title, ReleaseYear FROM albums WHERE ArtistId = @artistId"
    },
    {
      "MethodName": "ArchiveInvoices",
      "StoredProcName": "dbo.ArchiveInvoices",
      "ResultSetName": "ArchivedInvoice",
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

The connection-source property carries both the source type and its value.

```json
{
  "JsonFile": "appsettings.json",
  "ConnectionExtractionPath": "ConnectionStrings:Default"
}
```

`ConnectionExtractionPath` locates the value inside the selected source.

## Query sources

Each query uses one source property.

Use `SQLQuery` when the statement is stored directly in the configuration.

```json
{
  "MethodName": "GetAlbums",
  "SQLQuery": "SELECT AlbumId AS Id, Title FROM albums"
}
```

Use `StoredProcName` when generation should inspect a stored procedure.

```json
{
  "MethodName": "ArchiveInvoices",
  "StoredProcName": "dbo.ArchiveInvoices"
}
```

Use `SQLFile` when the statement belongs in a separate file.

```json
{
  "MethodName": "GetAlbumReport",
  "SQLFile": "Sql/GetAlbumReport.sql"
}
```

The remaining query fields control the generated code.

| Field | Effect |
| --- | --- |
| `MethodName` | Names the generated command method. |
| `ResultSetName` | Replaces the generated result-record name. |
| `Parameters` | Overrides parameter name, database type, or nullability when inspection cannot determine them. |

## Generated output

```json
{
  "OutputPath": "Data/Generated",
  "Namespace": "MyApp.Data",
  "IsInternal": false
}
```

| Field | Effect |
| --- | --- |
| `OutputPath` | Selects the generated directory relative to the project. |
| `Namespace` | Sets the generated namespace. |
| `IsInternal` | Uses `internal` instead of `public` generated types. |

## Connection sources

`RawConnectionString` stores the connection string directly.

```json
{ "RawConnectionString": "..." }
```

`EnvironmentVariable` reads the connection string from the named variable.

```json
{ "EnvironmentVariable": "APP_DATABASE" }
```

`NetUserSecrets` reads a value from the selected project’s user secrets.

```json
{
  "NetUserSecrets": "MyApp.csproj",
  "ConnectionExtractionPath": "ConnectionStrings:Default"
}
```

`LaunchSettings` reads a value from the selected launch profile.

```json
{
  "LaunchSettings": "Properties/launchSettings.json",
  "ConnectionExtractionPath": "Development:APP_DATABASE"
}
```

`XmlFile`, `IniFile`, `DotEnvFile`, and `MsBuildProject` are also available. `VsDataConnection` and `CloudSecret` are not currently implemented.
