# DynaObject

Query for `DynaObject` to get rows without declaring a type.

```csharp
// Columns: Id | Name | Email
var row = await cmd.QueryAsync<DynaObject>(cnn);

int id        = row.Get<int>("Id");
string name   = row.Get<string>("Name");
object? email = row["Email"];
object? first = row[0];
```

`Get<T>` returns a typed value, the indexer returns `object?`. Lookups take a string, a `ReadOnlySpan<char>` (no allocation), or a column index. Type conversions apply the same way as typed mapping, so `row.Get<long>("Id")` works on an `int` column.

## Duplicate names

Later duplicates get a `#n` suffix.

```csharp
// Columns: Id | Name | Id | Name
int id1 = row.Get<int>("Id");
int id2 = row.Get<int>("Id#2");
```

## Updating values

A `DynaObject` is mutable.

```csharp
row.Set("Name", "New Name");
row.Set(0, 99);
```

## Mixing with typed mapping

`DynaObject` composes like any other type, in a tuple or as a member of an object.

```csharp
var (id, rest) = cmd.Query<(int, DynaObject)>(cnn);
// id takes the first column, rest holds the remaining ones
```

## Schema-adaptive dictionaries

`DynaObject` negotiates its column names and typed reads once, then reuses that generated parser. This is the
fast open-row representation when the command cache can distinguish each projection, as it does for a
[`?SELECT`](../conditional-sql/dynamic-projection.md).

`Dictionary<string, object>` is the runtime-schema alternative. It is registered through the same
`TypeParsingInfo` infrastructure, but asks the current `DbDataReader` for names and values on every row.

```csharp
Dictionary<string, object> row = cmd.Query<Dictionary<string, object>>(cnn);
```

One root dictionary parser accepts every schema. This makes it suitable when a trusted
[`_R` handler](../conditional-sql/handlers.md#when-_r-decides-the-columns) changes the selected columns without
changing the command's parameter-usage cache key. Database `NULL` becomes `null`, names are case-insensitive,
and duplicate names receive `#2`, `#3`, and so on.

The same runtime row mapping composes under `List<Dictionary<string, object>>` for a buffered set and under
the streamed result shapes.

Like `DynaObject`, a dictionary can take the columns left after typed siblings consume theirs:

```csharp
var (id, remaining) = cmd.Query<(int, Dictionary<string, object>)>(cnn);
// id takes the first column; remaining reads every current column after it
```

The runtime schema lookup and ordinary dictionary entries cost more time and memory per row than
`DynaObject`. Prefer dynamic projection plus `DynaObject` when the projection can be represented by tracked
keys; use a dictionary when the reader schema itself must remain authoritative at call time.
