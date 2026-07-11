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
| `Get<T>()` | Parse starting at the current row, advancing the reader. `CanContinue` reports whether it is left on an untreated row. |
| `GetAsync<T>(CancellationToken ct = default)` | Async form. |
| `GetCurrentSetParser<T>()` | The current set's parser, to run yourself. `Parse` reads the next row (`CanContinue` reports whether it exists); an `IStepParser<T>` leaves the next `Read` to you. |

## Mixing with manual reading

`MultiReader` is itself a `DbDataReader`, so the raw reader surface (`Read`, `NextResult`, indexers) is available. `Query<T>` does the reading itself and always finishes the set, so parsing a set row by row belongs to `GetCurrentSetParser<T>` and `Get<T>`. Useful when the row's content decides how to parse it.

Every parser starts on the row you are on; they differ on the next row. `GetCurrentSetParser<T>` hands you the parser to run yourself. A parser that takes one row at a time is an `IStepParser<T>`, which every plain row type is; test for it and call `ParseStep` to parse the current row without reading past it, so the next `Read` stays yours. That control is the reason to take the parser; when you do not need it, `Query<T>` reads for you.

```csharp
while (multi.Read()) {
    if (multi.GetInt32(0) == (int)RowKind.Refund)
        refunds.Add(((IStepParser<Refund>)multi.GetCurrentSetParser<Refund>()).ParseStep(multi)!);
    else
        payments.Add(((IStepParser<Payment>)multi.GetCurrentSetParser<Payment>()).ParseStep(multi)!);
}
```

`Get<T>` runs the full parser from the row you are standing on. It reads the next row as it goes, so `CanContinue` reports its state afterwards, `true` when it sits on an untreated row, and the loop runs on that flag instead of calling `Read()` between rows.

```csharp
var reading = multi.Read();
while (reading) {
    (reading, var refund) = multi.Get<Refund>();
    refunds.Add(refund!);
}
```
