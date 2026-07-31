# Custom multi-row types

`List<T>`, `IEnumerable<T>`, and one-dimensional arrays fold across rows on their own.

```csharp
public record ArtistL(int Id, List<Album> Albums);
public record ArtistE(int Id, IEnumerable<Album> Albums);
public record ArtistA(int Id, Album[] Albums);
```

Anything else is registered with `MultiRowTypeParsingInfo`, which folds rows through three constructions, a seed that makes the accumulator, an `Add` that takes one element into it, and a finish that turns it into the result. Each is a `MethodBase`, a constructor, a static factory, or a method, and each may be defined on an open generic and closes to the element type.

A `HashSet` holds the accumulation itself, so the seed is its constructor. The `Add` is `null`, which finds the set's own `Add` by name. The finish is `null` too, since the accumulator already is the result.

```csharp
TypeParsingInfo.AddOrSet(typeof(HashSet<>),
    new MultiRowTypeParsingInfo(typeof(HashSet<>).GetConstructor(Type.EmptyTypes)!, null, null));

public record Article(int Id, string Title, HashSet<string> Tags) : IDbReadable;

// Id | Title | Tags
// 1  | Intro | csharp
// 1  | Intro | dotnet
// 1  | Intro | csharp
// -> Article(1, "Intro", { "csharp", "dotnet" })
```

The same three constructions fold a value that is no collection. The accumulator adds each element and the finish reads the result off it, here a running average. Its element comes from the `Add`'s parameter, so the `Add` is given rather than found.

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

Such a value folds beside a collection on the same rows, each reading its own columns.

```csharp
public record Daily(int Day, Average Temp, List<string> Events);

// Day | Temp | Events
// 5   | 18   | sunrise
// 5   | 22   | noon
// -> Daily(5, Average(20, 2), ["sunrise", "noon"])
```
