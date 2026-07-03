# Result shapes

Reads go through one method, `Query<T>`. The behavior lives in `T`: what zero rows mean, how many rows are taken, whether a `NULL` is allowed. To change the behavior, ask for a different type, not a different method.

## One object

```csharp
Track track = GetTrackById.Query<Track>(cnn, new { id = 10 });
```

Reads the first row. **Zero rows throw.** When "no row" is a normal outcome, use `Optional<T>`.

## One object or nothing

```csharp
Optional<Track> maybe = GetTrackById.Query<Optional<Track>>(cnn, new { id = 99 });

if (maybe.HasValue)
    Track track = maybe;   // implicit conversion to the inner T
```

`Optional<T>` is for reference types, `OptionalStruct<T>` for value types. Zero rows give an empty value instead of throwing.

## Many objects

```csharp
List<Track> all       = GetTracks.Query<List<Track>>(cnn);        // buffered
IEnumerable<Track> it = GetTracks.Query<IEnumerable<Track>>(cnn); // streamed

await foreach (var t in GetTracks.StreamQueryAsync<Track>(cnn))   // async stream
    Process(t);
```

`List<T>` buffers every row. `IEnumerable<T>` and `IAsyncEnumerable<T>` produce rows as you enumerate, keeping memory flat on large results. A streamed result holds the reader open while you iterate, so finish or dispose it before reusing the connection. Zero rows give an empty collection.

## The full set

| Ask for | Zero rows | Many rows | Notes |
| --- | --- | --- | --- |
| `T` | throws | takes the first | |
| `List<T>` / `IEnumerable<T>` | empty collection | all rows | |
| `Optional<T>` | empty (`HasValue == false`) | takes the first | reference types |
| `OptionalStruct<T>` | empty | takes the first | value types |
| `Single<T>` | empty value | **throws** on a second row | asserts at most one row |
| `MaybeNull<T>` | empty | takes the first | the mapped object itself may be `null` |
| `OptionalNullable<T>` | empty | takes the first | both of the above |

Each wrapper converts implicitly to its inner `T`, so it costs nothing to pass around.

```csharp
string? maybe = await cmd.QueryAsync<MaybeNull<string>>(cnn, new { txt = "def" });
```

## Zero rows and NULL are different things

A nullable type answers "the value may be NULL", not "there may be no row".

```csharp
int? n = GetNumber.Query<int?>(cnn);                       // a NULL value becomes null. Zero rows still throw
OptionalStruct<int> o = GetNumber.Query<OptionalStruct<int>>(cnn); // zero rows give an empty value
```

Same on the reference side: `Query<string>` throws on a NULL value, `Query<MaybeNull<string>>` hands you `null`. Column-level NULL rules are on [nullability](../mapping/nullability.md).

## Scalars

A primitive `T` maps the first column of the first row. For a value that is the whole point of the call, `ExecuteScalar<T>` is the natural choice.

```csharp
int count = CountTracks.ExecuteScalar<int>(cnn);
int alt   = CountTracks.Query<int>(cnn);   // also works
```

## These shapes are just types

`Optional<T>` is not a feature of `Query`. It is a small type the engine knows how to produce, and you can wrap it in a name you prefer.

```csharp
public static Optional<T> QueryFirstOrDefault<T>(this QueryCommand cmd, DbConnection cnn, object? p = null)
    where T : class => cmd.Query<Optional<T>>(cnn, p);
```

Adding a shape of your own works the same way the built-in ones were added. See [matching and parsers](../mapping/custom-parsing.md#adding-a-result-shape).
