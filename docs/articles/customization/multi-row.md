# Multi-row mappings

`MultiRowTypeParsingInfo` builds an accumulator, adds one mapped value for each row, and optionally converts the accumulator to the requested type.

## Return the accumulator directly

Register the seed and add operations as explicit `MethodBase` values.

```csharp
ConstructorInfo seed = typeof(HashSet<>).GetConstructor(Type.EmptyTypes) ?? throw new InvalidOperationException("HashSet constructor was not found.");

MethodInfo add = typeof(HashSet<>).GetMethod(nameof(HashSet<int>.Add)) ?? throw new InvalidOperationException("HashSet.Add was not found.");

TypeParsingInfo.AddOrSet(typeof(HashSet<>), new MultiRowTypeParsingInfo(seed, add, null));
```

The accumulator and requested type are both `HashSet<T>`, so a null converter returns the accumulator unchanged.

```csharp
public record Tag(int Id, string Name) : IDbReadable;

static readonly QueryCommand GetTags = new("SELECT TagId AS Id, Name FROM tags ORDER BY TagId");

HashSet<Tag> tags = GetTags.Query<HashSet<Tag>>(cnn);
```

```sql
SELECT TagId AS Id, Name FROM tags ORDER BY TagId
```

With no rows, the seed still creates an empty set.

```csharp
static readonly QueryCommand FindTags = new("SELECT TagId AS Id, Name FROM tags WHERE TagId < 0");

HashSet<Tag> tags = FindTags.Query<HashSet<Tag>>(cnn);
// tags is empty.
```

The return value of the add method is ignored. `HashSet<T>.Add(T)` may therefore return `bool` while the set remains the accumulator.

## Convert another accumulator

The seed may build a type different from the requested result.

```csharp
public sealed class Averager {
    double sum;
    int count;

    public void Add(double value) {
        sum += value;
        count++;
    }

    public Average Finish() => new(count == 0 ? 0 : sum / count, count);
}

public readonly record struct Average(double Mean, int Count);
```

Pass the accumulator constructor, its add method, and its converter explicitly.

```csharp
ConstructorInfo seed = typeof(Averager).GetConstructor(Type.EmptyTypes) ?? throw new InvalidOperationException("Averager constructor was not found.");

MethodInfo add = typeof(Averager).GetMethod(nameof(Averager.Add)) ?? throw new InvalidOperationException("Averager.Add was not found.");

MethodInfo converter = typeof(Averager).GetMethod(nameof(Averager.Finish)) ?? throw new InvalidOperationException("Averager.Finish was not found.");

TypeParsingInfo.AddOrSet(typeof(Average), new MultiRowTypeParsingInfo(seed, add, converter));
```

```csharp
Average average = GetScores.Query<Average>(cnn);
```

```sql
SELECT Score FROM reviews
```

```text
10 | 20 | 30 -> Average(20, 3)
no rows      -> Average(0, 0)
```

## Register the added value separately

The add method's parameter type does not become readable automatically.

```csharp
public record Tag(int Id, string Name);

HashSet<Tag> tags = GetTags.Query<HashSet<Tag>>(cnn);
// RINKU3001: Tag has no registration.
```

Use any normal registration mechanism before the multi-row mapping needs that value.

```csharp
TypeParsingInfo.GetOrAdd<Tag>();

HashSet<Tag> tags = GetTags.Query<HashSet<Tag>>(cnn);
```

The added type can instead register itself with the marker interface.

```csharp
public record Tag(int Id, string Name) : IDbReadable;
```

## Accumulate inside another object

The same registration works when the multi-row value is nested.

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, HashSet<Album> Albums);

static readonly QueryCommand GetArtists = new(
    "SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumsId, al.Title AS AlbumsTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId");

List<Artist> artists = GetArtists.Query<List<Artist>>(cnn);
```

```sql
SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumsId, al.Title AS AlbumsTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId
```

Each `Artist` grouping boundary finishes its current `HashSet<Album>` and begins another. The [grouping guide](../mapping/grouping.md) covers inferred and explicit boundaries.
