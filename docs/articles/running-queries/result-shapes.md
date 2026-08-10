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

```csharp
IEnumerable<Track> lazy = GetTracks.Query<IEnumerable<Track>>(cnn); // no execution yet
foreach (var track in lazy)                                     // executes on the first MoveNext
    Process(track);                                             // reader closes when the loop ends
```

Use `List<T>` when the rows must be reused. All collection shapes return an empty collection for zero rows. `QueryAsync<IEnumerable<T>>` executes asynchronously but returns a synchronously enumerated sequence, use
`StreamQueryAsync<T>` when each row should be consumed asynchronously:

```csharp
IEnumerable<Track> asyncStarted = await GetTracks.QueryAsync<IEnumerable<Track>>(cnn); // query has started; sync walk
await foreach (var track in GetTracks.StreamQueryAsync<Track>(cnn))                     // true async row consumption
    Process(track);
```

For streamed shapes, database errors surface while the sequence is consumed.

If the command has output parameters, use `out DbCommand` overload and keep it until
enumeration has finished before reading them.

```csharp
IEnumerable<Track> rows = GetTracks.Query<IEnumerable<Track>>(cnn, out DbCommand cmd);
foreach (var track in rows)
    Process(track);

var outVal = cmd.GetOutputValue<T>(...)
cmd.Dispose();
```
## The built-in shapes

These, and a few more, are the shapes Rinku ships for common cases. See [adding your own shape](#adding-your-own-shape) and [custom multi-row types](../mapping/custom-multi-row-types.md) to add custom behaviors.

```csharp
int count = GetNumber.Query<int>(cnn);                             // Throws if there are no rows. Otherwise returns the first row.
OptionalStruct<int> maybe = GetNumber.Query<OptionalStruct<int>>(cnn); // Empty if there are no rows. Otherwise contains the first row.
List<int> all = GetNumber.Query<List<int>>(cnn);                    // Empty if there are no rows. Otherwise contains all rows.
Single<int> one = GetNumber.Query<Single<int>>(cnn);                // Exactly one result; throws if there are none or several.
int? n = GetNumber.Query<int?>(cnn);                                // Throws if there are no rows. Returns null if the value is NULL.
MaybeNull<Person> person = GetPerson.Query<MaybeNull<Person>>(cnn); // Throws if there are no rows. HasValue == false if the value is NULL.
OptionalNullable<Person> either = GetPerson.Query<OptionalNullable<Person>>(cnn); // HasValue == false if there are no rows or the value is NULL.
```

When `T` uses multiple rows, more than one row can be part of the result. An `Artist` with a `List<Album>` can use several joined rows to make one `Artist`.

```csharp
Single<Artist> artist = GetArtists.Query<Single<Artist>>(cnn);  // throws if none exists or another Artist follows, but may have more than row for the albums
```

This throws if no `Artist` exists or if a second `Artist` follows. It does not throw when one `Artist` uses several rows for its albums. See [collections](collections.md) for multirow types.

The "has row" rule and the `NULL` rule are independent. `Optional<T>` accepts no rows but throws for a `NULL` value. `MaybeNull<T>` accepts a `NULL` value but throws when there are no rows. `OptionalNullable<T>` accepts both. A row can also [collapse](../mapping/nullability.md#abortonnull-collapse-the-object) to nothing, which follows the same `NULL` rules. Every wrapper converts implicitly to its inner `T`, so you can pass it wherever the `T` is expected. Column-level `NULL` rules are on [nullability](../mapping/nullability.md).

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
public static T? QueryFirstOrDefault<T>(this QueryCommand cmd, DbConnection cnn, object? p = null)
    where T : class => cmd.Query<OptionalNullable<T>>(cnn, p).Value;
```

Adding a shape of your own works the same way the built-in ones were added. See [parsers](../mapping/parsers.md#adding-a-result-shape).
