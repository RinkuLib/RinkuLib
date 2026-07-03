# Matching and parsers

The features in this section are default implementations plugged into a few fixed points. This page walks those points, from the most local to the broadest, and shows what handing in your own looks like. The built-ins you have already used are the examples.

## Name matching

Each slot holds its candidate names in an `INameComparer`, always matched as `[prefix] + [candidate]`. The attributes adjust the candidates:

- `[Alt("Name")]` adds an alternative name.
- `[AltSkippingSegments("Name", n)]` adds an alternative that skips `n` prefix segments, letting a deep slot match a higher-level column.
- `[AltUpTo("Name", "Key")]` skips prefix segments back up to the named segment.
- `[NoName]` drops name matching, position and type only.

```csharp
public record LayerOne(int First, LayerTwo Two);
public record LayerTwo([AltUpTo("NotTooDeep", "Two")] int Second, LayerThree Three) : IDbReadable;
public record LayerThree([AltUpTo("SuperDeep", "Two")] int Third, [AltUpTo("SemiDeep", "Three")] int Deep) : IDbReadable;

// Columns: First | NotTooDeep | SuperDeep | TwoSemiDeep
// Second matches NotTooDeep (prefix rewound to "Two"), Third matches SuperDeep,
// Deep matches TwoSemiDeep (rewound to after "Three").
```

`[NoName]` is how the [result shape](../running-queries/result-shapes.md) wrappers wrap their inner value without inventing a column name. Your own single-value wrapper does the same:

```csharp
public readonly struct Boxed<T>([NoName] T value) { public readonly T Value = value; }
```

## Bulk configuration

`TypeParsingInfo` applies settings across a type by slot name, without attributes. Useful when the type is not yours to annotate.

```csharp
var info = TypeParsingInfo.GetOrAdd<Customer>();

info.SetInvalidOnNull("Address", true);       // same as [InvalidOnNull] on the slot

info.UpdateAltName(nc =>                      // visit every slot's name comparer
    nc.GetDefaultName().Equals("Zip", StringComparison.OrdinalIgnoreCase)
        ? nc.AddAltName("PostalCode")         // return a new comparer to change the slot
        : null);                              // null leaves it as is
```

## The matcher itself

A slot's whole negotiation (does the schema satisfy me, which columns do I take) lives in its `ParamInfo`, produced by an `IParamInfoMaker`. The default builds one from the slot's type, name, and attributes. An attribute implementing `IParamInfoMaker` replaces that construction for its slot, which is full control over how one slot matches.

Null behavior is part of the matcher too, through `INullColHandler` (see [nullability](nullability.md)).

## Parser selection

One level up sits the choice of the whole parser for `T`. When a parser is needed, the engine walks `TypeParser.TypeParserMakers` in order and the first maker whose `CanHandle<T>()` returns true builds it. `List<>`, `IEnumerable<>`, `Optional<>`, and `Single<>` are entries in that list, each a small wrapper around the element parser. `Optional<>` is a handful of lines: empty on no rows, otherwise wrap the element.

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

A parser is just a compiled `Func<DbDataReader, T>` behind an interface. You can ask for one from any set of columns and drive it yourself.

```csharp
ColumnInfo[] cols = reader.GetColumns();
ITypeParser<Album> parser = TypeParser<Album>.GetTypeParser(ref cols);
Album a = parser.Parse(reader);
```

`TypeParser<T>` keeps a global schema-to-parser cache, and each `QueryCommand` keeps its own, tuned to that command. Holding a parser yourself, as above, skips both. The usage side of this is on [any DbCommand](../running-queries/direct-dbcommand.md).
