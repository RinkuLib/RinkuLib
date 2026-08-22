# Advanced customization

Rinku exposes extension points at the mapping, parameter, SQL, method, and cache boundaries.

| Extension point | Responsibility |
| --- | --- |
| `TypeParsingInfo` | Change how a type is mapped |
| Mapping slot rules | Change which members or construction slots participate |
| Multi-row mapping | Build one value from several rows |

| Extension point | Responsibility |
| --- | --- |
| Complete result parsers | Change how complete results are consumed |
| Parameter source rules | Change how values are discovered from an input object |
| `DbParamInfo` | Change how an application value becomes a database parameter |

| Extension point | Responsibility |
| --- | --- |
| `MethodCaller` | Adapt an existing method to another delegate signature |
| Conditional SQL handlers | Add application-defined conditional SQL suffix behavior |
| Cache control | Replace or invalidate cached metadata and parsers |

## Change type mapping

```csharp
public readonly record struct PositionalValue<T>(T Value);

TypeParsingInfo.AddOrSet(typeof(PositionalValue<>), CtorTypeInfo.Instance);
```

[Type registrations](type-registration.md) · [Mapping slot rules](slot-rules.md)

## Map multiple rows into one value

```csharp
ConstructorInfo seed = typeof(HashSet<>).GetConstructor(Type.EmptyTypes) ?? throw new InvalidOperationException("HashSet constructor was not found.");
MethodInfo add = typeof(HashSet<>).GetMethod(nameof(HashSet<int>.Add)) ?? throw new InvalidOperationException("HashSet.Add was not found.");

TypeParsingInfo.AddOrSet(typeof(HashSet<>), new MultiRowTypeParsingInfo(seed, add, null));
```

[Multi-row mappings](multi-row.md)

## Consume complete results

```csharp
TypeParser.TypeParserMakers.Insert(0, lastParserMaker);
```

[Complete result parsers](result-parsers.md)

## Control parameters

```csharp
SaveSearch.UpdateParamCache("@names", new NamesParamInfo());
```

[Parameter source rules](parameter-members.md) · [Parameter binding](parameters.md)

## Adapt methods

```csharp
Func<SaveAlbumArgs, CancellationToken, Task<int>> save = MethodCaller.Create<Func<SaveAlbumArgs, CancellationToken, Task<int>>>(method, CallerParameter<CancellationToken>.ByType());
```

[Method caller](method-caller.md)

## Add conditional SQL behavior

```csharp
QueryFactory.BaseHandlerMapper['D'] = _ => new SortDirectionHandler();
```

[Conditional SQL handlers](conditional-sql.md)

## Control caches

```csharp
int removed = GetAlbums.InvalidateParsers();
```

[Cache control](caches.md)
