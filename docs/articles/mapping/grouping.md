# Grouping

Rows fold into a value while its boundary holds, the columns that tell one value from the next. By default the boundary is every value before the first collection. A rule can name it instead, and that rule sits in one of two places, on a construction path or on the type.

## The default boundary

Every value before the first collection is the boundary. When it changes, a new value begins.

```csharp
public record Regional(int Region, List<decimal> Amounts) : IDbReadable;
Regional first = GetSales.Query<Regional>(cnn);

// Region | Amounts
// 1      | 9.99
// 1      | 4.00
// 2      | 5.00
// -> Regional(1, [9.99, 4.00]), the read stopping at the 2
```

A value after the first collection is not part of the boundary. It is read once, from the value's first row.

```csharp
public record Basket(int Id, List<int> Items, decimal Total);

// Id | Items | Total
// 1  | 10    | 14.00
// 1  | 4     | 14.00
// -> Basket(1, [10, 4], 14.00)
```

With nothing before the first collection, every row folds into one value.

```csharp
public record Pair(List<int> Numbers, List<string> Words) : IDbReadable;

// Numbers | Words
// 1       | a
// 2       | b
// -> Pair([1, 2], ["a", "b"])
```

A value after a collection with nothing before it has no boundary to infer, and the build throws `MissingGroupBoundary`.

```csharp
public record Report(List<int> Rows, int Total);
// throws MissingGroupBoundary, nothing tells one report from the next
```

## A key on a construction

`[GroupKey]` on a constructor parameter names the boundary for that construction. It reads that parameter's column and can sit anywhere, so a key after the collection reads a layout the default rejects.

```csharp
public record Statement(List<int> Lines, [GroupKey] int AccountId) : IDbReadable;

// Lines | AccountId
// 10    | 1
// 11    | 1
// 20    | 2
// -> Statement([10, 11], 1), Statement([20], 2)
```

Marking several parameters names a composite. It narrows the boundary to the columns you mark, so an unmarked value before the collection is read once rather than compared. Here `Region` and `Day` group and `Rate` stays out.

```csharp
public record Line(int Sku, int Qty) : IDbReadable;
public record Sale([GroupKey] int Region, [GroupKey] int Day, decimal Rate, List<Line> Lines);

// Region | Day | Rate | LinesSku | LinesQty
// 1      | 5   | 1.02 | 400      | 2
// 1      | 5   | 1.03 | 401      | 1
// 1      | 6   | 1.05 | 402      | 5
// -> Sale(1, 5, 1.02, [two lines]), Sale(1, 6, 1.05, [one line])
```

## A key on the type

A rule on the type applies whichever construction is chosen. `[GroupKeyColumns]` identifies the columns to group by, matching them by name.

```csharp
[GroupKeyColumns("Number")]
public record Account(string Holder, List<int> Entries);

// Number | Holder | Entries
// 1      | Ada    | 10
// 1      | Ada    | 11
// 2      | Bo     | 20
// -> Account(Ada) with [10, 11], Account(Bo) with [20]
```

`[GroupKey]` on a member identifies the grouping column.

```csharp
public class Account : IDbReadable {
    [GroupKey]
    public int Number { get; set; }
    public string Holder { get; set; }
    public List<int> Entries { get; set; }
}

// Number | Holder | Entries
// 1      | Ada    | 10
// 1      | Ada    | 11
// 2      | Bo     | 20
// -> Account with Number=1, Holder="Ada", Entries=[10, 11]
//    Account with Number=2, Holder="Bo", Entries=[20]
```

Several `[GroupKey]` members compose a composite key.

```csharp
public class Sale : IDbReadable {
    [GroupKey]
    public int Region { get; set; }
    [GroupKey]
    public int Day { get; set; }
    public decimal Rate { get; set; }
    public List<int> Amounts { get; set; }
}

// Region | Day | Rate | Amounts
// 1      | 5   | 1.02 | 100
// 1      | 5   | 1.03 | 200
// 1      | 6   | 1.05 | 300
// -> Sale with Region=1, Day=5 groups first two rows
//    Sale with Region=1, Day=6 groups third row
```

`[Alt]` on a member matches an alternate column name.

```csharp
public class Account : IDbReadable {
    [GroupKey]
    [Alt("AccountNumber")]
    public int Number { get; set; }
    public string Holder { get; set; }
}

// AccountNumber | Holder
// 1             | Ada
// 1             | Ada
// 2             | Bo
// -> Number matches "AccountNumber" column, groups by it
```

## Method boundaries

A boundary is an implementation, and equality is the built-in one. A static method is another, using its own logic to decide the boundary. The method returns whether the value continues into the same group (`Same`) and the key to carry to the next row (`Next`), its parameters after the stored key negotiated like any reader.

```csharp
static (bool Same, TKey Next) Method(TKey stored, ...negotiated readers)
```

A method boundary can be marked on the type with `[GroupKey]` or on a construction with `[GroupKeyMethod(name)]` — the same rule, stored and resolved differently.

On the type:

