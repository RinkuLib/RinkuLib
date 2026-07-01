# MultiReader

*Read several result sets from one command.*

`ExecuteMultiReader` returns a `MultiReader`, a `DbDataReader` that also knows the command's possible mappings, so each result set can be parsed to a type. It skips non-returning sets on its own.

`ExecuteMultiReader` hands you the underlying `DbCommand` through an `out` parameter and does **not** dispose it for you, so you can keep it, read output parameters, and so on. Dispose both the reader and the command when done. Like the other execution methods, it sits directly on the `QueryCommand` (a builder has it too).

```csharp
using var multi = batchCmd.ExecuteMultiReader(cnn, out DbCommand cmd);
using (cmd) {
    Artist artist      = multi.Query<Artist>();          // first set, one row (or default)
    List<Album> albums = multi.Query<List<Album>>();     // next set, all rows
}
```

## Methods

| Method | Purpose |
| --- | --- |
| `T? Get<T>()` | Parse the **current** row of the current set. Does not advance. |
| `T? Query<T>(bool goToNextResultSet = true)` | Skip empty sets, parse the current set as `T`, then optionally advance. `T` picks the shape, just as with `QueryCommand`: one row, `List<T>` for all rows, or `IEnumerable<T>` to stream the set. |
| `Task<T?> QueryAsync<T>(bool goToNextResultSet = true, CancellationToken ct = default)` | Async form of `Query<T>`. |
| `IAsyncEnumerable<T> StreamQueryAsync<T>(bool goToNextResultSet = true, CancellationToken ct = default)` | Async stream of the current set's rows, advancing once enumeration completes. |

There is no separate method per shape. `Query<T>` is the same call you use on a `QueryCommand`, and `T` decides whether you get one row, a list, or a stream. Each call advances through the batched result sets in order, reusing the command's schema-keyed parser cache for each one.

The `out` command is assigned synchronously, so it composes with `await` (the `Async` form also takes `out DbCommand cmd`).

```csharp
await using var multi = await batchCmd.ExecuteMultiReaderAsync(cnn, out DbCommand cmd, ct: ct);
await using (cmd) {
    var invoice = await multi.QueryAsync<Invoice>(ct: ct);
    await foreach (var line in multi.StreamQueryAsync<InvoiceLine>(ct: ct))
        Add(line);
}
```
