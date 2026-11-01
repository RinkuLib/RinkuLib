# Rinku Power Tools CLI

The CLI is a command-line host over the shared `RinkuPowerTools` implementation. It does not duplicate schema discovery or generation logic.

## Refresh

```bash
rinku refresh
rinku refresh dev
rinku refresh --default
```

`refresh` with no selector refreshes every `rinkupt.json`, `rinkupt.<name>.json` and `rinku.<name>.json` configuration beside the selected project. If there are none, it succeeds without doing anything. Named configurations generate `<name>.rinku.cs`; the default generates `DbCommands.rinku.cs`.

`refresh dev` refreshes `rinkupt.dev.json` only. `refresh --default` refreshes `rinkupt.json` only. An explicitly selected missing configuration is an error.

For example, keep commands in `rinkupt.json` and only `ConstantsPath` plus connection settings in `rinku.ENUM.json`. Both refresh on build, producing separate command and enum files. `refresh ENUM` selects the existing named file; if both naming variants exist, select the file path explicitly.

Use `--project <path>` to select a project. Without it, the CLI walks from the current directory to the nearest directory containing one `.csproj`.

## Other commands

```bash
rinku config list
rinku config show
rinku config init
rinku config set

rinku query list
rinku query show GetAlbums
rinku query add GetAlbums --file Sql/GetAlbums.sql
rinku query set GetAlbums --sql "SELECT ..."
rinku query remove GetAlbums
rinku query param GetAlbums @artistId --type int --nullable false
rinku query param GetAlbums @artistId --default null

rinku connection test
```

`--default` sets a C# default expression for an input argument; `--default=` removes that default. No type or nullability correction is required when discovery already supplies the desired metadata. Defaults must be trailing; generation preserves argument order and reports invalid ordering.

For config/query/connection management, `--config dev` selects `rinkupt.dev.json`. A config path is also accepted there. Refresh intentionally uses its simpler all/name/default surface.

## Build integration

Reference the companion `RinkuPowerTools.Cli.Build` package to invoke the packaged CLI before `CoreCompile`. It skips design-time builds. Set `RinkuPowerToolsRefreshOnBuild=false` to disable it for a build.

The same target is callable explicitly: `dotnet build path/to/project.csproj -t:RinkuPowerToolsRefresh`. It resolves project references first, including a referenced CLI project when using a source checkout. Explicit refresh works even with `RinkuPowerToolsRefreshOnBuild=false`; that setting only disables the automatic before-compile hook.

## Constants and enums

Each connection configuration has one optional constants source. The Visual Studio connection form offers None, SQL file, raw SQL and stored procedure. The JSON equivalents are:

```json
{ "ConstantsPath": "Sql/Constants.sql" }
```

Use ConstantsSQL or ConstantsProc instead of ConstantsPath for the other source modes; exactly one nonempty string is allowed at the connection level. Omit the property to disable generation. Stored procedures are parameterless and require a provider supporting them.

The source must return exactly one result set with exactly five named columns: Name, Value, Type, Description and Parent. Column order is immaterial. Non-null cells must be strings; numeric SQL values must be cast to text.

| Column | Meaning |
| --- | --- |
| Name | C# identifier for one member/container; keywords are escaped, invalid names fail |
| Value | Text parsed as Type; SQL NULL makes a container, empty string remains a string value |
| Type | Primitive C# scalar alias or corresponding System type name; SQL NULL for containers |
| Description | Optional System.ComponentModel.Description attribute on the member/container |
| Parent | Absolute original-name path, e.g. Application.Settings; SQL NULL only for root containers |

Parent paths create missing containers, so structural rows are optional unless they need descriptions. Paths are structural ancestry, not references to globally unique leaf names; they cannot form cycles. Empty/invalid path segments, self-named children, duplicate paths, root values and value nodes used as parents fail. Input order does not affect output. Descriptions and string/char values are escaped as C# literals, never treated as code.

A container with only integral value children becomes an enum. Supported underlying types are sbyte, byte, short, ushort, int, uint, long and ulong, including negative values and aliases. The largest declared type is respected. Mixed signed/unsigned ranges widen where necessary; a group containing negative values plus unsigned values beyond long.MaxValue becomes a static class because no enum underlying type can represent them together. Other groups become static classes with static readonly fields and nested definitions. Supported scalar fields also include string, char, bool, float, double and decimal. Parsing uses invariant culture and validates type ranges.

For example, Reader / "1" / byte / NULL / Application.Roles and Administrator / "300" / long / NULL / Application.Roles generate a nested Roles : long enum. Title / "Rinku" / string / NULL / Application.Settings generates a string field in a nested static Settings class. No Application, Roles or Settings rows are required.

Generated definitions live in the same <command-class>.rinku.cs file as the commands, inheriting namespace and public/internal visibility. Normal build integration includes newly generated outputs even on the first build. Removing the source removes the definitions from that file during refresh. Invalid definitions fail before command/support outputs are replaced. Refresh/Refresh All in Visual Studio, CLI refresh and the build target use the same implementation.

CLI configuration supports --constants-path, --constants-sql, --constants-proc and --constants-none on config init/set. Obsolete nested Definitions and Enums settings fail with migration instructions; replace them with the appropriate flat constants source property.
