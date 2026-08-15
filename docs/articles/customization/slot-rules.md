# Mapping slot rules

Every constructor parameter, property, and field has a `ParamInfo`. Smaller extension interfaces can replace its name, null, or column-usage rule without replacing the entire mapping.

## Add another column name

An attribute implementing `INameComparerMaker` can add a naming rule to one slot.

```csharp
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
sealed class DbPrefixAttribute : Attribute, INameComparerMaker {
    public INameComparer MakeComparer(Type type, ref INameComparer current, object[] attributes, object? member) => new NameComparer("db_" + current.GetDefaultName());
}

public record Album([DbPrefix] int Id, string Title);
```

```csharp
static readonly QueryCommand GetAlbum = new("SELECT db_Id, Title FROM albums WHERE db_Id = @id");

Album album = GetAlbum.Query<Album>(cnn, new { id = 12 });
```

```sql
SELECT db_Id, Title FROM albums WHERE db_Id = @id
```

Apply the same rule to every slot created afterward through the global comparer factory.

```csharp
NameComparerFactory shippedNames = ParamInfo.ComparerFactory;

ParamInfo.ComparerFactory = (type, name, altNames, attributes, member, makers) => {
    INameComparer comparer = shippedNames(type, name, altNames, attributes, member, makers);

    return name is null
        ? comparer
        : comparer.AddAltName("db_" + name);
};
```

Configure the factory during application startup.

## Change database NULL handling

An `INullColHandlerMaker` selects a null rule from an attribute.

```csharp
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
sealed class NullAsDefaultAttribute : Attribute, INullColHandlerMaker {
    public INullColHandler MakeColHandler(Type type, string? name, object[] attributes, object? member) => NullableTypeHandle.Instance;
}

public record Stock([NullAsDefault] int Count);
```

```csharp
Stock stock = GetStock.Query<Stock>(cnn);
```

```sql
SELECT NULL AS Count
```

```text
Count = 0
```

## Change column usage

An `IUsageFlagModifier` changes how a slot advances through columns or reuses them.

```csharp
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
sealed class ReusableSequentialAttribute : Attribute, IUsageFlagModifier {
    public void UpdateFlags(object? member, ref UsageFlags flags) => flags |= UsageFlags.CanReuse | UsageFlags.SequentialRead;
}

public record Pair([NoName, ReusableSequential] int First, [NoName] int Second);
```

```csharp
Pair pair = GetPair.Query<Pair>(cnn);
```

```sql
SELECT 7
```

```text
Pair(First: 7, Second: 7)
```

The [reading-order guide](../mapping/reading-order.md) covers the built-in usage attributes.

## Replace the complete slot

Implement `IParamInfoMaker` when one attribute must replace several slot rules together.

```csharp
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
sealed class PositionalReusableAttribute : Attribute, IParamInfoMaker {
    public ParamInfo MakeMatcher(Type type, INullColHandler nulls, INameComparer names, string? name, object[] attributes, UsageFlags flags, object? member)
        => new ParamInfoPlus(type, nulls, NoNameComparer.Instance, FlagUpdater.CanReuseAndSequential, IFallbackParserGetter.Nothing);
}

public record Pair([PositionalReusable] int First, [NoName] int Second);
```

```csharp
Pair pair = GetPair.Query<Pair>(cnn);
```

```sql
SELECT 7
```

```text
Pair(First: 7, Second: 7)
```

Use `ParamInfo.RegistrationInitializer` from [type registrations and defaults](type-registration.md#configure-registrations-created-afterward) when the rule should apply to every slot created later.

