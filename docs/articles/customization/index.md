# Advanced customization

Rinku includes default attributes and registrations. You can change one rule or replace a larger part of the mapping when needed.

## Change nested or row mapping

A normal wrapper needs no global setup.

```csharp
[method: AreReadable]
public readonly record struct DbValue<T>([NoName] T Value) : IDbReadable;
```

A positional wrapper selects constructor mapping explicitly.

```csharp
public readonly record struct PositionalValue<T>(T Value);

TypeParsingInfo.AddOrSet(typeof(PositionalValue<>), CtorTypeInfo.Instance);
```

A custom `TypeParsingInfo` changes how the type maps both at the root and when nested.

```csharp
TypeParsingInfo.AddOrSet(typeof(LocalDate), new LocalDateTypeParsingInfo());
```

`MultiRowTypeParsingInfo` accumulates one mapped `T` across rows.

```csharp
TypeParsingInfo.AddOrSet(typeof(HashSet<>), new MultiRowTypeParsingInfo(seed, add, finish: null));
```

## Change the complete query result

An `ITypeParserMaker` controls behavior around the complete result, such as zero-result handling or cardinality checks.

```csharp
TypeParser.TypeParserMakers.Insert(0, new ReusingBaseTypeParserMaker([typeof(Last<>)], (definition, itemType, ref _) => typeof(LastParser<>).MakeGenericType(itemType)));
```

## Change application-wide defaults

The registration initializers are process-wide startup configuration.

```csharp
TypeParsingInfo.RegistrationInitializer = (type, info) => info;
MethodCtorInfo.RegistrationInitializer = path => path;
ParamInfo.RegistrationInitializer = slot => slot;
```

Configure them before any query or parser creation. They affect only registrations created afterward.

## Change query values or generated SQL

Parameter member emitters change whether a member is supplied and which value it produces. `DbParamInfo` changes database binding. You can add SQL suffixes alongside `_N`, `_S`, `_R`, and `_X`.

```csharp
SaveNames.UpdateParamCache("@names", new NamesParamInfo());
QueryFactory.BaseHandlerMapper['D'] = _ => new SortDirectionHandler();
```

The examples are under [parameter binding](parameters.md) and [conditional SQL](conditional-sql.md). Use the smallest change that solves the problem.
