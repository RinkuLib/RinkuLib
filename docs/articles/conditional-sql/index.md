# Conditional SQL

Write valid SQL once, mark the optional spots, and each run drops the parts whose values you did not supply. No `WHERE 1=1`, no string concatenation.

```sql
SELECT TrackId AS Id, Name FROM tracks
WHERE AlbumId = @albumId AND UnitPrice >= ?@minPrice AND Name LIKE ?@name

-- @albumId and @minPrice supplied, no @name
SELECT TrackId AS Id, Name FROM tracks
WHERE AlbumId = @albumId AND UnitPrice >= @minPrice
```

`@name` was left out, so its clause dropped with the dangling `AND`. Keys come from the object or builder you pass at run time, see [supplying values](../running-queries/parameters.md#a-builder).

## Markers are opt-in

Markers are the only thing the engine acts on. A template with none is returned as written, so you can add this to queries you already have without changing what they do.

```sql
SELECT AlbumId, Title FROM albums ORDER BY Title
```

It also means the engine does nothing about what you leave unmarked. A plain `@albumId` is not conditional: it stays whether or not you supply a value, so a missing value throws just like handwritten SQL. Only the marked `?@minLength` drops when its value is absent.

```sql
SELECT Name, Composer FROM tracks
WHERE AlbumId = @albumId AND Milliseconds > ?@minLength

-- nothing supplied: ?@minLength drops, @albumId stays and the run throws for its missing value
SELECT Name, Composer FROM tracks
WHERE AlbumId = @albumId
```

Mark a spot with `?` when its presence should follow its value.

## Where markers work

A marker works the same in any statement, at any spot. An `UPDATE`:

```sql
UPDATE tracks SET Name = ?@name, UnitPrice = ?@price WHERE TrackId = @trackId

-- @trackId and @name supplied, no @price
UPDATE tracks SET Name = @name WHERE TrackId = @trackId
```

And one statement can carry conditions in several places at once. Here a CTE's `WHERE`, a `HAVING`, and the outer `WHERE` each prune on their own.

```sql
WITH spend AS (
    SELECT CustomerId, SUM(Total) AS Total, COUNT(*) AS Orders
    FROM invoices
    WHERE InvoiceDate >= ?@since
    GROUP BY CustomerId
    HAVING SUM(Total) >= ?@minSpend
)
SELECT c.FirstName, c.LastName, c.Country, s.Total, s.Orders
FROM spend s
JOIN customers c ON c.CustomerId = s.CustomerId
WHERE c.Country = ?@country
ORDER BY s.Total DESC
```

```sql
-- only @minSpend supplied
WITH spend AS (
    SELECT CustomerId, SUM(Total) AS Total, COUNT(*) AS Orders
    FROM invoices
    GROUP BY CustomerId
    HAVING SUM(Total) >= @minSpend
)
SELECT c.FirstName, c.LastName, c.Country, s.Total, s.Orders
FROM spend s
JOIN customers c ON c.CustomerId = s.CustomerId
ORDER BY s.Total DESC
```

```sql
-- @since and @country supplied
WITH spend AS (
    SELECT CustomerId, SUM(Total) AS Total, COUNT(*) AS Orders
    FROM invoices
    WHERE InvoiceDate >= @since
    GROUP BY CustomerId
)
SELECT c.FirstName, c.LastName, c.Country, s.Total, s.Orders
FROM spend s
JOIN customers c ON c.CustomerId = s.CustomerId
WHERE c.Country = @country
ORDER BY s.Total DESC
```

## How the template is read

The engine never builds your SQL into a tree. It scans the text, tracking structure as it goes, the sections, parentheses, quotes, and `CASE` depth, and the marker rules follow from that alone.

It also means the template does not have to be valid SQL on its own. Nothing is checked, only the SQL a run produces has to hold together. So you can lay down alternatives that could never coexist and let each run keep one. These two `FROM` clauses never appear together.

```sql
SELECT TrackId, Name /*!Archived*/FROM tracks /*Archived*/FROM tracks_archive ORDER BY Name

-- Archived off
SELECT TrackId, Name FROM tracks ORDER BY Name

-- Archived on
SELECT TrackId, Name FROM tracks_archive ORDER BY Name
```

The flip side is that a template broken for any other reason stays broken, since nothing here validates it.

## Reading the examples

The pages that follow show SQL blocks. The first block in an example is the template you write, the one carrying the markers. Each block below it is the SQL the engine generates, under a comment naming which keys that run supplies.

```sql
SELECT * FROM tracks WHERE AlbumId = @albumId AND Name = ?@Name

-- @albumId supplied, no @Name
SELECT * FROM tracks WHERE AlbumId = @albumId
```

A plain `@x` is a required key. It is taken as supplied throughout, so the labels only call out the optional markers a run does or does not include.

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
