# Conditional SQL cheat sheet

## Values and handlers

| Template | Supplied value | Generated SQL |
| --- | --- | --- |
| `WHERE Id = @id` | `id = 7` | `WHERE Id = @id` |
| `WHERE Id = ?@id` | no `id` | removed |
| `OFFSET @skip_N ROWS` | `skip = 10` | `OFFSET 10 ROWS` |
| `Name = @name_S` | `name = "O'Brien"` | `Name = 'O''Brien'` |
| `ORDER BY @order_R` | `order = "Title DESC"` | `ORDER BY Title DESC` |
| `IN (@ids_X)` | `ids = [2, 5]` | `IN (@ids_1, @ids_2)` |
| `IN (?@ids_X)` | `ids = []` | condition removed |

## Conditional keys

| Template | Meaning |
| --- | --- |
| `/*Key*/column` | Keep the footprint when `Key` is active. |
| `/*@value*/column` | Keep it when `@value` is supplied. |
| `/*A&B*/column` | Require both keys. |
| `/*A|B*/column` | Accept either key. |
| `/*!All*/condition` | Keep it while `All` is inactive. |
| `/*A|B&C*/column` | Evaluate left to right as `(A OR B) AND C`. |

## Footprint controls

| Template | Meaning |
| --- | --- |
| `?@a &AND ?@b` | Keep or remove both conditions together. |
| `?@a &OR ?@b` | Keep or remove both alternatives together. |
| `/*Key*/a&, b&, c` | Keep or remove the grouped list entries together. |
| `DISTINCT??? /*Key*/column` | Prevent the column footprint from taking `DISTINCT`. |
| `/*~ note */` | Emit the block comment as `/* note */`. |

## Dynamic projection

| Template | Meaning |
| --- | --- |
| `?SELECT Id, Title` | Use `Id` and `Title` as independent keys. |
| `?SELECT AlbumId AS Id!, Title` | Always keep `Id`. |
| `?SELECT AlbumId AS Id&, Title` | Keep both columns under the `Title` key. |
| `?SELECT /*Admin*/Id` | Require both `Id` and `Admin`. |

See full examples for [variables](variables.md), [collections](collections.md), [markers](markers.md), [dynamic projection](dynamic-projection.md), [handlers](handlers.md), and [template syntax](template-syntax.md).
