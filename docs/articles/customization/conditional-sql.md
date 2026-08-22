# Custom conditional SQL

A custom suffix can write validated application controlled SQL text.

This example adds `_D` for a sort direction enum.

```csharp
public enum SortDirection
{
    Ascending,
    Descending
}

sealed class SortDirectionHandler : IQuerySegmentHandler
{
    public void Handle(ref ValueStringBuilder sql, object value)
    {
        if (value is not SortDirection direction)
            throw new ArgumentException("A SortDirection value is required.", nameof(value));

        sql.Append(direction == SortDirection.Ascending ? "ASC" : "DESC");
    }
}
```

Register the suffix during application startup before commands using it are created.

```csharp
QueryFactory.BaseHandlerMapper['D'] = _ => new SortDirectionHandler();
```

Use the registered `_D` suffix in the template.

```csharp
static readonly QueryCommand OrderedAlbums = new("SELECT AlbumId AS Id, Title FROM albums ORDER BY Title @direction_D");

List<Album> albums = OrderedAlbums.Query<List<Album>>(cnn, new
{
    direction = SortDirection.Descending
});
```

```sql
SELECT AlbumId AS Id, Title FROM albums ORDER BY Title DESC
```

The handler receives the supplied value. Exceptions thrown by the handler are returned to the caller unchanged.

```csharp
OrderedAlbums.Query<List<Album>>(cnn, new
{
    direction = "DESC"
});
// ArgumentException from SortDirectionHandler
```

## SQL plus database parameters

Use `SpecialHandler` when the suffix must both change generated SQL and own database parameter binding.

```text
SpecialHandler.SpecialHandlerGetter['B'] =
    name => new Utf8Handler(name);
```

A special handler is responsible for its value conversion, parameter updates, SQL output, and parameter strategy reset.

This is a lower level extension point than `IQuerySegmentHandler`. Use a normal `DbParamInfo` when only the bound database value needs to change.

See [Parameter binding](parameters.md) for that path.
