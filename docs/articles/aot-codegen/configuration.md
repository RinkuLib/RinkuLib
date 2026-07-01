# Configuration

*The `rinkupt.json` file.*

The [Visual Studio managers](power-tools.md) read and write a JSON config in your project directory, `rinkupt.json` (or `rinkupt.{name}.json` for a named config, so one project can hold several). You can edit it by hand and commit it.

## Shape

```json
{
  "JsonFile": "appsettings.json",
  "ConnectionExtractionPath": "ConnectionStrings:Default",
  "OutputPath": "Data/Generated",
  "Namespace": "MyApp.Data",
  "IsInternal": false,
  "Queries": [
    {
      "MethodName": "GetTracksByAlbum",
      "SQLQuery": "SELECT TrackId AS Id, Name, UnitPrice FROM tracks WHERE AlbumId >= @albumId"
    },
    {
      "MethodName": "ArchiveInvoices",
      "StoredProcName": "dbo.ArchiveInvoices",
      "ResultSetName": "ArchivedInvoice",
      "Parameters": [
        { "Name": "@cutoff", "Type": "datetime2", "IsNullable": false }
      ]
    }
  ]
}
```

Two things about the shape.

- **The connection source is the property name**, and its value is the target. `"JsonFile": "appsettings.json"` both selects the JSON-file source and points at the file. `ConnectionExtractionPath` then locates the string inside it (here, `ConnectionStrings:Default`).
- **Each query's source is its key**, `SQLQuery` (inline SQL), `StoredProcName` (a stored procedure), or `SQLFile` (a `.sql` file path).

## Query settings

| Field | Meaning |
| --- | --- |
| `MethodName` | The generated method's name. |
| `SQLQuery` / `StoredProcName` / `SQLFile` | The query's source (one of these). |
| `ResultSetName` | Optional name for the generated result record. |
| `Parameters` | Optional `[ { Name, Type, IsNullable } ]` overrides, to pin a parameter the database can't fully infer. |

## Output settings

| Field | Meaning |
| --- | --- |
| `OutputPath` | Where generated files go (relative to the project). |
| `Namespace` | Base namespace for the generated code. |
| `IsInternal` | Generate `internal` types instead of `public`. |

## Connection sources

The most common ways to point at your database.

| Source (JSON key) | Value | Extraction path |
| --- | --- | --- |
| `RawConnectionString` | the connection string itself | n/a |
| `EnvironmentVariable` | the variable name | n/a |
| `JsonFile` | path to a `.json` file | `:`-separated property path (for example `ConnectionStrings:Default`) |
| `NetUserSecrets` | path to the `.csproj` | `:`-separated path into `secrets.json` |
| `LaunchSettings` | (uses `Properties/launchSettings.json`) | `ProfileName:VariableName` |

A few other keys exist (`XmlFile`, `IniFile`, `DotEnvFile`, `MsBuildProject`). `VsDataConnection` and `CloudSecret` are reserved but not yet implemented.
