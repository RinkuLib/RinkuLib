# Complete result parsers

A complete result parser is useful when a result type changes how rows are consumed rather than how one row is mapped.

The example below adds `Last<T>` as a complete result parser.

```csharp
public readonly record struct Last<T>(T Value);

public sealed class LastParser<T>(ITypeParser<T> inner)
    : BaseTypeParser<Last<T>>
{
    public override CommandBehavior Behavior =>
        inner.Behavior & ~CommandBehavior.SingleRow;

    public override bool CanParse(ColumnInfo[] schema) =>
        inner.CanParse(schema);

    public override Last<T> Default() =>
        throw new RinkuNoRowsException();

    public override (bool CanContinue, Last<T> Result) Parse(DbDataReader reader)
    {
        (bool more, T value) = inner.Parse(reader);

        while (more)
            (more, value) = inner.Parse(reader);

        return (false, new Last<T>(value));
    }

    public override async ValueTask<(bool CanContinue, Last<T> Result)> ParseAsync(
        DbDataReader reader,
        CancellationToken ct = default)
    {
        (bool more, T value) = await inner.ParseAsync(reader, ct);

        while (more)
            (more, value) = await inner.ParseAsync(reader, ct);

        return (false, new Last<T>(value));
    }
}
```

Register the wrapper during application startup.

```csharp
var lastParserMaker = new ReusingBaseTypeParserMaker(
    [typeof(Last<>)],
    (definition, itemType, ref _) =>
        typeof(LastParser<>).MakeGenericType(itemType));

TypeParser.TypeParserMakers.Insert(0, lastParserMaker);
```

The result can then be requested like any other result shape.

```csharp
static readonly QueryCommand GetAlbums = new(
    "SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId");

Last<Album> last = GetAlbums.Query<Last<Album>>(cnn);
Album album = last.Value;
```

`Album` still uses its normal row mapping. `Last<T>` only changes how complete `T` values are consumed.

## Schema compatibility

The parser decides whether a cached instance can read another schema.

```csharp
public override bool CanParse(ColumnInfo[] schema) =>
    inner.CanParse(schema);
```

Return `true` only for schemas that both the sync and async parser can read safely.

## Registration changes after queries ran

Register parser makers before queries are used whenever possible.

If a registration changes after a command already cached parsers, invalidate the affected command.

```csharp
GetAlbums.InvalidateParsers(QueryParserInvalidationScope.Global);
```

See [Cache control](caches.md) for the available invalidation scopes.
