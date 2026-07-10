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

`Query<T>` works like everywhere else. `T` picks one row, `List<T>` all rows, `IEnumerable<T>` a stream. Each call advances to the next result set, and non-returning sets are skipped automatically.

The `out DbCommand` is yours. Keep it to read [output parameters](parameter-metadata.md#output-parameters), and dispose it along with the reader. It is assigned synchronously, so the async form composes with `await`.

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
| `Query<T>()` | Parse the current set as `T`, then advance to the next set. |
| `QueryAsync<T>(CancellationToken ct = default)` | Async form. |
| `StreamQueryAsync<T>(bool goToNextResultSet = true, CancellationToken ct = default)` | Stream the current set's rows, advance when enumeration completes. |
| `GetStep<T>()` | Parse one step from the row a manual `Read()` put you on, ending on the step's last row. Throws when the parser must look past its own rows. |
| `GetStepAsync<T>(CancellationToken ct = default)` | Async form. |
| `Get<T>()` | Parse starting at the current row, advancing the reader. `CanContinue` reports whether it is left on an untreated row. |
| `GetAsync<T>(CancellationToken ct = default)` | Async form. |

## Mixing with manual reading

`MultiReader` is itself a `DbDataReader`, so the raw reader surface (`Read`, `NextResult`, indexers) is available. `Query<T>` does the reading itself and always finishes the set, so parsing a set step by step belongs to `GetStep<T>` and `Get<T>`. Useful when the row's content decides how to parse it.

`GetStep<T>` parses one step from the row you are standing on and ends on the step's last row, never looking further. A plain row type is a one row step, so the reader does not move at all. The rows a step takes are decided by the parser alone, which keeps the advance yours, and a parser that must look past its own rows to find its end (`List<T>`, `Single<T>`) throws.

```csharp
while (multi.Read()) {
    if (multi.GetInt32(0) == (int)RowKind.Refund)
        refunds.Add(multi.GetStep<Refund>()!);
    else
        payments.Add(multi.GetStep<Payment>()!);
}
```

`Get<T>` runs the full parser from the row you are standing on. The parser advances the reader, and `CanContinue` reports its state afterwards, `true` when it sits on an untreated row, so the loop runs on that flag instead of calling `Read()` between rows.

```csharp
var reading = multi.Read();
while (reading) {
    (reading, var refund) = multi.Get<Refund>();
    refunds.Add(refund!);
}
```
