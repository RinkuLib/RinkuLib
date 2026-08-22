# Type registrations

A type registration changes how a type is mapped wherever that registration is used.

Use normal object mapping first. Register a custom rule when the type needs a different mapping behavior.

## Positional wrapper

`CtorTypeInfo` maps constructor values in column order.

```csharp
public readonly record struct PositionalValue<T>(T Value);

TypeParsingInfo.AddOrSet(typeof(PositionalValue<>), CtorTypeInfo.Instance);
```

The wrapper can then be used as a result type.

```csharp
static readonly QueryCommand CountAlbums = new("SELECT COUNT(*) FROM albums");

PositionalValue<int> value = CountAlbums.Query<PositionalValue<int>>(cnn);
```

Use `[NoName]` or normal mapping attributes instead when a type can describe its own mapping without a global registration.

## Custom scalar mapping

A `ScalarTypeParsingInfo<T>` can convert a provider scalar into an application type at the root or inside another object.

```csharp
public readonly record struct LocalDate(DateTime Value);

sealed class LocalDateTypeParsingInfo : ScalarTypeParsingInfo<LocalDate>
{
    static readonly MethodInfo ConvertMethod = typeof(LocalDateTypeParsingInfo)
        .GetMethod(nameof(Convert), BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Convert was not found.");

    protected override DbItemPlan? TryCreatePlan(Type targetType, Type parentType, ParamInfo slot, ColumnInfo column, int ordinal)
    {
        if (column.Type != typeof(DateTime))
            return null;

        ITypeConverter converter = new MethodCallConverter(ConvertMethod);

        if (Nullable.GetUnderlyingType(targetType) is not null)
            converter = new NullableWrapperConverter(converter);

        return new ConvertedScalarPlan(parentType, converter, slot.NameComparer.GetDefaultName(), slot.NullColHandler, ordinal);
    }

    static LocalDate Convert(DateTime value) => new(value);
}

TypeParsingInfo.AddOrSet(typeof(LocalDate), new LocalDateTypeParsingInfo());
```

After registration the type can be used normally.

```csharp
public record Event(int Id, LocalDate Date);

Event item = GetEvent.Query<Event>(cnn);
```

```sql
SELECT EventId AS Id, EventDate AS Date FROM events
```

The custom parser accepts `DateTime` columns and converts them into `LocalDate`.

## Open and closed registrations

An exact closed registration has priority over an open generic registration.

```csharp
public readonly record struct Result<T>(T Value);

TypeParsingInfo.GetOrAdd(typeof(Result<>));

TypeParsingInfo.GetOrAdd<Result<int>>(saveAsGenericDefinitionWhenGeneric: false);
```

`Result<int>` uses its exact registration. Other closed `Result<T>` types can use the open registration.

## Application defaults

Registration initializers can modify mappings created later.

```csharp
TypeParsingInfo.RegistrationInitializer = static (type, generated) => generated;

MethodCtorInfo.RegistrationInitializer = static path => path;

ParamInfo.RegistrationInitializer = static slot => slot;
```

Configure these delegates during application startup before queries or parsers are created.

They affect registrations created afterward. Existing registrations remain unchanged.

## Remove a registration

Remove a registration when later parsers should stop using it.

```csharp
bool removed = TypeParsingInfo.TryRemove(typeof(LocalDate), out TypeParsingInfo? previous);
```

Already cached parsers keep their current mapping until they are invalidated.

See [Cache control](caches.md) when a registration is changed after queries already ran.
