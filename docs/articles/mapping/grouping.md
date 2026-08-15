# Group rows into results

A nested collection uses several rows to build one result. Values before the first collection form the default group key.

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, List<Album> Albums);

List<Artist> artists = GetArtists.Query<List<Artist>>(cnn);
```

```text
Id  Name    AlbumsId  AlbumsTitle
1   AC/DC   10        High Voltage
1   AC/DC   11        Let There Be Rock
2   Queen   20        Jazz
```

`Id` and `Name` remain equal for the first two rows, so they build one `Artist`. The third row starts another.

## Choose the key

Use `[GroupKey]` when specific mapped values should decide the boundary.

```csharp
public record Invoice(List<int> LineIds, [GroupKey] int InvoiceId);
```

```text
LineIds  InvoiceId
15       100
16       100
20       101
```

```csharp
List<Invoice> invoices = GetInvoices.Query<List<Invoice>>(cnn);
// Invoice 100 contains lines 15 and 16.
// Invoice 101 contains line 20.
```

Several marked values form one key.

```csharp
public record OrderLine([GroupKey] int OrderId, [GroupKey] int ProductId, List<string> Serials);
```

## Group by a column that is not stored

`[GroupKeyColumns]` can read a boundary column without adding it to the result type.

```csharp
[GroupKeyColumns("CustomerId")]
public record CustomerInvoices(string CustomerName, List<int> InvoiceIds);
```

```sql
SELECT CustomerId, CustomerName, InvoiceId AS InvoiceIds FROM invoices ORDER BY CustomerId
```

```text
CustomerId  CustomerName  InvoiceIds
1           Ada           10
1           Ada           11
2           Grace         12
```

```csharp
List<CustomerInvoices> customers = GetCustomerInvoices.Query<List<CustomerInvoices>>(cnn);
// customers.Count == 2
```

Rows belonging to the same key must remain consecutive in every form.

## Use a method as the boundary

A group method can decide whether the current row remains with the saved key.

```csharp
public record ShipmentBatch([Alt("ShippedAt")] DateTime Start, List<string> Items) : IDbReadable {

    [GroupKey]
    public static (bool Same, DateTime Next) WithinSevenDays(DateTime saved, DateTime shippedAt) {
        bool same = (shippedAt - saved).TotalDays <= 7;
        return (same, same ? saved : shippedAt);
    }
}
```

```text
ShippedAt   Items
2026-01-01  A
2026-01-03  B
2026-01-12  C
```

```csharp
List<ShipmentBatch> batches = GetShipments.Query<List<ShipmentBatch>>(cnn);
// The first batch contains A and B.
// The second batch contains C.
```

Use `[GroupKeyMethod]` when the method applies to one constructor rather than the whole type.

```csharp
public sealed class ShipmentBatch : IDbReadable {
    [GroupKeyMethod(nameof(WithinSevenDays))]
    public ShipmentBatch(DateTime start, List<string> items) { }

    public static (bool Same, DateTime Next) WithinSevenDays(DateTime saved, DateTime shippedAt) {
        bool same = (shippedAt - saved).TotalDays <= 7;
        return (same, same ? saved : shippedAt);
    }
}
```

## Rule order

A rule on the selected constructor wins over a rule on the type. A type rule wins over inferred grouping.

```csharp
[GroupKeyColumns("Region")]
public sealed class Sale : IDbReadable {
    public Sale([GroupKey] DateTime date, List<decimal> amounts) { }
    public Sale(string region, List<decimal> amounts) { }
}
```

```text
Date constructor    -> groups by Date
Region constructor  -> groups by the Region column
```

## Configure grouping at startup

The same built-in rules can be applied without attributes.

```csharp
public record Playlist(int Id);
public record CustomerSummary(int Id, string Country);
public record ImportRow(int AccountId, string Currency);
public record MonthlyReport(DateTime Date) {
    public static (bool Same, DateTime Next) ByMonth(DateTime saved, DateTime current) => (saved.Year == current.Year && saved.Month == current.Month, current);
}

TypeParsingInfoHelper.SetGroupKey<Playlist>(nameof(Playlist.Id));
TypeParsingInfoHelper.SetGroupKey<CustomerSummary>(nameof(CustomerSummary.Id), nameof(CustomerSummary.Country));
TypeParsingInfoHelper.SetGroupKeyColumns<ImportRow>("AccountId", "Currency");
TypeParsingInfoHelper.SetGroupKeyMethod<MonthlyReport>(nameof(MonthlyReport.ByMonth));
```

Remove a configured rule when the type should return to inference.

```csharp
TypeParsingInfoHelper.ClearGroupKey<Playlist>();
```

## Invalid grouping shapes

A multi-row value with no usable boundary raises `RINKU3002`.

```csharp
public record Report(List<int> Rows, int Total);

Report report = GetReport.Query<Report>(cnn);
// RINKU3002. Total follows the collection and cannot form the inferred key.
```

A named key that does not exist in the result raises `RINKU3003`.

```csharp
[GroupKeyColumns("AccountId")]
public record AccountRows(List<int> Values);

AccountRows rows = GetValuesOnly.Query<AccountRows>(cnn);
// RINKU3003. The result has no AccountId column.
```

Conflicting rule families at the same level raise `RINKU3004`.

```csharp
public sealed class Batch {
    [GroupKeyMethod(nameof(ByWindow))]
    public Batch([GroupKey] int id, List<string> items) { }

    public static (bool Same, int Next) ByWindow(int saved, int current) => (saved == current, current);
}
// RINKU3004. The constructor declares both a parameter key and a method key.
```

See [type registrations and defaults](../customization/type-registration.md) for custom `IGroupingRule` implementations.

[Adapt names when the SQL and code differ](names.md).
