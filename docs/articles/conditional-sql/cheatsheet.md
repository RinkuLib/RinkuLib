# Marker cheat sheet

*Every marker on one page.*

| Marker | Meaning | Example |
| --- | --- | --- |
| `@Var` | Standard parameter (static text) | `WHERE TrackId = @ID` |
| `?@Var` | Optional variable, prunes its footprint when unused | `AND Name = ?@Name` |
| `/*Key*/` | Conditional segment under a custom key (no value needed) | `/*ShowPrice*/ UnitPrice` |
| `/*@Var*/` | Conditional segment tied to a variable | `/*@AlbumId*/AlbumId = ...` |
| `???` | Forced boundary conditionals cannot cross (emits nothing) | `SELECT DISTINCT ??? /*X*/TrackId, Name` |
| `&AND` / `&OR` / `&,` | Group connector, bind conditions as one unit | `?@A &AND ?@B` |
| `\|` `&` `!` (in a marker) | OR / AND / NOT between keys, read left to right | `/*A\|B&C*/`, `/*!All*/` |
| `?SELECT` | Dynamic projection, each column becomes a conditional | `?SELECT AlbumId AS Id, Title FROM ...` |
| `!col` (in `?SELECT`) | Always-used column, never pruned | `?SELECT !AlbumId AS Id, Title FROM ...` |
| `col&,` (in `?SELECT`) | Join columns under the last column's key | `?SELECT AlbumId AS Id&, Title` |
| `@Var_N` | Numeric injection | `ORDER BY @Index_N` |
| `@Var_S` | Quoted string literal | `Name = @Name_S` |
| `@Var_R` | Raw SQL (unescaped, injection risk) | `FROM @Table_R` |
| `@Var_X` | Spread a collection into parameters | `IN (@GenreIds_X)` |
| `/*~ ... */` | Keep a literal comment in the output | `/*~hint*/SELECT ...` |

Detail pages: [optional variables](optional-variables.md), [conditional markers](conditional-markers.md), [operators & grouping](operators-and-grouping.md), [dynamic projection](dynamic-projection.md), [handlers](handlers.md).
