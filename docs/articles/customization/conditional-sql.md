# Conditional SQL handlers

Rinku registers `_N`, `_S`, `_R`, and `_X` through the same handler lists that you can use for your own suffixes.

Register custom suffixes once during application startup, before creating or using commands that contain them.

## Write validated SQL text

An `IQuerySegmentHandler` writes a supplied value into the generated SQL.

```csharp
public enum SortDirection {
    Ascending,
    Descending
}

sealed class SortDirectionHandler : IQuerySegmentHandler {
    public void Handle(ref ValueStringBuilder sql, object value) {
        if (value is not SortDirection direction)
            throw new ArgumentException("A SortDirection value is required.", nameof(value));

        sql.Append(direction == SortDirection.Ascending ? "ASC" : "DESC");
    }
}

QueryFactory.BaseHandlerMapper['D'] =
    _ => new SortDirectionHandler();
```

```csharp
static readonly QueryCommand OrderedAlbums = new("SELECT AlbumId AS Id, Title FROM albums ORDER BY Title @direction_D");

List<Album> albums = OrderedAlbums.Query<List<Album>>(cnn, new { direction = SortDirection.Descending });
```

```sql
SELECT AlbumId AS Id, Title FROM albums ORDER BY Title DESC
```

A custom handler exception propagates unchanged.

```csharp
List<Album> albums = OrderedAlbums.Query<List<Album>>(cnn, new { direction = "DESC" });
// ArgumentException from SortDirectionHandler.
```

Rinku does not replace that exception with `RINKU2003`.

## Write SQL and bind parameters

A `SpecialHandler` owns both generated SQL and database parameter binding.

```csharp
sealed class Utf8Handler : SpecialHandler {
    readonly string name;
    DbParamInfo parameter = TypedDbParamCache.Get(DbType.Binary);

    public Utf8Handler(string name) {
        this.name = name;
        IsCached = true;
    }

    public override bool CanHandle(ref object? value) => value is string;

    public override bool Use(IDbCommand command, ref object? value) {
        if (value is not string text)
            return false;

        return parameter.Use(name, command, Encoding.UTF8.GetBytes(text));
    }

    public override bool Use(DbCommand command, ref object? value) {
        if (value is not string text)
            return false;

        return parameter.Use(name, command, Encoding.UTF8.GetBytes(text));
    }

    public override bool SaveUse(IDbCommand command, ref object? value) {
        if (value is not string text)
            return false;

        object bytes = Encoding.UTF8.GetBytes(text);
        if (!parameter.SaveUse(name, command, ref bytes))
            return false;

        value = bytes;
        return true;
    }

    public override bool Update(IDbCommand command, ref object? current, object? value) {
        if (value is not null and not string)
            return false;

        object? bytes = value is string text
            ? Encoding.UTF8.GetBytes(text)
            : null;

        return parameter.Update(command, ref current, bytes);
    }

    public override void Handle(ref ValueStringBuilder sql, object value) => sql.Append(name);

    public override bool UpdateCache<T>(T getter) {
        if (!getter.TryGetInfo(name, out DbParamInfo learned))
            return false;

        parameter = learned;
        IsCached = learned.IsCached;
        return true;
    }

    public override void ResetCache(DbParamInfo inferred) {
        parameter = inferred;
        base.ResetCache(inferred);
    }
}

SpecialHandler.SpecialHandlerGetter['B'] =
    name => new Utf8Handler(name);
```

```csharp
static readonly QueryCommand SaveBinaryValue = new("INSERT INTO binary_values (Value) VALUES (@value_B)");

int inserted = SaveBinaryValue.Execute(cnn, new { value = "plain text" });
```

```sql
INSERT INTO binary_values (Value) VALUES (@value)
-- @value contains the UTF-8 bytes for plain text.
```

A suffix cannot exist in both the base-handler and special-handler registries. The conflict throws.

## Reset handler-owned parameter strategies

`QueryParameters.Reset()` calls `ResetCache(inferred)` on every special handler, then rebuilds its uncached-index list.

```csharp
public override void ResetCache(DbParamInfo inferred) {
    parameter = inferred;
    base.ResetCache(inferred);
}
```

Use the supplied `inferred` value. It represents the current `DbParameterDefaults.Current.Inferred`, which an application may replace.

The explicit equivalent synchronizes `IsCached` directly.

```csharp
public override void ResetCache(DbParamInfo inferred) {
    parameter = inferred;
    IsCached = inferred.IsCached;
}
```

Reset every owned strategy before updating `IsCached` when one handler owns several.

```csharp
public override void ResetCache(DbParamInfo inferred) {
    firstParameter = inferred;
    secondParameter = inferred;
    base.ResetCache(inferred);
}
```

The [parameter metadata guide](../running-queries/parameter-metadata.md#learn-again) shows the command-level reset.
