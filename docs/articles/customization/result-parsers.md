# Complete-result parsers

An `ITypeParser<T>` controls behavior around the complete query result. Use it when a wrapper changes zero-result handling, cardinality, row advancement, or deferred execution.

## Return the last complete result

This wrapper consumes the result and keeps its final mapped value.

```csharp
public readonly record struct Last<T>(T Value);
```

The inner parser still decides how one complete `T` is mapped. One `T` may consume several rows.

```csharp
public sealed class LastParser<T>(ITypeParser<T> inner)
    : BaseTypeParser<Last<T>> {

    public override CommandBehavior Behavior => inner.Behavior & ~CommandBehavior.SingleRow;

    public override bool CanParse(ColumnInfo[] schema) => inner.CanParse(schema);

    public override Last<T> Default() => throw new RinkuNoRowsException();

    public override (bool CanContinue, Last<T> Result) Parse(DbDataReader reader) {
        (bool more, T value) = inner.Parse(reader);

        while (more)
            (more, value) = inner.Parse(reader);

        return (false, new Last<T>(value));
    }

    public override async ValueTask<(bool CanContinue, Last<T> Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default) {
        (bool more, T value) = await inner.ParseAsync(reader, ct);

        while (more)
            (more, value) = await inner.ParseAsync(reader, ct);

        return (false, new Last<T>(value));
    }
}
```

Removing `CommandBehavior.SingleRow` allows the provider to return every result needed by `Last<T>`. `Default()` handles the zero-result case.

## Register a wrapper around one inner type

`ReusingBaseTypeParserMaker` resolves the one generic inner parser and passes it to the wrapper parser.

```csharp
TypeParser.TypeParserMakers.Insert(0, new ReusingBaseTypeParserMaker([typeof(Last<>)], (definition, itemType, ref _) => typeof(LastParser<>).MakeGenericType(itemType)));
```

```csharp
Last<Album> last = GetAlbums.Query<Last<Album>>(cnn);
```

```sql
SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId
```

The complete-result wrapper resolves a parser for its inner `Album`. `Album` does not need a nested-type marker for this root request.

## Handle another wrapper shape

Use `ITypeParserMaker` directly when the wrapper does not contain exactly one reusable generic inner parser.

```csharp
public sealed record AppResult(string Value);

public sealed class AppResultParserMaker(ITypeParser<AppResult> appParser) : ITypeParserMaker {

    public bool CanHandle<T>() => typeof(T) == typeof(AppResult);

    public bool TryMakeParser<T>(INullColHandler nullColHandler, ColumnInfo[] columns, [MaybeNullWhen(false)] out ITypeParser<T> parser) {
        if (appParser is ITypeParser<T> typed) {
            parser = typed;
            return true;
        }

        parser = null;
        return false;
    }

    public bool TryColdStart<T>(DbCommand command, ICacheGivingParser<T> cache, bool disposeCommand, [MaybeNullWhen(false)] out T result) {
        result = default;
        return false;
    }

    public bool TryColdStart<T>(IDbCommand command, ICacheGivingParser<T> cache, bool disposeCommand, [MaybeNullWhen(false)] out T result) {
        result = default;
        return false;
    }

    public bool TryColdStartAsync<T>(DbCommand command, ICacheGivingParser<T> cache, bool disposeCommand, CancellationToken ct, [MaybeNullWhen(false)] out Task<T> result) {
        result = null;
        return false;
    }

    public bool TryColdStartAsync<T>(IDbCommand command, ICacheGivingParser<T> cache, bool disposeCommand, CancellationToken ct, [MaybeNullWhen(false)] out Task<T> result) {
        result = null;
        return false;
    }
}

TypeParser.TypeParserMakers.Insert(0, new AppResultParserMaker(appResultParser));
```

`TypeParser.DefaultTypeParserMaker` remains the fallback after registered makers have declined the requested type.

## Accept compatible schemas

The global parser cache asks whether an existing parser accepts a schema.

```csharp
public override bool CanParse(ColumnInfo[] schema) => inner.CanParse(schema);
```

A parser that reads the live schema safely may accept every schema.

```csharp
public override bool CanParse(ColumnInfo[] schema) => true;
```

Return true only when both `Parse` and `ParseAsync` can safely handle those columns.

## Register before parser creation

Add parser makers during application startup.

```csharp
TypeParser.TypeParserMakers.Insert(0, lastParserMaker);
```

Parsers already cached before a maker change remain unchanged.

```csharp
GetAlbums.InvalidateParsers(QueryParserInvalidationScope.Global);
```

Invalidate affected parsers only when changing parser makers after queries have already run. See [cache ownership](caches.md) for the global invalidation modes.
