# Custom conditional SQL

## Sort direction suffix

```csharp
public enum SortDirection
{
    Ascending,
    Descending
}

sealed class SortDirectionHandler : IQuerySegmentHandler
{
    public void Handle(ref ValueStringBuilder query, object value)
    {
        if (value is not SortDirection direction)
            throw new ArgumentException("Expected SortDirection", nameof(value));
        query.Append(direction == SortDirection.Ascending ? "ASC" : "DESC");
    }
}
```

```csharp
QueryFactory.BaseHandlerMapper['D'] = _ => new SortDirectionHandler();
```

```csharp
static readonly QueryCommand OrderedAlbums = new("SELECT AlbumId AS Id, Title FROM albums ORDER BY Title @direction_D");

List<Album> albums = OrderedAlbums.Query<List<Album>>(cnn, new { direction = SortDirection.Descending });
// SELECT AlbumId AS Id, Title FROM albums ORDER BY Title DESC
```

Handler exceptions reach the caller.

```csharp
OrderedAlbums.Query<List<Album>>(cnn, new { direction = "DESC" });
// ArgumentException from SortDirectionHandler.
```

## SQL and database parameters

A handler that also owns database parameter binding can derive from [`SpecialHandler`](xref:Rinku.Querying.Parameters.SpecialHandler).

```csharp
sealed class Utf8Handler(string name) : SpecialHandler
{
    readonly DbParamInfo parameter = TypedDbParamCache.Get(DbType.Binary);

    public override bool Use(IDbCommand command, ref object? value)
    {
        if (value is not string text)
            return false;
        return parameter.Use(name, command, Encoding.UTF8.GetBytes(text));
    }

    public override bool Use(DbCommand command, ref object? value)
        => Use((IDbCommand)command, ref value);

    public override bool SaveUse(IDbCommand command, ref object? value)
    {
        if (value is not string text)
            return false;
        object bytes = Encoding.UTF8.GetBytes(text);
        if (!parameter.SaveUse(name, command, ref bytes))
            return false;
        value = bytes;
        return true;
    }

    public override bool Update(IDbCommand command, ref object? current, object? value)
    {
        object? bytes = value is string text ? Encoding.UTF8.GetBytes(text) : null;
        return parameter.Update(command, ref current, bytes);
    }

    public override void Handle(ref ValueStringBuilder sql, object value)
        => sql.Append(name);

    public override bool UpdateCache<T>(T getter) => true;
}

SpecialHandler.SpecialHandlerGetter['B'] = name => new Utf8Handler(name);
```

The special handler owns its value conversion, parameter update, SQL output, and parameter strategy reset.

[Parameter binding](parameters.md)
