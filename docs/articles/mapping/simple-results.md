# Choosing the result type

*One entry point, `Query<T>`. The behavior lives in `T`.*

Almost every read goes through `Query<T>` (the only exception is `StreamQueryAsync`, which returns an `IAsyncEnumerable<T>`). `Query<T>` itself does very little. It runs the command, and for each result hands the reader to the parser chosen for `T`. What happens with zero rows, many rows, or a `null` value is decided by **`T`**, not by `Query`. So you don't reach for a different method to change behavior, you ask for a different `T`.

Under the hood, asking for `T` selects an `ITypeParser<T>`. A different `T` selects a different parser, and that parser is where the rules live.

## One object

```csharp
Track track = GetTrackById.Query<Track>(cnn, new { id = 10 });
```

The parser for a plain type reads the first row and builds the object (see [complex objects](complex-objects.md) for constructors, members, and nesting). If there are **no rows it throws** ("No values were returned from the query"). That throw is the parser's choice on empty, not something `Query` does. When "no row" is a normal outcome, ask for a type whose parser tolerates it (below).

## Many objects

```csharp
List<Track> all       = GetTracks.Query<List<Track>>(cnn);        // buffered
IEnumerable<Track> it = GetTracks.Query<IEnumerable<Track>>(cnn); // streamed lazily
await foreach (var t in GetTracks.StreamQueryAsync<Track>(cnn))   // async stream
    Process(t);
```

`List<T>` buffers every row. `IEnumerable<T>` and `IAsyncEnumerable<T>` produce rows as you enumerate, which keeps memory flat for large results. A streamed enumerable holds the reader open while you iterate, so finish or dispose it before reusing the connection. With **no rows these return an empty collection**, again because that's what their parser's empty result is.

## Saying how many rows you expect

A few built-in types wrap another type to express how many rows you expect. None of them is special, each is a small `ITypeParser<T>` with a chosen empty result and, sometimes, a row-count check.

| Ask for | On zero rows | On many rows | Inner value |
| --- | --- | --- | --- |
| `T` (for example `Track`) | throws | takes the first | the object |
| `List<T>` / `IEnumerable<T>` | empty collection | all rows | the elements |
| `Optional<T>` | empty (`HasValue == false`) | takes the first | reference type |
| `OptionalStruct<T>` | empty | takes the first | value type |
| `Single<T>` | empty value | **throws** on a second row | asserts one row |
| `MaybeNull<T>` | one row whose mapped object may be `null` | takes the first | reference type |
| `T?` | a `null` struct | takes the first | nullable struct |

```csharp
Track t           = GetTrackById.Query<Track>(cnn, new { id = 10 });            // throws if absent
Optional<Track> o = GetTrackById.Query<Optional<Track>>(cnn, new { id = 99 });  // empty if absent

if (o.HasValue) { /* ... */ }
Track track = o;   // implicit conversion to the inner T
```

Each wrapper is implicitly convertible to its inner `T`, so they cost nothing to pass around.

## These wrappers are just types

`Optional<T>` is the clearest example of the whole design. Its parser wraps the element parser, returns an empty value when there are no rows, and otherwise wraps the parsed element. That's the entire feature. You can build the same idea yourself, or wrap it in a friendlier name, exactly because the behavior is in the type and not in `Query`.

```csharp
public static Optional<T> QueryFirstOrDefault<T>(this QueryCommand cmd, DbConnection cnn, object? p = null)
    where T : class => cmd.Query<Optional<T>>(cnn, p);
```

## A note on scalars

A primitive `T` (`int`, `string`, an enum) maps the first column of the first row. For a value that's the whole point of the call, such as `SELECT COUNT(*)`, `ExecuteScalar<T>` is the natural choice, it runs ADO.NET's `ExecuteScalar` instead of a reader. `Query<int>` also works and returns the first column of the first row.

```csharp
int count = CountTracks.ExecuteScalar<int>(cnn);
```
