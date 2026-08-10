# Custom multi-row types

`List<T>`, `IEnumerable<T>`, and one-dimensional arrays fold across rows on their own.

```csharp
public record ArtistL(int Id, List<Album> Albums);
public record ArtistE(int Id, IEnumerable<Album> Albums);
public record ArtistA(int Id, Album[] Albums);

```

Anything else is registered with `MultiRowTypeParsingInfo`, which folds multiple rows into a single value using a **3-step pipeline**:

1. **Seed (Accumulator Constructor):** Creates an intermediate object that persists while folding rows.
2. **Add Method:** Called for each row to feed data into the accumulator. It is required because its parameter declares the element type read from each row.
3. **Finish Method:** Converts the intermediate accumulator into the final target type. Pass `null` if the intermediate type *is* the final result.

---

### Accumulator matches the final type (`HashSet`)

A `HashSet` serves as its own accumulator. The seed is its constructor, `Add` is its one-element method, and `Finish` is `null` because the set is already the final result.

```csharp
TypeParsingInfo.AddOrSet(typeof(HashSet<>),
    new MultiRowTypeParsingInfo(
        typeof(HashSet<>).GetConstructor(Type.EmptyTypes)!,
        typeof(HashSet<>).GetMethod(nameof(HashSet<int>.Add))!,
        null));

public record Article(int Id, string Title, HashSet<string> Tags) : IDbReadable;

// Id | Title | Tags
// 1  | Intro | csharp
// 1  | Intro | dotnet
// 1  | Intro | csharp
// -> Article(1, "Intro", { "csharp", "dotnet" })

```

---

### Intermediate builder transforms into final type (`Average`)

When the final type cannot accumulate rows directly, use a separate helper class (an accumulator) and a finish method to convert it.

```csharp
public sealed class Averager {
    double sum;
    int count;

    public void AddValue(double value) {
        sum += value;
        count++;
    }

    public Average Result() => new(count == 0 ? 0 : sum / count, count);
}

public readonly record struct Average(double Mean, int Count);

TypeParsingInfo.AddOrSet(typeof(Average),
    new MultiRowTypeParsingInfo(
        typeof(Averager).GetConstructor(Type.EmptyTypes)!,
        typeof(Averager).GetMethod(nameof(Averager.AddValue), [typeof(double)]),
        typeof(Averager).GetMethod(nameof(Averager.Result))));

Average overall = GetScores.Query<Average>(cnn);

// Score: 10, 20, 30
// -> Average(20, 3)

```

The type can then be used directly like `List<T>`:

```csharp
public record Daily(int Day, Average Temp, List<string> Events);

// Day | Temp | Events
// 5   | 18   | sunrise
// 5   | 22   | noon
// -> Daily(5, Average(20, 2), ["sunrise", "noon"])

```
