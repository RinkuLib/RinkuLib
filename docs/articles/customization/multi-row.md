# Custom multi row mappings

`MultiRowTypeParsingInfo` registers a result type that accumulates one mapped value from each row.

The example below adds `HashSet<T>` as another multi row result shape.

```csharp
ConstructorInfo seed = typeof(HashSet<>).GetConstructor(Type.EmptyTypes) ?? throw new InvalidOperationException("HashSet constructor was not found.");

MethodInfo add = typeof(HashSet<>).GetMethod(nameof(HashSet<int>.Add)) ?? throw new InvalidOperationException("HashSet.Add was not found.");

TypeParsingInfo.AddOrSet(typeof(HashSet<>), new MultiRowTypeParsingInfo(seed, add, null));
```

The added value still needs normal mapping registration.

```csharp
public record Tag(int Id, string Name) : IDbReadable;
```

The registered result type can then be requested directly.

```csharp
static readonly QueryCommand GetTags = new("SELECT TagId AS Id, Name FROM tags ORDER BY TagId");

HashSet<Tag> tags = GetTags.Query<HashSet<Tag>>(cnn);
```

With no rows, the seed creates an empty set.

```csharp
static readonly QueryCommand FindTags = new("SELECT TagId AS Id, Name FROM tags WHERE TagId < 0");

HashSet<Tag> tags = FindTags.Query<HashSet<Tag>>(cnn);
// tags is empty
```

The return value from the add method is ignored. This allows methods such as `HashSet<T>.Add` to return `bool` while the collection remains the accumulator.

## Convert the accumulator

The seed can create a different type when a final conversion is needed.

```csharp
public sealed class Averager
{
    double sum;
    int count;

    public void Add(double value)
    {
        sum += value;
        count++;
    }

    public Average Finish() => new(count == 0 ? 0 : sum / count, count);
}

public readonly record struct Average(double Mean, int Count);
```

Register the seed, add method, and final converter.

```csharp
ConstructorInfo seed = typeof(Averager).GetConstructor(Type.EmptyTypes) ?? throw new InvalidOperationException("Averager constructor was not found.");
MethodInfo add = typeof(Averager).GetMethod(nameof(Averager.Add)) ?? throw new InvalidOperationException("Averager.Add was not found.");
MethodInfo finish = typeof(Averager).GetMethod(nameof(Averager.Finish)) ?? throw new InvalidOperationException("Averager.Finish was not found.");

TypeParsingInfo.AddOrSet(typeof(Average), new MultiRowTypeParsingInfo(seed, add, finish));
```

```csharp
Average average = GetScores.Query<Average>(cnn);
```

A custom multi row type can also be nested inside another mapped object. Normal [grouping](../mapping/grouping.md) decides where one parent ends and the next begins.
