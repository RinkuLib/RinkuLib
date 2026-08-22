# Group rows into results

Values before the first nested collection form the default group key.

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

The first two rows keep the same parent values and build one `Artist`. The third row starts another.

Rows for one group must stay consecutive.

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

Several `[GroupKey]` values form one compound key.

```csharp
public record OrderLine([GroupKey] int OrderId, [GroupKey] int ProductId, List<string> Serials);
```

Grouping uses `EqualityComparer<T>.Default` for ordinary key values.

## Group by a column that is not stored

Use `[GroupKeyColumns]` when the boundary column should not become a result member.

```csharp
[GroupKeyColumns("CustomerId")]
public record CustomerInvoices(string CustomerName, List<int> InvoiceIds);
```

```sql
SELECT CustomerId, CustomerName, InvoiceId AS InvoiceIds FROM invoices ORDER BY CustomerId
```

`CustomerId` controls grouping without becoming a property on `CustomerInvoices`.

## Use a method as the boundary

A group method can replace normal equality grouping.

```csharp
public record ShipmentBatch([Alt("ShippedAt")] DateTime Start, List<string> Items) : IDbReadable
{
    [GroupKey]
    public static (bool Same, DateTime Next) WithinSevenDays(DateTime saved, DateTime shippedAt)
    {
        bool same = (shippedAt - saved).TotalDays <= 7;
        return (same, same ? saved : shippedAt);
    }
}
```

The method receives saved group state and the current row value. It returns whether the row stays in the group and the next saved state.

Use `[GroupKeyMethod]` when the method belongs to one constructor instead of the whole type.

```csharp
public sealed class ShipmentBatch : IDbReadable
{
    [GroupKeyMethod(nameof(WithinSevenDays))]
    public ShipmentBatch(DateTime start, List<string> items) { }

    public static (bool Same, DateTime Next) WithinSevenDays(DateTime saved, DateTime current)
        => (saved.Date == current.Date, current);
}
```

## Configure grouping during setup

The built in grouping rules can be assigned without attributes.

```csharp
TypeParsingInfoHelper.SetGroupKey<Playlist>(nameof(Playlist.Id));
TypeParsingInfoHelper.SetGroupKey<CustomerSummary>(nameof(CustomerSummary.Id), nameof(CustomerSummary.Country));
TypeParsingInfoHelper.SetGroupKeyColumns<ImportRow>("AccountId", "Currency");
TypeParsingInfoHelper.SetGroupKeyMethod<MonthlyReport>(nameof(MonthlyReport.ByMonth));
```

Remove an assigned rule when the type should return to its declared or inferred behavior.

```csharp
TypeParsingInfoHelper.ClearGroupKey<Playlist>();
```

## Custom grouping rules

Implement `IGroupingRule` only when key values and boundary methods cannot express the required rule.

The custom rule only needs to expose the boundary behavior. The parser and execution state remain owned by Rinku.

See the [API reference](../../api/index.md) for the interface contract. Keep custom grouping setup with the other type registration code described in [advanced type registration](../customization/type-registration.md).

## Invalid grouping shapes

A multi row value with no usable boundary raises `RINKU3002`.

```csharp
public record Report(List<int> Rows, int Total);

Report report = GetReport.Query<Report>(cnn);
// RINKU3002
```

See [collection mapping](collections.md) for the shapes that require a boundary.
