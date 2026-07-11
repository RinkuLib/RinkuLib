# Result shapes

Reads go through one method, `Query<T>`. The behavior lives in `T`. It decides what zero rows mean, how many rows are taken, and whether a `NULL` is allowed. To change the behavior, ask for a different type, not a different method.

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

## The built-in shapes

These, and a few more, are the shapes Rinku ships for common cases. Each is a small type that wraps the element parser with one rule of its own, and you can add your own the same way (see [below](#adding-your-own-shape)). The set is open.

They answer two separate questions.

**How many rows.**

| Ask for | No row | One row | Extra rows |
| --- | --- | --- | --- |
| `T` | throws | the object | takes the first |
| `Optional<T>` | `HasValue == false` | the object | takes the first |
| `OptionalStruct<T>` | `HasValue == false` | the value | takes the first |
| `List<T>` / `IEnumerable<T>` | empty collection | one element | all rows |
| `Single<T>` | a default `Single<T>` | the object | **throws** |

**Whether the value may be null.** A row can carry a `NULL`, or a nested object can [collapse](../mapping/nullability.md#invalidonnull-collapse-the-object) to nothing. By default that throws. A null-accepting shape takes it instead.

| Ask for | Null value |
| --- | --- |
| `MaybeNull<T>` | `HasValue == false` (reference types) |
| `T?` | `null` (value types) |

The two questions are independent. `Optional<T>` accepts a missing row but throws on a `NULL` value. `MaybeNull<T>` accepts a `NULL` value but throws on a missing row. `OptionalNullable<T>` is the two stacked, `Optional`'s missing-row rule around `MaybeNull`'s null rule, flattened to a single `HasValue == false` for both cases.

```csharp
int? n                = GetNumber.Query<int?>(cnn);               // no row -> throws; NULL value -> null; 
OptionalStruct<int> o = GetNumber.Query<OptionalStruct<int>>(cnn); // no row -> HasValue == false; NULL value -> throws;
```

Every wrapper converts implicitly to its inner `T`, so you can pass it wherever the `T` is expected. Column-level `NULL` rules are on [nullability](../mapping/nullability.md).

## Scalars

A primitive `T` maps the first column of the first row. `ExecuteScalar<T>` runs the command and returns that single value.

```csharp
int count = CountTracks.ExecuteScalar<int>(cnn);
int alt   = CountTracks.Query<int>(cnn);   // also works
```

## Tuples

Ask for a `ValueTuple` and its elements are taken in order, the tuple names (`Item1`, `Item2`, ...) ignored. Each element then negotiates as usual. A basic type has no name left to match, so it takes the next column. A complex element still matches its own members by name.

```csharp
// Basic: strictly by column order
var (id, name) = cmd.Query<(int, string)>(cnn);

// Mixed: the basic element takes the next column, the complex one negotiates its own
var (id, location) = cmd.Query<(int, Location)>(cnn);

// Complex: each element matches its member names, in order
public record struct Person(int Id, string Name);
var (p1, p2) = cmd.Query<(Person, Person)>(cnn);
// Columns: Id | Name | Id | Name  -> p1 takes the first pair, p2 the second
```

Positional parsing comes from the type's registration, and any type can opt into it. See [registering with another info](../mapping/registration.md#registering-with-another-info).

## Adding your own shape

Every shape above is an ordinary type the engine produces from a small parser. Wrap one in a name you prefer:

```csharp
public static OptionalNullable<T> QueryFirstOrDefault<T>(this QueryCommand cmd, DbConnection cnn, object? p = null)
    where T : class => cmd.Query<OptionalNullable<T>>(cnn, p);
```

Adding a shape of your own works the same way the built-in ones were added. See [parsers](../mapping/parsers.md#adding-a-result-shape).
