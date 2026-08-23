# Type registration

## Positional constructor mapping

```csharp
public readonly record struct PositionalValue<T>(T Value);

TypeParsingInfo.AddOrSet(typeof(PositionalValue<>), CtorTypeInfo.Instance);

static readonly QueryCommand CountAlbums = new("SELECT COUNT(*) FROM albums");
PositionalValue<int> value = CountAlbums.Query<PositionalValue<int>>(cnn);
```

The registration participates wherever `PositionalValue<T>` is mapped.

## Custom scalar mapping

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

The same registration participates at the root or inside another mapped type.

```csharp
public record Event(int Id, LocalDate Date);

static readonly QueryCommand GetEvent = new("SELECT EventId AS Id, EventDate AS Date FROM events WHERE EventId = @eventId");
Event item = GetEvent.Query<Event>(cnn, new { eventId = 12 });
```

## Open and closed generic registrations

```csharp
public readonly record struct Result<T>(T Value);

TypeParsingInfo open = TypeParsingInfo.GetOrAdd(typeof(Result<>));
TypeParsingInfo integers = TypeParsingInfo.GetOrAdd<Result<int>>(saveAsGenericDefinitionWhenGeneric: false);
```

The exact closed registration is checked before the open generic registration.

```text
Result<int>     exact Result<int>
Result<string>  open Result<>
```

## Registration initializers

Initializers affect registrations created through the registration creation paths after the initializer is installed.

```csharp
ParamInfo.RegistrationInitializer = static slot =>
{
    if (string.Equals(slot.NameComparer.GetDefaultName(), "Id", StringComparison.OrdinalIgnoreCase))
        slot.SetAbortOnNull(true);

    return slot;
};
```

A directly constructed `ParamInfo` does not run that initializer.

```csharp
MethodCtorInfo.RegistrationInitializer = static path =>
{
    if (path.TargetType == typeof(Album))
        path.Flags |= MethodCtorInfo.AdditionalFlags.CanCompleteWithMembers;

    return path;
};
```

A directly constructed `MethodCtorInfo` does not run that initializer. Paths created through `MethodCtorInfo.TryNew` do.

```csharp
TypeParsingInfo.RegistrationInitializer = static (type, generated) =>
{
    if (type == typeof(Playlist) && generated is ICanUpdateGroupKey grouping)
        grouping.GroupKey = new EqualityGroupingRule("Id");

    return generated;
};
```

A generated registration passes through this initializer before publication. A registration supplied directly to `AddOrSet` does not.

## Replace and remove registrations

```csharp
TypeParsingInfo.AddOrSet(typeof(LocalDate), new LocalDateTypeParsingInfo());
bool removed = TypeParsingInfo.TryRemove(typeof(LocalDate), out TypeParsingInfo? previous);
```

Parsers already created from an older registration keep their existing behavior until those parsers are invalidated.

[Cache control](caches.md) · [Construction paths](../mapping/construction-paths.md)
