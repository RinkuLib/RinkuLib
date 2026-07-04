# Conditional SQL

Write valid SQL once, mark the optional spots, and the engine prunes the parts whose values you did not supply. No `WHERE 1=1`, no string concatenation.

```csharp
static readonly QueryCommand Search = new(
    "SELECT TrackId AS Id, Name FROM tracks WHERE AlbumId = @albumId AND UnitPrice >= ?@minPrice AND Name LIKE ?@name");

var tracks = Search.Query<List<Track>>(cnn, new { albumId = 1, minPrice = 0.99m });
// SELECT TrackId AS Id, Name FROM tracks WHERE AlbumId = @albumId AND UnitPrice >= @minPrice
```

`@name` was not supplied, so its clause left, along with the `AND` that would have dangled. The same works on any statement:

```csharp
static readonly QueryCommand UpdateTrack = new(
    "UPDATE tracks SET Name = ?@name, UnitPrice = ?@price WHERE TrackId = @trackId");

UpdateTrack.Execute(cnn, new { trackId = 10, name = "Remastered" });
// UPDATE tracks SET Name = @name WHERE TrackId = @trackId
```

When C# logic decides what is active, a [builder](../running-queries/parameters.md#a-builder) sets the same keys explicitly:

```csharp
var b = Search.StartBuilder();
b.Use("@albumId", 1);
if (filterByPrice) b.Use("@minPrice", 0.99m);
var tracks = b.Query<List<Track>>(cnn);
```

## Any SQL is a template

Templates are plain SQL. With no markers, the query passes through untouched, so adopting this costs nothing on existing queries.

## Structure, not clauses

The parser does not know what a `WHERE` is for. It reads structure: word boundaries, quote and comment state, parenthesis and `CASE` depth, and section keywords (`SELECT`, `FROM`, the joins, `WHERE`, `SET`, `VALUES`, `GROUP BY`, `HAVING`, `ORDER BY`, `LIMIT`, `OFFSET`, `WHEN`, `THEN`, `ELSE`, `;`), every one treated identically. A footprint is a span between structural points, and the cleanup rules, trailing operator, dangling comma, emptied section keyword, are the same rules in every section, at every nesting depth.

The consequence: nothing on these pages is a special case, and there is no list of supported clauses. A marker behaves in a projection, a `JOIN`, a `SET` list, a `THEN` branch, or a CTE body exactly as it does in a `WHERE`, and you decide how much of a statement is conditional, from one column to most of the text.

Two facts to keep straight:

- **A plain `@id` is static text.** The engine does not manage its presence. If its clause stays and you never supply a value, the database provider throws at execution. Mark it `?@id` when its presence should follow the value.
- **Everything is decided per call, parsed once.** The template is parsed when the `QueryCommand` is built. Runs only pick which parts stay.

## Reading the examples

Pages in this section show a **Template** (the SQL you write) and a **Result** (the SQL generated for a given set of supplied keys).

## The syntax

- [Optional variables](optional-variables.md). `?@Var`, the bread and butter.
- [Conditional markers](conditional-markers.md). `/*...*/` conditions, custom keys, grouping, boundaries.
- [Dynamic projection](dynamic-projection.md). `?SELECT`, column-level conditions.
- [Handlers](handlers.md). `_N`, `_S`, `_R`, `_X`, and registering your own letter.
- [Cheat sheet](cheatsheet.md). Every marker on one page.

## Changing the variable character

`@` is the default, not a requirement. The parser spots a variable as the chosen character at a word boundary, then reads the name up to the next boundary, so any character that does not otherwise start a word in your SQL works: `:` for Oracle style, `$`, `#`, whatever fits your provider.

```csharp
var oracle = new QueryCommand("SELECT * FROM t WHERE id = :id AND status = ?:status", ':');

QueryFactory.DefaultVariableChar = ':';   // app-wide, set once at startup
```

All markers compose with the chosen character unchanged: `?:status` is optional, `:table_R` is a handler. The builder's `Use('@', name, value)` overload takes the character separately from the name for `nameof`-friendly code (see [supplying values](../running-queries/parameters.md#a-builder)).
