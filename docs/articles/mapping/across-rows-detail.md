# Across rows in detail

This page expands on [Across rows](across-rows.md), covering explicit grouping declarations, custom multi-row types, aggregate folds, and runtime configuration.

## Grouping and boundaries

The parser selects the grouping rule in this order:
1. Negotiated parsing path grouping (if provided).
2. Explicit grouping declarations on the type.
3. Default fallback grouping rules.

## Declaring grouping explicitly

When default fallback grouping is insufficient, the parser checks for explicit declarations in this order:

1. The chosen construction's own key: its parameters marked with `[GroupKey]`, or a `[GroupKeyMethod]` naming a boundary method.
2. The type's own key: a member marked with `[GroupKey]`, or a static `[GroupKey]` method.

A construction carries parameter keys or a method reference, never both, and a type carries member keys or a method, never both. Either conflict throws `ConflictingGroupKey`.

Explicit grouping rules establish a boundary regardless of member order. A marked parameter can appear anywhere, successfully resolving layouts that would break default rules.

```csharp
public record Statement(List<int> Lines, [property: GroupKey] int AccountId) : IDbReadable;

// Lines | AccountId
// 10    | 1
// 11    | 1
// 20    | 2
// -> Statement([10, 11], 1), Statement([20], 2)
```

Multiple members can be marked to define complex grouping values.

```csharp
public record Sale(
    [property: GroupKey] int Region,
    [property: GroupKey] int Day,
    List<Line> Lines);
```

## Grouping methods

Column equality isn't always enough to define a boundary. A grouping method lets you define boundaries using custom logic. 

The method must return whether the boundary continues (`Same`) and the state to track for the next row (`Next`).

```csharp
static (bool Same, TKey Next) Method(TKey previous, ...currentRowColumns)
```

```csharp
public record Window(List<int> Readings) : IDbReadable {
    [GroupKey]
    public static (bool Same, int Next) WithinFive(int previous, int reading)
        => (reading - previous <= 5, reading);
}

// Reading: 1, 3, 6, 20, 22

List<Window> windows = GetSensor.Query<List<Window>>(cnn);

// windows[0].Readings == [1, 3, 6]
// windows[1].Readings == [20, 22]
```

A construction can point at a boundary method instead of marking parameters, with `[GroupKeyMethod]`.

```csharp
public class Bucketed : IDbReadable {
    [GroupKeyMethod(nameof(SameTens))]
    public Bucketed(int value, List<int> readings) {
        Value = value;
        Readings = readings;
    }
    public int Value { get; }
    public List<int> Readings { get; }
    public static (bool Same, int Next) SameTens(int stored, int value) => (value / 10 == stored / 10, value);
}
```

## Runtime grouping configuration

You can configure grouping rules globally before building the parser. This lets you set boundaries without modifying target types.

```csharp
TypeParsingInfoHelper.SetGroupKey<Artist>(nameof(Artist.Id));
TypeParsingInfoHelper.SetGroupKey<Sale>(nameof(Sale.Region), nameof(Sale.Day));
TypeParsingInfoHelper.SetGroupKeyMethod<Window>(nameof(Window.WithinFive));
TypeParsingInfoHelper.SetGroupKey<Artist>(customMaker);
```

## Custom collection types

`List<T>`, `IEnumerable<T>`, and one-dimensional arrays work as multi-row types automatically.

```csharp
public record ArtistL([property: GroupKey] int Id, List<Album> Albums);
public record ArtistE([property: GroupKey] int Id, IEnumerable<Album> Albums);
public record ArtistA([property: GroupKey] int Id, Album[] Albums);
```

You can register other collection types with up to three constructions the parser bakes in: one that seeds the accumulator, one that folds an element (or `null` to find its `Add(element)` by name), and one that finishes it into the result (`null` when the accumulator already is the result). Each may be defined on an open generic and closes to the element type. For instance, `HashSet<T>` seeds and finishes as itself and folds with its own `Add`.

```csharp
TypeParsingInfo.AddOrSet(typeof(HashSet<>),
    new MultiRowTypeParsingInfo(typeof(HashSet<>).GetConstructor(Type.EmptyTypes)!, null, null));

public record Article([property: GroupKey] int Id, string Title, HashSet<string> Tags) : IDbReadable;

// Id | Title | Tags
// 1  | Intro | csharp
// 1  | Intro | dotnet
// 1  | Intro | csharp
// -> Article(1, "Intro", { "csharp", "dotnet" })
```

## Folding without a collection

A multi-row type doesn't have to be a collection. The same `MultiRowTypeParsingInfo` folds rows into any value: the seed, the `Add` whose parameter is the element read per row, and the finish that produces the result. For a value that is no collection the element comes from that `Add`, so it is given rather than found.

```csharp
public sealed class Averager {
    double sum;
    int count;

    public void Add(double value) {
        sum += value;
        count++;
    }

    public Average Result() => new(count == 0 ? 0 : sum / count, count);
}

public readonly record struct Average(double Mean, int Count);

TypeParsingInfo.AddOrSet(typeof(Average),
    new MultiRowTypeParsingInfo(
        typeof(Averager).GetConstructor(Type.EmptyTypes)!,
        typeof(Averager).GetMethod(nameof(Averager.Add), [typeof(double)]),
        typeof(Averager).GetMethod(nameof(Averager.Result))));

Average overall = GetScores.Query<Average>(cnn);

// Score: 10, 20, 30
// -> Average(20, 3)
```

Aggregate multi-row values and collections can fold simultaneously from the same grouped rows.

```csharp
public record Daily([property: GroupKey] int Day, Average Temp, List<string> Events);

// Day | Temp | Events
// 5   | 18   | sunrise
// 5   | 22   | noon
// -> Daily(5, Average(20, 2), ["sunrise", "noon"])
```

## Errors

- `MissingGroupBoundary` — A boundary cannot be determined. This happens when default grouping is used, but a multi-row member appears before any single-row members can establish grouping values.
- `GroupKeyUnmapped` — A member explicitly declared in the grouping rule (e.g., using `[GroupKey]`) is missing from the provided result set.
- `ConflictingGroupKey` — Two group key declarations conflict at the same level: a member key and a method key on the type, or parameter keys and a method reference on one construction.
