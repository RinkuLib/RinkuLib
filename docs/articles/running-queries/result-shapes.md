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

`List<T>` buffers every row. `IEnumerable<T>` and `IAsyncEnumerable<T>` produce rows as you enumerate, keeping memory flat on large results. Zero rows give an empty collection.

When `T` is itself nested, a `List<Artist>` whose `Artist` holds a `List<Album>`, a join's repeated rows fold back into it. See [collections](collections.md).

A streamed result waits. `Query<IEnumerable<T>>` runs nothing when you call it, the command goes off on the first step of walking the rows, and the reader closes when the walk ends, whether you reach the last row or leave the loop early.

```csharp
var tracks = GetTracks.Query<IEnumerable<Track>>(cnn);   // nothing has run
foreach (var t in tracks)                                // runs it here
    Process(t);                                          // reader closed at the end
```

A result you decide not to walk holds nothing, and walking one twice runs the command twice. Ask for `List<T>` when you mean to read the rows more than once.

Two things follow from the waiting. A command handed back by the `out DbCommand` overloads has not run yet, so its output parameters fill only once you walk the rows. And what the database refuses surfaces where you walk, not where you asked for the result.

`QueryAsync<IEnumerable<T>>` waits the same way, and awaiting it gives a sequence, not an async stream. The rows still come as you walk them and the walk is a synchronous one. `StreamQueryAsync<T>` is the async stream, and the one to reach for when the rows should come asynchronously.

## The built-in shapes

These, and a few more, are the shapes Rinku ships for common cases. Each is a small type that wraps the element parser with one rule of its own, and you can add your own the same way (see [below](#adding-your-own-shape)). The set is open.

```csharp
int count = GetNumber.Query<int>(cnn);                         // Throws if there are no rows. Otherwise returns the first row.
Optional<int> maybe = GetNumber.Query<Optional<int>>(cnn);     // Returns an empty value if there are no rows. Otherwise returns the first row.
List<int> all = GetNumber.Query<List<int>>(cnn);                // Returns an empty list if there are no rows. Otherwise returns all rows.
Single<int> one = GetNumber.Query<Single<int>>(cnn);            // Returns a default value if there are no rows. Returns the row if there is one. Throws if there are multiple rows.
int? n = GetNumber.Query<int?>(cnn);                            // Throws if there are no rows. Returns null if the value is NULL.
OptionalStruct<int> o = GetNumber.Query<OptionalStruct<int>>(cnn); // HasValue == false if there are no rows. Throws if the value is NULL.
MaybeNull<Person> person = GetPerson.Query<MaybeNull<Person>>(cnn); // Throws if there are no rows. HasValue == false if the value is NULL.
OptionalNullable<Person> either = GetPerson.Query<OptionalNullable<Person>>(cnn); // HasValue == false if there are no rows or the value is NULL.
```

When `T` uses multiple rows, more than one row can be part of the result. An `Artist` with a `List<Album>` can use several joined rows to make one `Artist`.

```csharp
Single<Artist> artist = GetArtists.Query<Single<Artist>>(cnn);  // throws if another Artist follows
```

This throws if a second `Artist` follows. It does not throw when one `Artist` uses several rows for its albums. See [collections](collections.md) for multirow types.

The row rule and the `NULL` rule are independent. `Optional<T>` accepts no rows but throws for a `NULL` value. `MaybeNull<T>` accepts a `NULL` value but throws when there are no rows. `OptionalNullable<T>` accepts both. A row can also [collapse](../mapping/nullability.md#abortonnull-collapse-the-object) to nothing, which follows the same `NULL` rules. Every wrapper converts implicitly to its inner `T`, so you can pass it wherever the `T` is expected. Column-level `NULL` rules are on [nullability](../mapping/nullability.md).

## Scalars

A primitive `T` maps the first column of the first row. `Query<T>` is valid when
the command is a `SELECT` whose result is one scalar. `ExecuteScalar<T>` is the
matching shape for an execution that also returns one value.

```csharp
int count = CountTracks.Query<int>(cnn);              // SELECT: read one scalar
int alt   = CountTracks.ExecuteScalar<int>(cnn);      // also works
```

## Tuples

`ValueTuple` is read by constructor position. Its elements are read sequentially and the tuple names (`Item1`, `Item2`, ...) are ignored. Each element then uses its normal parser. Basic elements match by type. Complex elements match their members by name. See [names](../mapping/names.md) and [reading order](../mapping/reading-order.md).

```csharp
var pair = cmd.Query<(int Id, string Name)>(cnn);

// SELECT 7, 'Intro' -- works.
// SELECT 7          -- does not.
```

Every tuple element must be readable.

When the same type must accept more than one row shape, use a normal mapped type and make the fallback explicit:

```csharp
public record Track(int Id, string? Name = null);

// Id                         -> Track(7, null)
// Id | Name                  -> Track(7, "Intro")
```

Use separate construction paths instead when the two shapes need different construction logic.

```csharp
// Tuple elements are read sequentially. Tuple names are ignored.
var (id, name) = cmd.Query<(int, string)>(cnn);

// Tuple elements are read sequentially and Location maps its members by name.
var (id, location) = cmd.Query<(int, Location)>(cnn);

// Each Person maps its members by name.
public record struct Person(int Id, string Name);
var (p1, p2) = cmd.Query<(Person, Person)>(cnn);
// Columns: Id | Name | Id | Name  -> p1 takes the first pair, p2 the second
```

This behavior is built in for `ValueTuple`. Other types can use their own registered parsing implementation when they need a different mapping rule. See [registering with another info](../mapping/registration.md#registering-with-another-info).

## Adding your own shape

Every shape above is an ordinary type the engine produces from a small parser. Wrap one in a name you prefer:

```csharp
public static OptionalNullable<T> QueryFirstOrDefault<T>(this QueryCommand cmd, DbConnection cnn, object? p = null)
    where T : class => cmd.Query<OptionalNullable<T>>(cnn, p);
```

Adding a shape of your own works the same way the built-in ones were added. See [parsers](../mapping/parsers.md#adding-a-result-shape).
