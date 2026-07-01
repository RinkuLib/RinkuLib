# Handlers (`_Letter`)

*Reshape the SQL with an actual runtime value.*

Sometimes a query must change based on a value, not just a true-or-false condition, to change a table name, say, or to expand a list into an `IN` clause. Handlers do this with the syntax `@Var_Letter`, where `_Letter` selects a handler. The variable itself is `@Var`. The `_Letter` is not part of the name.

> If a handled variable's value is required but missing, the error is raised during **query generation**, not at the database call, because the handler needs the value to build the SQL string.

## Built-in handlers

**`_N`, numeric injection.** Inserts a number into the SQL text.

* `SELECT * FROM tracks ORDER BY @Index_N` -> (`@Index` = 3) -> `... ORDER BY 3`

**`_S`, string literal.** Inserts a string, escaped with single quotes.

* `SELECT * FROM artists WHERE Name = @Name_S` -> (`@Name` = Queen) -> `... WHERE Name = 'Queen'`

**`_R`, raw SQL.** Inserts a string **without escaping**.

* `SELECT * FROM @Table_R WHERE UnitPrice > 0` -> (`@Table` = tracks) -> `SELECT * FROM tracks ...`

> **Warning.** `_R` injects values verbatim. Only pass fully controlled, sanitized values, or you create a SQL-injection hole.

**`_X`, collection spreading.** Expands an `IEnumerable` into one parameter per item (`@Var_1`, `@Var_2`).

* `SELECT * FROM tracks WHERE GenreId IN (@GenreIds_X)` -> (`@GenreIds` = [1,2,3]) -> `... IN (@GenreId_1, @GenreId_2, @GenreId_3)`

Handlers combine with optional markers.

* `SELECT * FROM tracks WHERE GenreId IN (?@GenreIds_X)` drops the whole clause if `@GenreIds` is not provided.

The four built-ins cover the common cases, and they're not the whole story. The handler set is itself a base point. `_N`, `_S`, `_R`, and `_X` are the first implementations of a handler hook, and you can register your own letter the same way. See [custom handlers](custom-handlers.md).
