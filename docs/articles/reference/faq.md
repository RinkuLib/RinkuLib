# FAQ

*Common questions and gotchas.*

### Do I create a `QueryCommand` per call?

No. Create it **once** (a `static readonly` field is ideal) and reuse it. Parsing happens at construction, and reusing the command is what makes the caches pay off.

```csharp
static readonly QueryCommand GetTrackById = new("SELECT TrackId AS Id, Name FROM tracks WHERE TrackId = @id");

// reuse on every call, pass the per-call state in
Track t = GetTrackById.Query<Track>(cnn, new { id = 10 });
```

See [core concepts](../getting-started/core-concepts.md).

### A parameter's segment is in the query but I get a provider error.

Standard parameters (`@ID`) are treated as static text by the blueprint. If the segment is kept but you never supplied a value, the **database provider** throws at execution. Mark the variable optional (`?@ID`) if its presence should depend on whether you set it. See the data-versus-presence note in [conditional SQL](../conditional-sql/overview.md).

### How do I avoid `WHERE 1=1`?

You don't need it. Write valid SQL as if every parameter is used, then add `?` to the optional ones. The engine prunes dangling operators, commas, and empty clauses. See [optional variables](../conditional-sql/optional-variables.md).

### How do I read multiple result sets?

Use [`ExecuteMultiReader`](../executing/multi-result.md) and the `MultiReader`'s `Query<T>` and `QueryAll<T>` methods.

### Is it thread-safe to share a `QueryCommand`?

Yes, that is the intended usage. A `QueryCommand` holds only the immutable blueprint (parsed segments, the mapper, parameter registry), so it has **no per-call state** and concurrent calls don't collide on it. The mutable parts are its lazy caches (the parsing cache and the per-type accessor cache). Writes to those are guarded by shared static locks (`QueryCommand.ParsingCacheSharedLock` and `TypeAccessorSharedLock`), and the read path does not lock. Per-call data never lives on the shared command. It is the **parameter object** you pass to each call (or a **builder** when you build state in C#). So the pattern is one shared `QueryCommand` and fresh per-call state.
