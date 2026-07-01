# Base SQL compatibility

*Any valid SQL is already a valid template.*

RinkuLib templates are parsed linearly. If the engine reaches the end of the string without hitting any conditional markers, the template stays a single, unfragmented segment with no attached conditions. So **any valid SQL is automatically a valid template**.

Without markers, the parser finds no optional boundaries and leaves the query intact.

* **Template:** `SELECT * FROM tracks WHERE GenreId = 1`
* **Result:** `SELECT * FROM tracks WHERE GenreId = 1`

Parameters are part of the static text. Since no markers define them as conditional, the parser finds no optional boundaries.

* **Template:** `UPDATE tracks SET UnitPrice = @price WHERE TrackId = @id`
* **Result:** `UPDATE tracks SET UnitPrice = @price WHERE TrackId = @id`

## Customizing the variable character

The engine spots variables by a prefix character. `@` is the default, but it's configurable.

- **Local override.** When compiling a template, pass a `variableChar`. If you pass `:`, the engine parses `:Var` or `?:Var` instead of `@Var`.
- **Global default.** Set the public static field `QueryFactory.DefaultVariableChar` (default `'@'`) to change the prefix for the whole app.

Changing it globally means all future templates compile with your prefix without passing it each time.
