# Advanced customization

Use this section when the database side, the .NET side, or the boundary between them needs a rule that the built-in behavior does not provide.

Start with normal mapping and execution first.

```csharp
public readonly record struct DbValue<T>([NoName] T Value) : IDbReadable;
// Normal mapping is enough; no custom registration is needed.
```

Use a registration when a type needs a different mapping rule.

```csharp
public readonly record struct PositionalValue<T>(T Value);

TypeParsingInfo.AddOrSet(typeof(PositionalValue<>), CtorTypeInfo.Instance);
```

Use a parameter strategy when an application value needs another database representation.

```csharp
SaveSearch.UpdateParamCache("@names", new NamesParamInfo());
```

Use a SQL handler when a suffix should generate application-defined SQL.

```csharp
QueryFactory.BaseHandlerMapper['D'] = _ => new SortDirectionHandler();
```

Use Method Caller when an existing method should be exposed through another delegate signature.

```csharp
Func<SaveAlbumArgs, CancellationToken, Task<int>> save = MethodCaller.Create<Func<SaveAlbumArgs, CancellationToken, Task<int>>>(method, CallerParameter<CancellationToken>.ByType());
```

Use a complete result parser when the requested result type changes how complete results are consumed.

```csharp
TypeParser.TypeParserMakers.Insert(0, lastParserMaker);
```

[Type registrations](type-registration.md) · [Mapping slot rules](slot-rules.md) · [Multi-row mappings](multi-row.md) · [Complete result parsers](result-parsers.md) · [Parameter source rules](parameter-members.md) · [Parameter binding](parameters.md) · [Method caller](method-caller.md) · [Conditional SQL handlers](conditional-sql.md) · [Cache control](caches.md)
