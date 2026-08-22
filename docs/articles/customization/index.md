# Advanced customization

Use this section when the built in mapping, parameter, or SQL rules do not match an application rule.

Start with the normal usage pages before replacing a rule.

```csharp
public readonly record struct DbValue<T>([NoName] T Value) : IDbReadable;
```

The wrapper above uses normal mapping with attributes only. It does not need a custom registration.

Use a registration when the type needs a different mapping rule.

```csharp
public readonly record struct PositionalValue<T>(T Value);

TypeParsingInfo.AddOrSet(typeof(PositionalValue<>), CtorTypeInfo.Instance);
```

Use a parameter strategy when the database value needs conversion.

```csharp
SaveSearch.UpdateParamCache("@names", new NamesParamInfo());
```

Use a SQL handler when a suffix should generate application defined SQL.

```csharp
QueryFactory.BaseHandlerMapper['D'] = _ => new SortDirectionHandler();
```


Use Method Caller when an existing method should be exposed through another delegate signature.

```csharp
Func<SaveAlbumArgs, CancellationToken, Task<int>> save =
    MethodCaller.Create<Func<SaveAlbumArgs, CancellationToken, Task<int>>>(
        method,
        CallerParameter<CancellationToken>.ByType());
```

Use a complete result parser when the requested result type changes how rows are consumed.

```csharp
TypeParser.TypeParserMakers.Insert(0, lastParserMaker);
```

The following pages show one complete usage path for each extension point.

* [Type registrations](type-registration.md)

* [Mapping slot rules](slot-rules.md)

* [Multi row mappings](multi-row.md)

* [Complete result parsers](result-parsers.md)

* [Parameter source rules](parameter-members.md)

* [Parameter binding](parameters.md)

* [Method caller](method-caller.md)

* [Conditional SQL handlers](conditional-sql.md)

* [Cache control](caches.md)
