# Conditional SQL

*One query that adapts to what you supply, without string-building.*

You write valid SQL once, **mark** the optional spots, and the engine **prunes** the parts whose values you didn't supply, keeping the result valid. It replaces the usual `WHERE 1=1` and string concatenation. You can use it on its own to produce a `DbCommand`, with or without [mapping](../mapping/index.md) the result.

```csharp
// "?" marks @minPrice and @name optional.
static readonly QueryCommand Search = new(
    "SELECT TrackId AS Id, Name FROM tracks WHERE AlbumId = @albumId AND UnitPrice >= ?@minPrice AND Name LIKE ?@name");

// Only @albumId and @minPrice supplied, so the @name clause is pruned.
var tracks = Search.Query<List<Track>>(cnn, new { albumId = 1, minPrice = 0.99m });
// SELECT TrackId AS Id, Name FROM tracks WHERE AlbumId = @albumId AND UnitPrice >= @minPrice
```

A present marker is kept. An absent one is pruned, along with the dangling `AND` that would have been left behind. The parameter object drives it. Each member that's present activates its marker.

When C# logic should decide what's active, a [builder](../executing/builders.md) sets the same markers explicitly, the other way to supply them.

```csharp
var b = Search.StartBuilder().Use("@albumId", 1);
if (filterByPrice) b.Use("@minPrice", 0.99m);
var tracks = b.Query<List<Track>>(cnn);
```

Either way, parsing happens once when the [command is built](../executing/query-command.md). Only the pruning differs per call.

## How to read the examples

Each example shows a **Template** (the SQL you write) and a **Result** (the SQL generated when the marked keys are *not* used).

> **Data vs. presence.** A plain parameter like `@id` is static text to the engine. It controls neither the parameter's presence nor its value. If a clause stays in the SQL but you never supplied the value, the **database provider** throws at execution. Mark a variable optional (`?@id`) when its presence should depend on whether you set it.

## The markers

- [Base SQL](base-sql.md). Any valid SQL is already a valid template. Choosing the variable character.
- [Optional variables](optional-variables.md). `?@Var` and footprint pruning.
- [Conditional markers](conditional-markers.md). `/*...*/`, custom keys, the `???` boundary.
- [Operators & grouping](operators-and-grouping.md). `&AND`, `&OR`, `&,`, and the `|`, `&`, `!` comment operators.
- [Dynamic projection](dynamic-projection.md). `?SELECT`.
- [Handlers](handlers.md). `_N`, `_S`, `_R`, `_X`, and [writing your own](custom-handlers.md).
- [Special clauses](special-clauses.md). CASE, JOIN, CTE edge cases.

For a one-page reference, see the [cheat sheet](cheatsheet.md).

## The markers bottom out in something you can extend

A whole `SELECT` can be made optional with `?SELECT`. A list expands into an `IN` clause with the `_X` handler:

```csharp
// template
"SELECT * FROM tracks WHERE GenreId IN (@genreIds_X)"
// @genreIds = [1, 2, 3]  ->  GenreId IN (@genreId_1, @genreId_2, @genreId_3)
```

The handlers (`_N`, `_S`, `_R`, `_X`) aren't a fixed list, they're built-ins over a handler interface you can implement for your own suffix. That's the layer beneath the markers. The built-in ones are just the first implementations of it. See [writing your own handler](custom-handlers.md).
