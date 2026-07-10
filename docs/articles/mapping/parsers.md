# Parsers

A parser reads a result into one `T`, a single object, a `List<T>`, or an `Optional<T>`. It drives the reader and declares the `CommandBehavior` the executor uses, so a streaming parser opts its query into streaming on its own. The result shapes are themselves parsers, and you can add your own.

## Selection

When a parser is needed for a `T` and a schema, the engine walks `TypeParser.TypeParserMakers` in order. The first maker that claims `T` builds it, and `DefaultTypeParserMaker`, the object parser, catches the rest. The [result shapes](../running-queries/result-shapes.md) are entries in that list. `List<T>` and `IEnumerable<T>` drive the reader row by row, `Optional<T>` and `Single<T>` add their zero and one row rules, each over the element parser.

## Add a result shape

<a id="adding-a-result-shape"></a>Adding a shape of your own is how `List<T>` itself is built, and the reason to reach for a parser. It decides how the rows become a value.

A parser is called with the reader on the first row to parse and advances the reader as it goes. `CanContinue` reports the reader's state on return, `true` when it is left on an untreated row. The element parser follows the same contract, so a shape that gathers rows loops on the element's flag instead of calling `Read` itself.

To gather rows into a `HashSet<T>`, a shape the engine does not ship, write a parser over the element parser, strip `SingleRow` so every row is read, return the empty set on no rows, and add one element per iteration.

```csharp
public sealed class HashSetParser<T>(ITypeParser<T> element) : BaseTypeParser<HashSet<T>> {
    public override CommandBehavior Behavior => element.Behavior & ~CommandBehavior.SingleRow;
    public override HashSet<T> Default() => [];                                    // no rows
    public override (bool CanContinue, HashSet<T> Result) Parse(DbDataReader reader) {
        var set = new HashSet<T>();
        bool canContinue;
        do {
            (canContinue, var item) = element.Parse(reader);                       // the element advances the reader
            set.Add(item);
        } while (canContinue);
        return (false, set);                                                       // no row left
    }
    public override async ValueTask<(bool CanContinue, HashSet<T> Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default) {
        var set = new HashSet<T>();
        bool canContinue;
        do {
            (canContinue, var item) = await element.ParseAsync(reader, ct);
            set.Add(item);
        } while (canContinue);
        return (false, set);
    }
}
```

Register it against its generic definition. `ReusingBaseTypeParserMaker` builds the element parser and hands it to the constructor, so `HashSet<T>` maps for any `T`.

```csharp
// before the first query, ahead of the defaults
TypeParser.TypeParserMakers.Insert(0, new ReusingBaseTypeParserMaker(
    [typeof(HashSet<>)],
    (def, item, ref _) => typeof(HashSetParser<>).MakeGenericType(item)));

HashSet<Track> unique = cnn.Query<HashSet<Track>>(
    "SELECT DISTINCT TrackId AS Id, Name FROM playlist_track");
```

`ReusingBaseTypeParserMaker` is one `ITypeParserMaker`. Implement the interface yourself for a shape that is not a generic wrapper, or that builds its inner parser some other way.

## Getting a parser

The makers run behind `TypeParser.GetTypeParser<T>(ref cols)`, which builds a parser for a schema and caches it. That cache is a linear scan kept to hold memory down, not for speed, so a lookup per query is slow. Run commands through a cache that keeps the parser after first use instead. The usage side, and how to hand a parser to a `DbCommand`, is on [any DbCommand](../running-queries/direct-dbcommand.md).
