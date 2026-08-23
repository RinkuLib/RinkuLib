# Custom multi-row mapping

## HashSet

```csharp
ConstructorInfo seed = typeof(HashSet<>).GetConstructor(Type.EmptyTypes)
    ?? throw new InvalidOperationException("HashSet constructor was not found.");
MethodInfo add = typeof(HashSet<>).GetMethod(nameof(HashSet<int>.Add))
    ?? throw new InvalidOperationException("HashSet.Add was not found.");

TypeParsingInfo.AddOrSet(typeof(HashSet<>), new MultiRowTypeParsingInfo(seed, add, null));
```

The added value keeps its mapping.

```csharp
public record Tag(int Id, string Name) : IDbReadable;

static readonly QueryCommand GetTags = new("SELECT TagId AS Id, Name FROM tags ORDER BY TagId");
HashSet<Tag> tags = GetTags.Query<HashSet<Tag>>(cnn);
```

No rows still create the seed value.

```csharp
static readonly QueryCommand FindTags = new("SELECT TagId AS Id, Name FROM tags WHERE TagId < 0");
HashSet<Tag> tags = FindTags.Query<HashSet<Tag>>(cnn);
// tags is empty.
```

The return value of `HashSet<T>.Add` is ignored. The `HashSet<T>` remains the accumulator.

## Accumulator with final conversion

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

```csharp
ConstructorInfo seed = typeof(Averager).GetConstructor(Type.EmptyTypes)
    ?? throw new InvalidOperationException("Averager constructor was not found.");
MethodInfo add = typeof(Averager).GetMethod(nameof(Averager.Add))
    ?? throw new InvalidOperationException("Averager.Add was not found.");
MethodInfo finish = typeof(Averager).GetMethod(nameof(Averager.Finish))
    ?? throw new InvalidOperationException("Averager.Finish was not found.");

TypeParsingInfo.AddOrSet(typeof(Average), new MultiRowTypeParsingInfo(seed, add, finish));
```

```csharp
static readonly QueryCommand GetScores = new("SELECT Score FROM ratings ORDER BY RatingId");
Average average = GetScores.Query<Average>(cnn);
```

## Compose inside another mapping

```csharp
public record Artist(int Id, string Name, [Alt("Tag")] HashSet<Tag> Tags);

static readonly QueryCommand GetArtistsWithTags = new("SELECT ar.ArtistId AS Id, ar.Name, t.TagId AS TagId, t.Name AS TagName FROM artists ar JOIN artist_tags at ON at.ArtistId = ar.ArtistId JOIN tags t ON t.TagId = at.TagId ORDER BY ar.ArtistId");
List<Artist> artists = GetArtistsWithTags.Query<List<Artist>>(cnn);
```

`HashSet<Tag>` keeps the custom multi-row behavior. `Tag` keeps its mapping. The parent boundary still uses the grouping system.

[Grouping](../mapping/grouping.md)
