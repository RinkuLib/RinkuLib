# SQL files as commands

The Rinku NuGet package generates shared `QueryCommand` members from `.rnksql` files. It does not require PowerTools or a database connection during compilation.

For example, `Sql/Users/Save.rnksql` can contain any SQL template:

```sql
UPDATE demo.UserProfiles
SET DisplayName = ?@DisplayName, Email = ?@Email
WHERE Id = @Id;
```

Configure the source root in the project:

```text
<PropertyGroup>
  <RinkuSqlRoot>Sql</RinkuSqlRoot>
</PropertyGroup>
```

The generated member is `Queries.Users.Save`, in the project's `RootNamespace`. It is a `static readonly QueryCommand`, so all existing command, builder, parameter and result APIs apply:

```csharp
var save = new Queries().Users.Save.StartBuilder();
save.Use("@Id", 74);
save.Use("@DisplayName", "James");
save.Execute(connection);
```

There is no inferred relationship between a command and database tables or operations. The SQL remains authored SQL.

## Normal SQL files require opt-in

Only `.rnksql` files are discovered automatically. Include ordinary `.sql` files individually or by folder:

```text
<ItemGroup>
  <RinkuSql Include="Sql/Users/Save.sql" />
  <RinkuSql Include="Sql/Updates/**/*.sql" Pattern="\.sql$" />
</ItemGroup>
```

Overlapping inclusions process a physical file once. Other `.sql` files remain untouched and can continue to use PowerTools. The file extension does not constrain which SQL operations it can contain.

## Choose a container with an attribute

```csharp
[QueryCommands(SqlFolder, Pattern = @"\.sql$", Recursive = true)]
public static partial class UserQueries {
    private const string SqlFolder = "Sql/Users";
}
```

The attribute selects an already included file or folder and places its commands under this partial class. Subdirectories produce nested static partial classes. Paths are project-relative or absolute; the regex matches slash-separated paths relative to the selected folder. Recursion is off unless requested.

The attribute's code fix offers explicit inclusion of a file, a folder, or a recursive folder. It adds `RinkuSql` items to the project; it does not silently opt in every SQL file. The IDE reevaluates the project after that change. The code fix requires a saved project file.

Generated output is compiler-managed. Edit the SQL files or the configuration, not the generated C#.

## Shared named access

Commands register when their assembly initializes. Typed access and the existing shared dictionary refer to the same instance:

```csharp
QueryCommand command = ConnectionQueryExtensions.CommandCache["Users/Save.rnksql"];
```

Existing SQL-string execution methods can use that key too:

```csharp
connection.Execute("Users/Save.rnksql", new { Id = 74, DisplayName = "James" });
```

Keys are relative to the SQL source root, retain the extension and casing, and use `/`. No bare-filename aliases are added. Set `RinkuSqlKeyPrefix`, or item metadata `KeyPrefix`, when multiple assemblies would otherwise register the same keys. Duplicate keys across assemblies fail during initialization rather than replacing a command. Assemblies must actually be loaded; the registry does not discover unloaded assemblies.

## Inline or deployed files

SQL is embedded in generated C# by default. No SQL file is needed at runtime.

For file loading, configure `RinkuSqlContent` as `File` globally, or set `Content` on individual items:

```text
<ItemGroup>
  <RinkuSql Include="Sql/Users/Save.sql" Content="File" RuntimePath="Sql/Users/Save.sql" />
  <RinkuSql Include="../Shared/Load.sql" Root="../Shared" Class="SharedQueries" KeyPrefix="shared" Content="File" Link="Sql/Shared/Load.sql" />
</ItemGroup>
```

File mode copies SQL to build and publish output. `RuntimePath`, then `Link`, then the project-relative source path determines the deployed path. It must remain relative and inside the application directory. The command reads the file once relative to `AppContext.BaseDirectory`; there is no file watcher or separate text cache.

External source paths are supported. Give external files a `Root`; in file mode also provide a relative `Link` or `RuntimePath`.

## Configuration

| Project property | Default | Corresponding item metadata |
|---|---|---|
| `RinkuSqlRoot` | Project directory | `Root` |
| `RinkuSqlClass` | `Queries` | `Class` |
| `RinkuSqlNamespace` | `RootNamespace` | `Namespace` |
| `RinkuSqlContent` | `Inline` | `Content` |
| `RinkuSqlKeyPrefix` | Empty | `KeyPrefix` |

Item metadata also supports `Pattern`, `RuntimePath` and `Link`. Set `EnableDefaultRinkuSqlItems` to `false` to use explicit inclusion only.

Invalid identifiers, non-partial containers, member/folder conflicts, duplicate keys and invalid configuration produce `RK0200` or `RK0201` diagnostics. Names are not silently rewritten, except that C# keywords are escaped.
## Visual Studio SQL editing

PowerTools bundles a native SQL editor companion in `Editor/RinkuPowerTools.SqlEditor.vsix`. Install it with `Editor/Install.ps1`, or open the VSIX directly. SQL Server Data Tools must be installed. Restart Visual Studio and reopen existing `.rnksql` tabs; if a previous editor association persists, use Open With > SQL Server Tools.

The companion associates `.rnksql` with Visual Studio's SQL editor. Coloring, completion, connection handling and error display remain native. A small in-process bridge replaces every `?` with a space in parse-request text only. The document, undo history and saved SQL remain unchanged, and positions still match because replacement preserves length. Other SQL errors remain visible; conditional templates can still be structurally invalid until evaluated.

The bridge uses reflection into the installed language service and therefore needs compatibility checks when Visual Studio changes. It contains no SQL parser and does not disable IntelliSense. Normal `.sql` documents are unaffected. This is an IDE extension, not a NuGet build hook; installing the library package alone does not install it into Visual Studio.
