# Mapping slot rules

A mapping slot is a constructor parameter, property, or field.

Use a slot extension when an application needs a naming, null, or column usage rule that the built in attributes do not provide.

## Add a naming rule

This example maps one slot to a database name with a `db_` prefix.

```csharp
[AttributeUsage(
    AttributeTargets.Parameter |
    AttributeTargets.Property |
    AttributeTargets.Field)]
sealed class DbPrefixAttribute : Attribute, INameComparerMaker
{
    public INameComparer MakeComparer(
        Type type,
        ref INameComparer current,
        object[] attributes,
        object? member) =>
        new NameComparer("db_" + current.GetDefaultName());
}
```

Apply the attribute to the slot.

```csharp
public record Album(
    [DbPrefix] int Id,
    string Title);
```

```csharp
static readonly QueryCommand GetAlbum = new(
    "SELECT db_Id, Title FROM albums WHERE db_Id = @id");

Album album = GetAlbum.Query<Album>(cnn, new { id = 12 });
```

```sql
SELECT db_Id, Title FROM albums WHERE db_Id = @id
```

## Other slot rules

Use `INullColHandlerMaker` when an attribute must define a new database `NULL` rule.

Use `IUsageFlagModifier` when an attribute must change sequential reading or column reuse.

Use `IParamInfoMaker` only when one attribute must replace several slot rules together.

The built in alternatives are documented in [Names](../mapping/names.md), [Null handling](../mapping/nulls.md), and [Reading order](../mapping/reading-order.md).
