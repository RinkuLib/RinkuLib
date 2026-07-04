# Parsers

The parser is the compiled reader for one result schema and one `T`. This page covers how the engine picks one, how to add your own, and how to drive one directly.

## Parser selection

When a parser is needed, the engine walks `TypeParser.TypeParserMakers` in order and the first maker that handles `T` builds it. `List<>`, `IEnumerable<>`, `Optional<>`, and `Single<>` are entries in that list, each a small wrapper around the element parser. `Optional<>` is a handful of lines: empty on no rows, otherwise wrap the element.

<a id="adding-a-result-shape"></a>Adding your own shape or projection is another entry:

```csharp
public sealed class MoneyParserMaker : ITypeParserMaker {
    public bool CanHandle<T>() => typeof(T) == typeof(Money);

    public bool TryMakeParser<T>(INullColHandler nullColHandler, ColumnInfo[] cols,
                                 [MaybeNullWhen(false)] out ITypeParser<T> parser) {
        var inner = TypeParser<decimal>.GetTypeParser(ref cols, nullColHandler);
        parser = (ITypeParser<T>)(object)new MoneyParser(inner);
        return true;
    }
}

// before the first query, ahead of the defaults
TypeParser.TypeParserMakers.Insert(0, new MoneyParserMaker());
```

The parser you return also declares the `CommandBehavior` the executor uses, so a streaming parser opts the query into streaming on its own.

## Using the engine directly

A parser is an `ITypeParser<T>` built for one schema. The default object parser compiles to a single row-reading function; others do more, `List<T>`'s drives the reader and composes the element parser for every row. Ask for one from any set of columns and drive it yourself.

```csharp
ColumnInfo[] cols = reader.GetColumns();
ITypeParser<Album> parser = TypeParser<Album>.GetTypeParser(ref cols);
Album a = parser.Parse(reader);
```

`TypeParser<T>` keeps a global schema-to-parser cache, and each `QueryCommand` keeps its own, tuned to that command. Holding a parser yourself, as above, skips both. The usage side of this is on [any DbCommand](../running-queries/direct-dbcommand.md).
