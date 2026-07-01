# Custom parsing & matching

*Change how a slot matches a column, or how a whole type is parsed.*

The engine is built as a set of base points with default implementations, and the public API is just those defaults. You can change a single slot, a whole type, or the parser-selection step. This page goes from the most local change to the broadest.

## The matcher, `ParamInfo`

Every slot (a constructor parameter or settable member) is paired with a **`ParamInfo`** that decides whether the schema can satisfy it. A `ParamInfo` is produced by an **`IParamInfoMaker`**.

- **Default.** `DefaultParamInfoMaker` builds a `ParamInfo` from the slot's type, name, and functional attributes.
- **By attribute.** If any attribute on the slot implements `IParamInfoMaker`, its `MakeMatcher(...)` builds the matcher instead, giving you full control over how that slot negotiates.

A `ParamInfo` is closed lazily (a generic slot resolves its real type at use). Simple types look for one column matching by name and convertible by type. Complex types delegate to that type's `TypeParsingInfo` and pass a growing name prefix (the **column modifier**) down the tree.

## Name matching

The `INameComparer` holds the candidate names for a slot (its own name plus any alternates) and always matches against `[modifier] + [candidate]`.

- **`[Alt("Name")]`.** Add an alternative candidate name.
- **`[AltSkippingSegments("Name", nbSegmentSpan)]`.** An alternate that also skips a number of modifier segments, letting a nested member match higher up the prefix.
- **`[AltUpTo("Name", "KeyUpTo")]`.** An alternate that skips segments up to a named key in the modifier.
- **`[NoName]`.** Drop name matching entirely, match by position and type only. This is how the [cardinality wrappers](simple-results.md#saying-how-many-rows-you-expect) wrap their inner value.

## Column scavenging

Three flag attributes tune which columns a slot may take. They are the fix for the positional pitfalls in [value tuples](value-tuples.md).

| Attribute | Effect |
| --- | --- |
| `[CanNotLookAnywhere]` | Force sequential matching, only the column right after the last used one. |
| `[CanLookAnywhere]` | Allow matching any column, not just the next. |
| `[MayReuseCol]` | Allow matching a column another slot already consumed. |

## Null behavior

How a slot reacts to `NULL` is also part of its matcher, through `INullColHandler`, covered on [nullability](nullability.md). In short, `[NotNull]` throws on null, `[InvalidOnNull]` abandons the construction path, and you can plug in a custom `INullColHandler` or `INullColHandlerMaker`.

## Bulk configuration

`TypeParsingInfo` (see [registration & discovery](registration-and-discovery.md)) can apply settings across a type's registry by default name.

```csharp
var info = TypeParsingInfo.GetOrAdd<Customer>();
info.SetInvalidOnNull("Address", true);   // matching params in PossibleConstructors
info.AddAltName("Zip", "PostalCode");      // a "Zip" slot also matches a "PostalCode" column
```

## Replacing how a type is parsed, `ITypeParserMaker`

One level up from per-slot control is the parser-selection step. When a parser for `T` is needed, the engine walks `TypeParser.TypeParserMakers` in order. The first whose `CanHandle<T>()` returns `true` builds the parser, otherwise it falls back to `DefaultTypeParserMaker`. This is the same place the built-ins use. `IEnumerable<>`/`List<>`, `Optional<>`, and `Single<>` are each registered here, each a small parser wrapping the element parser. They are the clearest examples to read before writing your own. `Optional<>` in particular is only a handful of lines, an empty result on no rows, otherwise wrap the parsed element.

```csharp
public interface ITypeParserMaker {
    bool CanHandle<T>();
    bool TryMakeParser<T>(INullColHandler nullColHandler, ColumnInfo[] cols,
                          [MaybeNullWhen(false)] out ITypeParser<T> parser);
}
```

Add your own to project a domain type, say a `Money` wrapper over a `decimal` column.

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

// register before the first query runs (insert ahead of the defaults for priority)
TypeParser.TypeParserMakers.Insert(0, new MoneyParserMaker());
```

The parser you return also sets the result shape and the `CommandBehavior` the executor uses, so a sequential or streaming parser opts the whole query into that behavior automatically.
