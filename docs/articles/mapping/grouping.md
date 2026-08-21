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

`Id` and `Name` remain equal for the first two rows, so they build one `Artist`. The third row starts another. Ordinary equality grouping compares each key part with `EqualityComparer<T>.Default`; all parts must remain equal for the row to stay in the group.

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

### Type member keys match the schema

A key marked on a property or field is a type-level grouping option. It is used when every marked name can be read from the returned schema. Otherwise Rinku continues to the default inferred key.

```csharp
public sealed class SoftBatch : IDbReadable {
    [GroupKey, Alt("Key")]
    public int Id { get; }
    public string? Code { get; }
    public List<int> Values { get; }

    public SoftBatch(int key, List<int> values) {
        Id = key;
        Values = values;
    }

    public SoftBatch(string code, List<int> values) {
        Code = code;
        Values = values;
    }
}
```

The schema containing `Key` uses `Id` as its key because `[Alt("Key")]` makes the column match. A schema containing only `Code` cannot use that rule, so normal key inference uses `Code`.

A `[GroupKey]` placed directly on a constructor parameter belongs to that construction. Its rule is tried before the type rule. If it returns no boundary, Rinku tries the type rule and then inference.

An application can install an [Id default](../customization/type-registration.md#configure-an-id-default) through the registration delegates without adding any `Id` behavior to Rinku itself.

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

Method grouping replaces ordinary equality semantics with the method's state and comparison logic.

## Rule order

Rinku tries the selected construction's rule, the type rule, and inferred grouping in that order. Returning a boundary stops the chain. Returning `null` continues it, while throwing refuses the schema.

```csharp
[GroupKeyColumns("Region")]
public sealed class Sale : IDbReadable {
    public Sale([GroupKey] DateTime date, List<decimal> amounts) { }
    public Sale(string region, List<decimal> amounts) { }
}
```

```text
Date constructor    -> Date rule, then Region rule, then inference
Region constructor  -> Region rule, then inference
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

Remove an assigned rule when the type should return to its attribute default, or to inference when it has no attribute rule.

```csharp
TypeParsingInfoHelper.ClearGroupKey<Playlist>();
```

## Custom grouping rule

Implement `IGroupingRule` when neither equality keys nor a boundary method can express the rule. This example groups by a named column while discovering its CLR type from the result schema.

```csharp
sealed class ColumnGroupingRule(string columnName) : IGroupingRule {
    public GroupingBoundary MakeBoundary(Type spanningType, ColumnInfo[] columns, ColModifier modifier, IBoundaryBuild build) {
        int index = Array.FindIndex(columns, item => string.Equals(item.Name, columnName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            throw new InvalidOperationException($"Column {columnName} was not found");

        ColumnInfo column = columns[index];
        var name = ParamInfo.Create(column.Type, columnName, []).NameComparer;
        DbItemPlan reader = GroupKeyNegotiation.NegotiateReader(name, column.Type, columns, modifier, columnName);
        return new EqualityBoundary([(build.Reader(reader, column.Type), build.Field(column.Type))]);
    }
}

public sealed record RegionGroup(int Ordinal, string Region, List<int> Codes) : IDbReadable;

TypeParsingInfoHelper.SetGroupKey<RegionGroup>(new ColumnGroupingRule("Region"));
```

```text
Ordinal  Region  Codes
1        West    10
2        West    11
3        East    20

RegionGroup(1, "West", [10, 11])
RegionGroup(3, "East", [20])
```

`MakeBoundary` runs while the parser is built. `IBoundaryBuild` creates the per-execution key storage, so the rule itself does not keep mutable grouping state. Assign a rule to one construction path with `GetConstruction(...).GroupKey` when it should be tried first for that path.

`MakeBoundary` may return `null` when the schema is not a match, or throw when the mismatch is invalid. Rinku tries a construction rule, a type rule, and finally inferred grouping. The built-in equality rule returns `null` when one of its key columns cannot be mapped.

## Invalid grouping shapes

A multi-row value with no usable boundary raises `RINKU3002`.

```csharp
public record Report(List<int> Rows, int Total);

Report report = GetReport.Query<Report>(cnn);
// RINKU3002. Total follows the collection and cannot form the inferred key.
```

A rule can make an absent key fatal by raising `RINKU3003` instead of returning `null`.

```csharp
throw new RinkuConfigurationException(
    ErrorCodes.GroupKeyUnmapped,
    "the required AccountId key matched no column");
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

[Adapt names when the SQL and code differ](names.md).
