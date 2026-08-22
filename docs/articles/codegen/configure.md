# Configure CodeGen

Create a Rinku Power Tools configuration from the project submenu. The configuration manager writes a `rinkupt.json` file in the project.

```text
Right click the project
Rinku Power Tools
Configure
```


The database selector defaults to `Auto detect`. Use SQL Server, PostgreSQL, or SQLite explicitly when the connection string is ambiguous or when you want the configuration to pin a provider.

A common configuration reads the connection string from `appsettings.json`.

```json
{
  "JsonFile": "appsettings.json",
  "ConnectionExtractionPath": "ConnectionStrings:Default",
  "OutputPath": "Data/Generated",
  "Namespace": "MyApp.Data"
}
```

```json
{
  "ConnectionStrings": {
    "Default": "Server=.;Database=Music;Trusted_Connection=True;TrustServerCertificate=True"
  }
}
```

The extraction path walks through JSON properties using `:` between each name.

## Test the connection

Use `Test Connection` before adding queries.

```text
Connection Source     JSON Configuration File
Relative File Path   appsettings.json
JSON Path             ConnectionStrings:Default
```

`Show Connection String` resolves the same source and displays the value that CodeGen will use.

`Test Connection` uses the selected provider, or the provider inferred from that resolved value.

## Choose the output

```text
Output Path    Data/Generated
Namespace      MyApp.Data
Internal       false
```

The output path is relative to the project. When no namespace is supplied, CodeGen derives it from the project namespace and the output path.

The internal option changes the generated command class between `public` and `internal`.

## Use more than one configuration

The default configuration is stored as `rinkupt.json`.

A named configuration uses its name in the file name.

```text
rinkupt.json
rinkupt.Reporting.json
rinkupt.Admin.json
```

A configuration name must be a valid C# identifier. Named configurations keep independent connection settings, query lists, output paths, and generated command files.

## Connection sources

The configuration manager currently exposes these sources.

| Source | Target | Extraction value |
| --- | --- | --- |
| Raw Connection String | The connection string | None |
| Environment Variable | Variable name | None |
| JSON Configuration File | Relative JSON path | JSON property path |
| XML or Config File | Relative XML path | XPath |
| .env File | Relative file path | Variable name |
| INI File | Relative file path | Key path |
| MSBuild Project File | Relative project path | Property name |
| .NET User Secrets | Relative project path | JSON property path |
| Launch Settings | Project file path | Profile and variable name |

The file based targets are resolved from the project directory.

A raw connection string is stored directly in the configuration file. The other sources resolve the value when CodeGen runs.

### Environment variable

```json
{
  "EnvironmentVariable": "MUSIC_DB"
}
```

### XML

```json
{
  "XmlFile": "App.config",
  "ConnectionExtractionPath": "//add[@name='Default']/@connectionString"
}
```

### Dot env

```json
{
  "DotEnvFile": ".env",
  "ConnectionExtractionPath": "MUSIC_DB"
}
```

### INI

```json
{
  "IniFile": "settings.ini",
  "ConnectionExtractionPath": "[Database]ConnectionString"
}
```

### MSBuild property

```json
{
  "MsBuildProject": "MyApp.csproj",
  "ConnectionExtractionPath": "CodeGenConnectionString"
}
```

### User secrets

```json
{
  "NetUserSecrets": "MyApp.csproj",
  "ConnectionExtractionPath": "ConnectionStrings:Default"
}
```

The project file must contain a `UserSecretsId`.

### Launch settings

```json
{
  "LaunchSettings": "Properties/launchSettings.json",
  "ConnectionExtractionPath": "Development:MUSIC_DB"
}
```

The first value selects the launch profile. The second value selects an environment variable from that profile.

Continue with [Add queries](queries.md).
