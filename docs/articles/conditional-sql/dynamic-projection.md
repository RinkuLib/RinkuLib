# Dynamic projection

`?SELECT` gives each projected column a conditional key.

```csharp
static readonly QueryCommand AlbumProjection = new("?SELECT AlbumId AS Id, Title, ReleaseYear FROM albums");

var values = AlbumProjection.StartBuilder();
values.Use("Title");

List<DynaObject> albums = values.Query<List<DynaObject>>(cnn);
```

```sql
SELECT Title FROM albums
```

When none of the projection keys are active, the complete `SELECT` section disappears.

```csharp
List<DynaObject> albums = AlbumProjection.Query<List<DynaObject>>(cnn);
```

```sql
FROM albums
-- Passed to the provider as written.
```

Rinku does not validate the generated SQL. This standalone result is invalid, but another condition combination can retain a [valid template alternative](template-syntax.md#conditions-may-select-incompatible-template-alternatives).

The column name or alias becomes its key.

```sql
?SELECT a.AlbumId, a.Title AS Name, COUNT(*) AS Total FROM albums a
```

```text
a.AlbumId        -> AlbumId
a.Title AS Name  -> Name
COUNT(*) AS Total -> Total
```

## Alias calculated columns

The parser takes the final identifier or quoted identifier before the comma or next SQL section. It does not interpret the expression.

```sql
?SELECT a.Price * a.Quantity FROM album_lines a
```

```text
Key: Quantity
```

Give calculated columns an explicit, stable key.

```sql
?SELECT a.Price * a.Quantity AS LineTotal FROM album_lines a
```

```text
Key: LineTotal
```

Activating `LineTotal` keeps the aliased expression in the generated SQL.

```sql
SELECT a.Price * a.Quantity AS LineTotal FROM album_lines a
```

## Keep a column every time

`!` after a projected expression makes that column unconditional.

```sql
?SELECT AlbumId AS Id!, Title, ReleaseYear FROM albums
```

When no keys are active, only the unconditional `Id` column remains.

```sql
SELECT AlbumId AS Id FROM albums
```

Activating `Title` adds that column after the unconditional `Id`.

```sql
SELECT AlbumId AS Id, Title FROM albums
```

The `!` suffix is only valid inside a `?SELECT`.

## Group columns under one key

`&,` joins columns to the key derived from the last column.

```sql
?SELECT AlbumId AS Id&, Title, ReleaseYear FROM albums
```

`Title` keeps both `Id` and `Title`.

```sql
SELECT AlbumId AS Id, Title FROM albums
```

`Id` is not a separate key in this template.

## Add another condition

An explicit marker adds its requirement to the column's derived key.

```sql
?SELECT AlbumId AS Id!, Title, /*Admin*/InternalCode FROM albums
```

`InternalCode` remains unavailable until its additional `Admin` key is also active.

```sql
SELECT AlbumId AS Id FROM albums
```

When both keys are active, the generated projection contains both columns.

```sql
SELECT AlbumId AS Id, InternalCode FROM albums
```

On an always-kept column, a marker replaces the unconditional behavior.

```sql
?SELECT /*Manual*/AlbumId AS Id!, Title FROM albums
```

When `Manual` is inactive, `Title` can remain without the otherwise unconditional `Id`.

```sql
SELECT Title FROM albums
```

## Keep UNION projections aligned

Matching aliases share the same keys across `?SELECT` statements.

```sql
?SELECT ArtistId AS Id, Name FROM artists UNION ALL ?SELECT GenreId AS Id, Name FROM genres
```

Activating `Name` keeps the matching projection on both sides of the union.

```sql
SELECT Name FROM artists UNION ALL SELECT Name FROM genres
```

Use matching aliases on both sides. Different derived keys can produce incompatible projections.

## Use a conditional projection in a CTE

```sql
WITH a AS (?SELECT AlbumId AS Id, Title, ArtistId FROM albums) SELECT * FROM a
```

Activating `Title` changes the projection inside the CTE.

```sql
WITH a AS (SELECT Title FROM albums) SELECT * FROM a
```

## Isolate a modifier

Use `???` when a modifier should remain while its first projected column disappears.

```sql
?SELECT DISTINCT??? Title, Composer FROM albums
```

When only `Composer` is active, `DISTINCT` remains attached to the projection.

```sql
SELECT DISTINCT Composer FROM albums
```

## Parser caching follows the keys

Each active key combination has its own typed row parser.

```sql
?SELECT AlbumId AS Id!, Title, ReleaseYear FROM albums
```

```text
Keys: Title             -> parser for Id, Title
Keys: ReleaseYear       -> parser for Id, ReleaseYear
Keys: Title,ReleaseYear -> parser for Id, Title, ReleaseYear
```

A raw handler value is not a key. Changing a raw projection can therefore reuse a parser built for another schema.

```csharp
static readonly QueryCommand UnsafeTypedProjection = new("SELECT @columns_R FROM albums");

List<Album> first = UnsafeTypedProjection.Query<List<Album>>(cnn, new { columns = "AlbumId AS Id, Title" });

List<Album> second = UnsafeTypedProjection.Query<List<Album>>(cnn, new { columns = "ReleaseYear, Title" });
// The active key set did not change, so the first typed parser may be reused.
```

Use `?SELECT` for a known set of typed projections. Use a schema-adaptive [dictionary row](../mapping/dynamic-rows.md) when the selected columns are truly open-ended.
