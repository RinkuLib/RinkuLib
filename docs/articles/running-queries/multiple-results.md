# Multiple result sets

A command with several selects returns several result sets. `ExecuteMultiReader` reads them in order, each mapped to its own type.

```csharp
static readonly QueryCommand Dashboard = new(
    "SELECT * FROM artists WHERE ArtistId = @id; SELECT * FROM albums WHERE ArtistId = @id");

using var multi = Dashboard.ExecuteMultiReader(cnn, out DbCommand cmd, new { id = 1 });
using (cmd) {
    Artist artist      = multi.Query<Artist>();       // first set
    List<Album> albums = multi.Query<List<Album>>();  // second set
}
```

`Query<T>` works like everywhere else: `T` picks one row, `List<T>` all rows, `IEnumerable<T>` a stream. Each call advances to the next result set, and non-returning sets are skipped automatically.

The `out DbCommand` is yours: keep it to read [output parameters](parameter-metadata.md#output-parameters), and dispose it along with the reader. It is assigned synchronously, so the async form composes with `await`.

```csharp
using var multi = await Dashboard.ExecuteMultiReaderAsync(cnn, out DbCommand cmd, new { id = 1 }, ct: ct);
using (cmd) {
    var invoice = await multi.QueryAsync<Invoice>(ct: ct);
    await foreach (var line in multi.StreamQueryAsync<InvoiceLine>(ct: ct))
        Add(line);
}
```

## Methods

| Method | Purpose |
| --- | --- |
| `Query<T>(bool goToNextResultSet = true)` | Parse the current set as `T`, then advance. Pass `false` to stay on the set. |
| `QueryAsync<T>(bool goToNextResultSet = true, CancellationToken ct = default)` | Async form. |
| `StreamQueryAsync<T>(bool goToNextResultSet = true, CancellationToken ct = default)` | Stream the current set's rows, advance when enumeration completes. |
| `Get<T>()` | Parse the row a manual `Read()` put you on. No initial reading, no advancing. |

## Mixing with manual reading

`MultiReader` is itself a `DbDataReader`, so the raw reader surface (`Read`, `NextResult`, indexers) is available. That is the scenario `Get<T>` exists for: `Query<T>` does the reading itself, so once you have called `Read()` yourself, `Get<T>` parses the row you are standing on. Useful when the row's content decides how to parse it.

```csharp
while (multi.Read()) {
    if (multi.GetInt32(0) == (int)RowKind.Refund)
        refunds.Add(multi.Get<Refund>());
    else
        payments.Add(multi.Get<Payment>());
}
```
