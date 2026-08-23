# Grouping

## Inferred parent values

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, List<Album> Albums);
```

```text
Id  Name    AlbumsId  AlbumsTitle
1   AC/DC   10        High Voltage
1   AC/DC   11        Let There Be Rock
2   Queen   20        Jazz
```

Values mapped before the first nested multi-row value form the default parent boundary. The first two rows remain one `Artist`. The third row starts another.

Rows for one group stay consecutive.

## Explicit key

```csharp
public record Invoice(List<int> LineIds, [GroupKey] int InvoiceId);
```

```text
LineIds  InvoiceId
15       100
16       100
20       101
```

Several key values form one compound key.

```csharp
public record OrderLine([GroupKey] int OrderId, [GroupKey] int ProductId, List<string> Serials);
```

Ordinary key values use `EqualityComparer<T>.Default`.

## Boundary column without a member

```csharp
[GroupKeyColumns("CustomerId")]
public record CustomerInvoices(string CustomerName, List<int> InvoiceIds);

List<CustomerInvoices> customers = cnn.Query<List<CustomerInvoices>>("SELECT CustomerId, CustomerName, InvoiceId AS InvoiceIds FROM invoices ORDER BY CustomerId");
```

`CustomerId` participates in the boundary without becoming a result member.

## Boundary method

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

The method receives saved boundary state and the current row value. It returns whether the row stays in the group and the next saved state.

A construction can carry its own method rule.

```csharp
public sealed class ShipmentBatch : IDbReadable
{
    [GroupKeyMethod(nameof(WithinSameDay))]
    public ShipmentBatch(DateTime start, List<string> items) { }

    public static (bool Same, DateTime Next) WithinSameDay(DateTime saved, DateTime current)
        => (saved.Date == current.Date, current);
}
```

## Setup registration

```csharp
TypeParsingInfoHelper.SetGroupKey<Playlist>(nameof(Playlist.Id));
TypeParsingInfoHelper.SetGroupKey<CustomerSummary>(nameof(CustomerSummary.Id), nameof(CustomerSummary.Country));
TypeParsingInfoHelper.SetGroupKeyColumns<ImportRow>("AccountId", "Currency");
TypeParsingInfoHelper.SetGroupKeyMethod<MonthlyReport>(nameof(MonthlyReport.ByMonth));
```

```csharp
TypeParsingInfoHelper.ClearGroupKey<Playlist>();
```

## Rule order

```csharp
[GroupKeyColumns("Region")]
public sealed class Sale : IDbReadable
{
    public Sale([GroupKey] DateTime date, List<decimal> amounts) { }
    public Sale(string region, List<decimal> amounts) { }
}
```

A rule on the selected construction is tried before the type rule. The type rule is tried before inferred grouping. A custom rule can return no boundary so negotiation continues to the next source.

An application-defined boundary can implement [`IGroupingRule`](xref:Rinku.Mapping.IGroupingRule).

## Missing boundary

```csharp
public record Report(List<int> Rows, int Total);

Report report = cnn.Query<Report>("SELECT RowValue AS Rows, Total FROM report_rows ORDER BY RowNumber");
// RINKU3002 when no usable parent boundary can be negotiated.
```

[Multi-row mapping](collections.md) · [RINKU3002](../reference/errors.md#rinku3002-missing-group-boundary)
