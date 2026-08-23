# Complete result parsers

## Reuse the single value wrapper parsers

```csharp
public readonly record struct Maybe<T>(T? Value) : IWrapping<Maybe<T>, T> where T : class
{
    public static Maybe<T> Make(T value) => new(value);
}

var maybeMaker = new ReusingBaseTypeParserMaker(
    [typeof(Maybe<>)],
    (definition, itemType, ref _) => typeof(OptionalTypeParser<,>).MakeGenericType(definition.MakeGenericType(itemType), itemType),
    (definition, itemType, ref _) => typeof(FastOptionalTypeParser<,>).MakeGenericType(definition.MakeGenericType(itemType), itemType));

TypeParser.TypeParserMakers.Insert(0, maybeMaker);
```

```csharp
Maybe<Album> album = GetAlbums.Query<Maybe<Album>>(cnn);
```

[`IWrapping<TSelf, T>`](xref:Rinku.IWrapping`2) provides the value construction used by the wrapper parsers.

## Last value

```csharp
public readonly record struct Last<T>(T Value);

public sealed class LastParser<T>(ITypeParser<T> inner) : BaseTypeParser<Last<T>>
{
    public override CommandBehavior Behavior => inner.Behavior & ~CommandBehavior.SingleRow;

    public override bool CanParse(ColumnInfo[] schema) => inner.CanParse(schema);

    public override Last<T> Default() => throw new RinkuNoRowsException();

    public override (bool CanContinue, Last<T> Result) Parse(DbDataReader reader)
    {
        (bool more, T value) = inner.Parse(reader);

        while (more)
            (more, value) = inner.Parse(reader);

        return (false, new Last<T>(value));
    }

    public override async ValueTask<(bool CanContinue, Last<T> Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default)
    {
        (bool more, T value) = await inner.ParseAsync(reader, ct);

        while (more)
            (more, value) = await inner.ParseAsync(reader, ct);

        return (false, new Last<T>(value));
    }
}
```

Register the complete result parser maker during setup.

```csharp
var lastParserMaker = new ReusingBaseTypeParserMaker(
    [typeof(Last<>)],
    (definition, itemType, ref _) => typeof(LastParser<>).MakeGenericType(itemType));

TypeParser.TypeParserMakers.Insert(0, lastParserMaker);
```

```csharp
static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId");

Last<Album> last = GetAlbums.Query<Last<Album>>(cnn);
Album album = last.Value;
```

`Album` still uses its mapping. `Last<T>` changes how complete `T` values are consumed.

## Schema compatibility

```csharp
public override bool CanParse(ColumnInfo[] schema) => inner.CanParse(schema);
```

The cached parser reports which returned schemas it can parse.

## Registration change after a parser was cached

```csharp
GetAlbums.InvalidateParsers(QueryParserInvalidationScope.Global);
```

[Cache control](caches.md)

## Reader holding result parsers

Custom streamed results use [`IReaderHoldingParser<T>`](xref:Rinku.Mapping.Parsers.IReaderHoldingParser`1). Reader completion behavior is represented by [`IReaderDone`](xref:Rinku.Mapping.Parsers.IReaderDone).
