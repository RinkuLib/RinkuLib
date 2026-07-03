# Configuration

The Visual Studio managers read and write `rinkupt.json` in your project directory (`rinkupt.{name}.json` for a named config, one project can hold several). It is plain JSON, edit it by hand and commit it.

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

Two conventions in the shape:

- **The connection source is the property name.** `"JsonFile": "appsettings.json"` both selects the JSON-file source and points at the file. `ConnectionExtractionPath` locates the string inside it.
- **Each query's source is its key.** One of `SQLQuery` (inline SQL), `StoredProcName`, or `SQLFile` (a `.sql` file path).

## Query settings

| Field | Meaning |
| --- | --- |
| `MethodName` | The generated method's name. |
| `SQLQuery` / `StoredProcName` / `SQLFile` | The query's source, one of these. |
| `ResultSetName` | Optional name for the generated result record. |
| `Parameters` | Optional `[ { Name, Type, IsNullable } ]` overrides for parameters the database cannot fully infer. |

## Output settings

| Field | Meaning |
| --- | --- |
| `OutputPath` | Where generated files go, relative to the project. |
| `Namespace` | Base namespace for the generated code. |
| `IsInternal` | Generate `internal` types instead of `public`. |

## Connection sources

| Source key | Value | Extraction path |
| --- | --- | --- |
| `RawConnectionString` | the connection string itself | n/a |
| `EnvironmentVariable` | the variable name | n/a |
| `JsonFile` | path to a `.json` file | `:`-separated property path |
| `NetUserSecrets` | path to the `.csproj` | `:`-separated path into `secrets.json` |
| `LaunchSettings` | uses `Properties/launchSettings.json` | `ProfileName:VariableName` |

`XmlFile`, `IniFile`, `DotEnvFile`, and `MsBuildProject` also exist. `VsDataConnection` and `CloudSecret` are reserved, not yet implemented.
