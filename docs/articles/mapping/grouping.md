# Grouping

Rows fold into a value while their grouping rule holds. By default, this rule is inferred from the construction path. An explicit rule can be named instead, sitting either on a construction path or on the type.

A grouping rule is a general boundary strategy. The library includes equality, composite equality, column, and method-based rules, but those are built-in choices, not the complete set of possible rules. A custom rule can use any state and comparison logic that fits the row shape.

## The default rule

Every value before the first multi-row type (like a list, or a custom aggregate) forms the grouping rule. When any of those values change, a new value begins.

Even when querying a single item rather than a list, the engine knows it must read multiple rows to build the result because of that multi-row type, stopping only when the grouping rule breaks.

```csharp
public record Artist(int Id, string Name, List<string> Albums) : IDbReadable;
Artist first = GetArtists.Query<Artist>(cnn);

// Id | Name   | Albums
// 1  | AC/DC  | For Those About To Rock
// 1  | AC/DC  | Let There Be Rock
// 2  | Accept | Restless and Wild
// -> Artist(1, "AC/DC", ["For Those About To Rock", "Let There Be Rock"]), the read stopping at the 2

```

A value after the first multi-row type is not part of the rule. It is read once, from the value's first row.

```csharp
public record Invoice(int Id, List<int> LineIds, decimal Total) : IDbReadable;
var invoice = GetInvoices.Query<List<Invoice>>(cnn);

// Id  | LineIds | Total
// 100 | 15      | 14.00
// 100 | 16      | 13.00 - if the value is different from the first, it will be lost 
// 101 | 17      | 15.00
// -> [Invoice(100, [15, 16], 14.00), Invoice(101, [17], 15.00)]

```

With only multi-row types, every row folds into one value.

```csharp
public record TrackList(List<string> TrackNames, List<decimal> Prices) : IDbReadable;
var trackList = GetTrackList.Query<TrackList>(cnn);

// TrackNames       | Prices
// Breaking The Law | 0.99
// Run to the Hills | 1.99
// -> TrackList(["Breaking The Law", "Run to the Hills"], [0.99, 1.99])

```

A value after a multi-row type with nothing before it has no rule to infer, and the build throws `MissingGroupBoundary`.

```csharp
public record Report(List<int> Rows, int Total);
// throws MissingGroupBoundary, nothing tells one report from the next

```

If the chosen construction path is the parameterless ctor, it requires an explicit rule.

```csharp
public class Report : IDbReadable {
    public Report() { }
    public int ID { get; set; }
    public List<int> Values { get; set; }
}

// Throws an error at build time

```

## Explicit keys

Explicit keys replace the default rule. They are used to fix a shape mismatch or to optimize performance. You can define them on a specific construction path or globally on the type.

On a **construction path**, `[GroupKey]` on a parameter names the column.

```csharp
// Optimization: The default rule would compare every scalar before the list.
// By marking Id, the engine only compares Id, skipping Name, Email, and Phone.
public record Customer([GroupKey] int Id, string Name, string Email, string Phone, List<Invoice> Invoices) : IDbReadable;

// Shape mismatch: The default rule rejects keys placed after a multi-row type. 
// An explicit key allows it.
public record Invoice(List<int> LineIds, [GroupKey] int InvoiceId) : IDbReadable;
var invoices = GetInvoices.Query<List<Invoice>>(cnn);

// LineIds | InvoiceId
// 15      | 100
// 16      | 100
// 20      | 101
// -> [Invoice([15, 16], 100), Invoice([20], 101)]

```

On the **type**, `[GroupKey]` on a property sets a baseline rule for the type, serving as the default unless a specific construction path overrides it. 

It also makes parameterless constructors usable.

```csharp
public class Playlist : IDbReadable {
    [GroupKey]
    public int PlaylistId { get; set; }
    public string Name { get; set; }
    public List<string> Tracks { get; set; }
}
var playlists = GetPlaylists.Query<List<Playlist>>(cnn);

// PlaylistId | Name  | Tracks
// 1          | Heavy | Track A
// 1          | Heavy | Track B
// 2          | Light | Track C
// -> [Playlist(PlaylistId=1, Name="Heavy", Tracks=["Track A", "Track B"]),
//    Playlist(PlaylistId=2, Name="Light", Tracks=["Track C"])]

```

### Application-wide key conventions

Grouping rules belong either to a construction path or to the type-wide fallback. Their registration initializers can
therefore establish global conventions without a separate grouping registry.

Use the construction initializer when the key depends on the chosen constructor or factory:

```csharp
MethodCtorInfo.RegistrationInitializer = static path => {
    var id = path.MethodBase.GetParameters().FirstOrDefault(p => string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase));
    if (id is not null)
        path.GroupKey = new EqualityGroupingRule(id);
};
```

