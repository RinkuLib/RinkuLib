# Advanced customization

## Type mapping

```csharp
public readonly record struct PositionalValue<T>(T Value);

TypeParsingInfo.AddOrSet(typeof(PositionalValue<>), CtorTypeInfo.Instance);

static readonly QueryCommand CountAlbums = new("SELECT COUNT(*) FROM albums");
PositionalValue<int> count = CountAlbums.Query<PositionalValue<int>>(cnn);
```

[Type registration](type-registration.md)

## Mapping slot rules

```csharp
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
sealed class DbPrefixAttribute : Attribute, INameComparerMaker
{
    public INameComparer MakeComparer(Type type, ref INameComparer current, object[] attributes, object? member)
        => new NameComparer("db_" + current.GetDefaultName());
}

public record Album([DbPrefix] int Id, string Title);
```

[Mapping slot rules](slot-rules.md)

## Multi-row mapping

```csharp
ConstructorInfo seed = typeof(HashSet<>).GetConstructor(Type.EmptyTypes)
    ?? throw new InvalidOperationException("HashSet constructor was not found.");
MethodInfo add = typeof(HashSet<>).GetMethod(nameof(HashSet<int>.Add))
    ?? throw new InvalidOperationException("HashSet.Add was not found.");

TypeParsingInfo.AddOrSet(typeof(HashSet<>), new MultiRowTypeParsingInfo(seed, add, null));

public record Tag(int Id, string Name) : IDbReadable;

static readonly QueryCommand GetTags = new("SELECT TagId AS Id, Name FROM tags ORDER BY TagId");
HashSet<Tag> tags = GetTags.Query<HashSet<Tag>>(cnn);
```

[Custom multi-row mapping](multi-row.md)

## Complete result parser

[Complete result parser example](result-parsers.md#last-value)

## Other extension points

[Parameter source rules](parameter-members.md)

[Parameter binding](parameters.md)

[Method caller](method-caller.md)

[Custom conditional SQL](conditional-sql.md)

[Cache control](caches.md)
