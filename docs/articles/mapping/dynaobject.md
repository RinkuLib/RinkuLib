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