Use the type initializer for a member-based fallback, including parameterless construction completed through members:

```csharp
TypeParsingInfo.RegistrationInitializer = static (type, info) => {
    if (info is ICanUpdateGroupKey grouping && type.GetProperty("Id") is { } id)
        grouping.GroupKey = new EqualityGroupingRule(id);
};
```

A construction key still overrides the type key. Both callbacks run only as lazy registration creates their owners;
they add no grouping comparison or row-reading dispatch of their own.

`[GroupKeyColumns]` identifies the grouping rule by using the columns directly (by using the name).

```csharp
// CustomerId is present in the database result set, but it is not stored 
// as a parameter on the record. It is used strictly to drive the grouping rule.
[GroupKeyColumns("CustomerId")]
public record Customer(string FirstName, string LastName, List<int> InvoiceIds) : IDbReadable;
var customers = GetCustomers.Query<List<Customer>>(cnn);

// CustomerId | FirstName | LastName | InvoiceIds
// 1          | Ada       | Lovelace | 100
// 1          | Ada       | Lovelace | 101
// 2          | Alan      | Turing   | 102
// -> [Customer("Ada", "Lovelace", [100, 101]), 
//    Customer("Alan", "Turing", [102])]

```

## Composite keys

Marking multiple properties or parameters composes a composite key, grouping rows when *all* of those values match.

```csharp
public class OrderItem : IDbReadable {
    [GroupKey]
    public int OrderId { get; set; }
    [GroupKey]
    public int ProductId { get; set; }
    public List<string> SerialNumbers { get; set; }
}
var items = GetOrderItems.Query<List<OrderItem>>(cnn);

// OrderId | ProductId | SerialNumbers
// 50      | 101       | SN-001
// 50      | 101       | SN-002
// 50      | 102       | SN-003
// 51      | 101       | SN-004
// -> [OrderItem(OrderId=50, ProductId=101, SerialNumbers=["SN-001", "SN-002"]),
//    OrderItem(OrderId=50, ProductId=102, SerialNumbers=["SN-003"]),
//    OrderItem(OrderId=51, ProductId=101, SerialNumbers=["SN-004"])]

```

## Alternate column names

You can use `[Alt]` to match a group key property or parameter to a database column with a different name.

```csharp
public class Employee : IDbReadable {
    [GroupKey]
    [Alt("EmployeeId")]
    public int Id { get; set; }
    public List<string> Territories { get; set; }
}
var employees = GetEmployees.Query<List<Employee>>(cnn);

// EmployeeId | Territories
// 7          | North
// 7          | South
// 8          | East
// -> [Employee(Id=7, Territories=["North", "South"]),
//    Employee(Id=8, Territories=["East"])]

```

## A built-in method rule

Equality is the simplest built-in rule. A static method is another built-in rule for boundaries that need custom state. The method returns whether the value continues into the same group (`Same`) and the key to carry to the next row (`Next`). Other rule implementations can use different inputs and state.

Its parameters after the stored key are negotiated like any reader, supporting mapping attributes like `[Alt]`.

```csharp
static (bool Same, TKey Next) Method(TKey stored, ...negotiated readers)

```

A method rule can be placed on a construction path using `[GroupKeyMethod(name)]`, or on the type by marking the static method itself with `[GroupKey]`.

On a **construction path**:

```csharp
public class MonthlySalesReport : IDbReadable {
    [GroupKeyMethod(nameof(ByMonth))]
    public MonthlySalesReport(DateTime month, List<decimal> invoiceTotals) {
        Month = month;
        InvoiceTotals = invoiceTotals;
    }
    
    public DateTime Month { get; }
    public List<decimal> InvoiceTotals { get; }

    public static (bool Same, DateTime Next) ByMonth(DateTime stored, DateTime invoiceDate) 
    {
        var rowMonth = new DateTime(invoiceDate.Year, invoiceDate.Month, 1);
        return (rowMonth == stored, rowMonth);
    }
}
var reports = GetReports.Query<List<MonthlySalesReport>>(cnn);

// InvoiceDate | InvoiceTotals
// 2026-01-15  | 10.99
// 2026-01-22  | 15.00
// 2026-02-05  | 8.99
// -> MonthlySalesReport(2026-01-01, [10.99, 15.00]), MonthlySalesReport(2026-02-01, [8.99])

```

On the **type**:

```csharp
public record ShipmentBatch([Alt("ShippedAt")] DateTime batchStartDate, List<string> items) : IDbReadable {
    [GroupKey]
    public static (bool Same, DateTime Next) WithinBatchWindow(DateTime anchorDate, DateTime shippedAt) {
        const double maxWindowDays = 7;
        if ((shippedAt - anchorDate).TotalDays <= maxWindowDays)
            return (true, anchorDate);
        return (false, shippedAt);
    }
}

```

## Which rule wins