```csharp
public class SessionGroup : IDbReadable {
    public SessionGroup(DateTime sessionDate, List<int> values) {
        SessionDate = sessionDate;
        Values = values;
    }
    public DateTime SessionDate { get; }
    public List<int> Values { get; }
    [GroupKey]
    public static (bool Same, DateTime Next) BySessionDate(DateTime previous, DateTime sessionDate)
        => (sessionDate == previous, sessionDate);
}

// SessionDate | Values
// 2026-07-30  | 10
// 2026-07-30  | 11
// 2026-07-31  | 20
// -> SessionGroup(2026-07-30, [10, 11]), SessionGroup(2026-07-31, [20])
```

On a construction, naming the method:

```csharp
public class DailyReport : IDbReadable {
    [GroupKeyMethod(nameof(ByDate))]
    public DailyReport(DateTime date, List<int> readings) {
        Date = date;
        Readings = readings;
    }
    public DateTime Date { get; }
    public List<int> Readings { get; }
    public static (bool Same, DateTime Next) ByDate(DateTime previous, DateTime date) => (date == previous, date);
}
```

The method's parameters after the stored key are negotiated readers, so they support the full `INameComparer` infrastructure — `[Alt]` to match alternate column names, just like members do.

```csharp
public class Report : IDbReadable {
    public Report(List<int> values) { Values = values; }
    public List<int> Values { get; }
    [GroupKey]
    public static (bool Same, int Next) BySourceId(int previous, [Alt("SourceKey")] int sourceId)
        => (sourceId == previous, sourceId);
}

// SourceKey | Values
// 1         | 10
// 1         | 11
// 2         | 20
// -> Report([10, 11]), Report([20])
```

A rule you write yourself is an `IGroupingRule`, set through [runtime configuration](#setting-the-boundary-at-runtime) or made by your own attribute like the built-in ones.

## Which rule wins

The boundary is chosen per construction path, most specific first:

1. The chosen construction's own key, its `[GroupKey]` parameters or a `[GroupKeyMethod]`.
2. The type's key, a `[GroupKey]` member or a static `[GroupKey]` method.
3. The chosen construction's default, the values before its first collection.

A type with two constructions groups by whichever the result's columns select. Neither here marks a key, so each falls to its own default.

```csharp
public class Route : IDbReadable {
    public Route(int Line, List<int> stops) {
        Key = Line;
        Stops = stops;
    }
    public Route(int From, int To, List<int> stops) {
        Key = From * 1000 + To;
        Stops = stops;
    }
    public int Key { get; }
    public List<int> Stops { get; }
}

// A Line column selects the first construction, grouping by Line:
// Line | Stops -> Route(Key 1, [10, 11]), Route(Key 2, [20])

// From and To select the second, grouping by both:
// From | To | Stops -> Route(Key 1005, [10, 11]), Route(Key 1006, [20])
```

A type-level rule can be overridden on a specific construction path. Here, the type groups by `Region`, but the first construction groups by `Date` instead.

```csharp
[GroupKeyColumns("Region")]
public class Sale : IDbReadable {
    public Sale([GroupKey] DateTime date, List<int> amounts) {
        Date = date;
        Region = null!;
        Amounts = amounts;
    }
    public Sale(string region, List<int> amounts) {
        Date = default;
        Region = region;
        Amounts = amounts;
    }
    public DateTime Date { get; }
    public string Region { get; }
    public List<int> Amounts { get; }
}

// When Date is available, the first construction is chosen and groups by Date (path rule overrides type rule):
// Date       | Amounts
// 2026-07-30 | 100
// 2026-07-30 | 200
// 2026-07-31 | 300
// -> Sale(2026-07-30, [100, 200]), Sale(2026-07-31, [300])

// When only Region is available, the second construction is chosen and groups by Region (uses type rule):
// Region | Amounts
// West   | 100
// West   | 200
// East   | 300
// -> Sale(West, [100, 200]), Sale(East, [300])
```

A construction carries parameter keys or a method reference, never both, and a type carries member keys or a method, never both. A parameterless constructor has no parameters to mark, so it cannot override the type-level rule and requires the type to have one. If the type has no grouping rule, negotiation fails.

The key is negotiated apart from the construction, so a construction can map every parameter and still fail. A key naming a column the result does not carry throws `GroupKeyUnmapped`, even when the construction itself was satisfiable.

## Setting the boundary at runtime

A type's boundary can be set before its parser is built, without changing the type. The forms taking an `IGroupingRule` take a rule of your own or a built-in one built by hand, and the last sets it on one construction path by its parameter types rather than on the type.

```csharp
TypeParsingInfoHelper.SetGroupKey<Artist>(nameof(Artist.Id));                     // an equality key
TypeParsingInfoHelper.SetGroupKey<Sale>(nameof(Sale.Region), nameof(Sale.Day));   // composite
TypeParsingInfoHelper.SetGroupKeyMethod<Window>(nameof(Window.WithinFive));       // a method boundary
TypeParsingInfoHelper.SetGroupKey<Artist>(customRule);                            // a rule of your own
TypeParsingInfoHelper.SetGroupKey<Artist>(customRule, typeof(int), typeof(List<Album>)); // on one construction path
```

## Errors

- `MissingGroupBoundary`, a value sits after a collection with no boundary to infer.
- `GroupKeyUnmapped`, a declared key names a column the result does not carry.
- `ConflictingGroupKey`, a member key and a method key on one type, or parameter keys and a method reference on one construction.
