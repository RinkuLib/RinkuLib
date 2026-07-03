# FAQ

### Do I create a `QueryCommand` per call?

No. Create it once, a `static readonly` field is ideal, and reuse it. Parsing happens at construction. Per-call state travels in the parameter object or a builder.

```csharp
static readonly QueryCommand GetTrackById = new("SELECT TrackId AS Id, Name FROM tracks WHERE TrackId = @id");

Track t = GetTrackById.Query<Track>(cnn, new { id = 10 });
```

### Is it thread-safe to share a `QueryCommand`?

Yes, that is the intended usage. The command holds no per-call state, and its internal caches are guarded. One shared command, fresh per-call values.

### The clause is in the SQL but the provider throws about a missing parameter.

A plain `@Id` is static text, the engine does not manage its presence. If its clause stays and you never supplied a value, the provider throws at execution. Mark it `?@Id` when its presence should follow the value. See [conditional SQL](../conditional-sql/index.md#any-sql-is-a-template).

### How do I avoid `WHERE 1=1`?

You do not need it. Write the SQL as if every parameter is used, add `?` to the optional ones, and the engine prunes dangling operators, commas, and emptied clauses. See [optional variables](../conditional-sql/optional-variables.md).

### `Query<T>` threw "No values were returned from the query".

The query returned zero rows and a plain `T` treats that as an error. Ask for `Optional<T>` (or `OptionalStruct<T>`) when no row is a normal outcome. See [result shapes](../running-queries/result-shapes.md).

### My `IN (@ids_X)` clause disappeared.

An empty collection counts as not supplied, so an optional `?@ids_X` clause is pruned rather than generating `IN ()`. Pass a non-empty collection to keep the clause.

### A nested object is null even though some of its columns had values.

A slot marked `[InvalidOnNull]` collapses the whole nested object when its column is NULL. That is its purpose, typically for outer joins. See [nullability](../mapping/nullability.md).

### A nested type is not being mapped at all.

Types reached only through another type must be registered. Add the `IDbReadable` marker to it, or register it explicitly. See [registration](../mapping/registration.md).

### How do I read multiple result sets?

`ExecuteMultiReader`, then `Query<T>` once per set. See [multiple result sets](../running-queries/multiple-results.md).