The grouping rule is chosen per construction path, most specific first:

1. **Path explicit:** If there is a grouping rule on the path that is chosen.
2. **Type explicit:** If there is a grouping rule that is set on the type.
3. **Path default:** If no grouping rules are set.

A type with multiple constructions infers a different default rule depending on which columns the database returns.

```csharp
public class Route : IDbReadable {
    public Route(int Line, List<int> stops) { ... }
    public Route(int From, int To, List<int> stops) { ... }
}

// Line column available -> selects first construction, defaults to grouping by Line
// From and To columns available -> selects second construction, defaults to grouping by both

```

A specific construction path can explicitly override a type-level rule.

```csharp
[GroupKeyColumns("Region")]
public class Sale : IDbReadable {
    public Sale([GroupKey] DateTime date, List<int> amounts) { ... }
    public Sale(string region, List<int> amounts) { ... }
}

// Date column available -> first path chosen, overrides type rule to group by Date
// Region column available -> second path chosen, falls back to type rule (Region)

```

Only one compatible rule declaration can be used at a time on the same target (type, or specific path). Declarations that belong to different rule families cannot be combined at that level.

```csharp
public class InvalidBatch : IDbReadable {
    // Throws ConflictingGroupKey: these declarations request incompatible rule families
    [GroupKeyMethod(nameof(ByWindow))]
    public InvalidBatch([GroupKey] int id, List<string> items) { ... }

    public static (bool Same, int Next) ByWindow(int stored, int current) => ...
}

```

## Setting the rule at runtime

Configure grouping before the first parser is built for the type and result shape.
The runtime rule replaces the rule found by attributes.

```csharp
TypeParsingInfoHelper.SetGroupKey<Playlist>(nameof(Playlist.PlaylistId));                      // equality key
TypeParsingInfoHelper.SetGroupKey<CustomerSummary>(nameof(CustomerSummary.CustomerId), "Country"); // composite
TypeParsingInfoHelper.SetGroupKeyColumns<ImportRow>("AccountId", "Currency");                  // columns, no members needed
TypeParsingInfoHelper.SetGroupKeyMethod<MonthlySalesReport>(nameof(MonthlySalesReport.ByMonth)); // method rule

// Get a path, then configure the path itself
var path = TypeParsingInfo.GetOrAdd<Invoice>()
    .GetConstruction(typeof(int), typeof(List<InvoiceLine>));
path.GroupKey = customRule;

// Select the exact path when two factories have the same parameter types
var factory = typeof(Invoice).GetMethod(nameof(Invoice.FromImport))!;
TypeParsingInfo.GetOrAdd<Invoice>().GetConstruction(factory).GroupKey = customRule;

// A path method rule uses the same public rule as [GroupKeyMethod]
var method = typeof(Invoice).GetMethod(nameof(Invoice.SameInvoice))!;
TypeParsingInfo.GetOrAdd<Invoice>()
    .GetConstruction(typeof(int), typeof(List<InvoiceLine>)).GroupKey = new MethodGroupingRule(method);

// Remove an attribute rule and let the normal fallback apply
TypeParsingInfoHelper.ClearGroupKey<Invoice>();
TypeParsingInfo.GetOrAdd<Invoice>().GetConstruction(factory).GroupKey = null;

```

Complete takeover uses the public interfaces. The library does not need to know the rule or the type metadata implementation. This example deliberately wraps the shipped `DefaultTypeParsingInfo` to retain its object-mapping behavior; an independent implementation does not need to do so.

```csharp
public sealed class MyTypeInfo : TypeParsingInfo, ICanUpdateGroupKey {
    private readonly DefaultTypeParsingInfo inner = new(typeof(Invoice));
    public IGroupingRule? GroupKey { get; set; }

    public override void ValidateCanUseType(Type targetType)
        => inner.ValidateCanUseType(targetType);

    public override DbItemPlan? TryGetParser(
        Type currentClosedType,
        RecursiveInfo previousUsages,
        ParamInfo paramInfo,
        ColumnInfo[] columns,
        ColModifier colModifier,
        ref ColumnUsage colUsage,
        MethodCtorInfo.AdditionalFlags callerFlags = default) {
        inner.GroupKey = GroupKey;
        return inner.TryGetParser(currentClosedType, previousUsages, paramInfo, columns,
            colModifier, ref colUsage, callerFlags);
    }
}

var info = new MyTypeInfo();
TypeParsingInfo.AddOrSet<Invoice>(info);
info.SetGroupKey(myRule); // any IGroupingRule implementation can take control
```

## Errors

* `MissingGroupBoundary`, a value sits after a multi-row type with no rule to infer.
* `GroupKeyUnmapped`, a declared key names a column the result does not carry.
* `ConflictingGroupKey`, incompatible grouping declarations on one type or one construction path. For example, two declarations may request different rule families at the same level.
