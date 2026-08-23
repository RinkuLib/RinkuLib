# Mapping slot rules

## Custom name comparer attribute

```csharp
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
sealed class DbPrefixAttribute : Attribute, INameComparerMaker
{
    public INameComparer MakeComparer(Type type, ref INameComparer current, object[] attributes, object? member)
        => new NameComparer("db_" + current.GetDefaultName());
}
```

```csharp
public record Album([DbPrefix] int Id, string Title);

static readonly QueryCommand GetAlbum = new("SELECT db_Id, Title FROM albums WHERE db_Id = @id");
Album album = GetAlbum.Query<Album>(cnn, new { id = 12 });
```

The attribute changes the name comparer for that mapping slot.

## Null rule extension point

Custom null attributes implement [`INullColHandlerMaker`](xref:Rinku.Mapping.INullColHandlerMaker).

Built in null behavior is shown with concrete examples in [Database NULL](../mapping/nulls.md).

## Column usage extension point

Custom column usage attributes implement [`IUsageFlagModifier`](xref:Rinku.Mapping.IUsageFlagModifier).

Built in usage behavior is shown with concrete examples in [Reading order](../mapping/reading-order.md).

## Combined slot rule

An attribute that replaces several slot properties can implement [`IParamInfoMaker`](xref:Rinku.Mapping.IParamInfoMaker).

[ParamInfo API](xref:Rinku.Mapping.ParamInfo)
