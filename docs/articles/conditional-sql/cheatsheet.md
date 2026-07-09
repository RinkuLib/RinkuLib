# Cheat sheet

| Marker | Meaning | Example |
| --- | --- | --- |
| `@Var` | Plain parameter, static text | `WHERE TrackId = @Id` |
| `?@Var` | Optional variable, prunes its footprint when unused | `AND Name = ?@Name` |
| `/*Key*/` | Conditional footprint under a custom key | `/*ShowPrice*/UnitPrice` |
| `/*@Var*/` | Conditional footprint tied to a variable | `/*@AlbumId*/AlbumId = ...` |
| `|` `&` in a marker | Combine keys, or / and, left to right | `/*A|B&C*/` |
| `!` in a marker | Negate a key, keep the footprint when it is absent | `/*!All*/` |
| `&AND` / `&OR` / `&,` | Weld footprints into one | `?@A &AND ?@B` |
| `???` | Boundary a footprint cannot cross, emits nothing | `SELECT DISTINCT ??? /*X*/Id, Name` |
| `?SELECT` | Dynamic projection, each column becomes a condition | `?SELECT AlbumId AS Id, Title FROM ...` |
| `col!` in `?SELECT` | Always-kept column | `?SELECT AlbumId AS Id!, Title` |
| `col&,` in `?SELECT` | Join columns under the last one's key | `?SELECT AlbumId AS Id&, Title` |
| `/*Key*/col` in `?SELECT` | Extra key required on top of the column's own | `?SELECT Id, /*Admin*/Email` |
| `@Var_N` | Number written into the SQL | `OFFSET @Skip_N ROWS` |
| `@Var_S` | Quoted string literal | `Name = @Name_S` |
| `@Var_R` | Raw SQL, unescaped, injection risk | `FROM @Table_R` |
| `@Var_X` | Collection spread into parameters | `IN (@GenreIds_X)` |
| `/*~ ... */` | Literal comment, kept in the output | `/*~ hint */SELECT ...` |

Details: [optional variables](optional-variables.md), [conditional markers](conditional-markers.md), [dynamic projection](dynamic-projection.md), [handlers](handlers.md).
