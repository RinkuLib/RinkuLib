# DynaObject

*Dictionary-like dynamic rows.*

Query for `DynaObject` to get a row you can read by name or index without a fixed type.

```csharp
// Schema: | ID | Name | Email |
var row = await cmd.QueryAsync<DynaObject>(cnn, ct: ct);

int id        = row.Get<int>("ID");
string name   = row.Get<string>("Name");
var email     = row.Get<string?>(spanKey);   // ReadOnlySpan<char> key
object? first = row[0];                       // integer index
```

The indexer returns `object?`, and `Get<T>` returns a typed value. Lookups accept a string, a `ReadOnlySpan<char>` (handy for high-performance parsing), or an integer index.

## Duplicate names

Duplicate column names get a suffix on each later appearance.

```csharp
// Schema: | ID | Name | ID | Name |
int id1      = row.Get<int>("ID");
int id2      = row.Get<int>("ID#2");
object name2 = row["Name#2"];
```

## Updating values

`DynaObject` is mutable, even though it represents a row.

```csharp
row.Set("Name", "New Name");
row.Set(0, 99);
```
