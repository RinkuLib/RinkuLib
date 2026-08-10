# Parsers

A parser reads a result into one `T`, a single object, a `List<T>`, or an `Optional<T>`. It drives the reader and declares the `CommandBehavior` the executor uses, so a streaming parser opts its query into streaming on its own. The result shapes are themselves parsers, and you can add your own.

## Selection

When a parser is needed for a `T` and a schema, the engine walks `TypeParser.TypeParserMakers` in order. The first maker that claims `T` builds it, and the installed fallback catches the rest. Rinku installs `DefaultTypeParserMaker`, its object-parser implementation, in that fallback slot. `IEnumerable<T>`, `Optional<T>`, and `Single<T>` are complete-result makers because they own streaming or zero/one-row rules. `List<T>` is a registered multi-row parsing info instead: the fallback emits its accumulator directly into the root parser.

The maker is the complete-result layer: it owns reader advancement, zero-row behavior, buffering or streaming, and sync/async execution. `TypeParsingInfo` is the lower compositional layer. It negotiates how one value is built inside another value, including nested multi-row members. A collection accumulator that only needs seed/add/finish belongs in `MultiRowTypeParsingInfo`; it then works both at the root and when nested. Use an `ITypeParserMaker` when the result needs a reader-lifecycle rule that a construction plan cannot express. `List<T>` takes the emitted accumulator road at both levels. `IEnumerable<T>` uses a maker at the root because it defers the cold query until enumeration and returns a true stream; as a nested member it uses the list accumulator because a parent cannot retain an open reader inside itself.

`ListTypeParser<T>` and `FastListTypeParser<T>` remain available when an application explicitly wants to wrap an existing element parser. They are lower-level alternatives, not parallel registrations. The shipped fallback maker also uses them when the registered list plan negotiates successfully but its topology cannot be lowered by the multi-row emitter, such as a list whose element is itself a complete multi-row list. Ordinary scalar, tuple, object, and grouped-object lists stay on the directly emitted road. The emitted driver retains `SequentialAccess` when the negotiated element reads columns in order, just as the wrapped simple parser does.

## Add a result shape

<a id="adding-a-result-shape"></a>A result parser decides how the rows become a value. Reach for this layer when the shape needs to control the reader lifecycle rather than only fold rows into an accumulator.

The `HashSet<T>` below intentionally demonstrates the full parser layer. If all you need is to fold rows into a set, the shorter and composable registration is [`MultiRowTypeParsingInfo`](custom-multi-row-types.md); do not register both unless the parser supplies a distinct top-level lifecycle or measured fast path.

A parser is called with the reader on the first row to parse and advances the reader as it goes. `CanContinue` reports the reader's state on return, `true` when it is left on an untreated row. The element parser follows the same contract, so a shape that gathers rows loops on the element's flag instead of calling `Read` itself.

To gather rows into a `HashSet<T>`, a shape the engine does not ship, write a parser over the element parser, strip `SingleRow` so every row is read, return the empty set on no rows, and add one element per iteration.

```csharp
public sealed class HashSetParser<T>(ITypeParser<T> element) : BaseTypeParser<HashSet<T>> {
    public override CommandBehavior Behavior => element.Behavior & ~CommandBehavior.SingleRow;
    public override bool CanParse(ColumnInfo[] schema) => element.CanParse(schema); // same columns as the element
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

`ReusingBaseTypeParserMaker` is a shipped implementation of `ITypeParserMaker`. Implement the interface yourself for a shape that is not a generic wrapper, or that builds its inner parser some other way.

The fallback is also replaceable without making the parser cache know its implementation:

```csharp
TypeParser.DefaultTypeParserMaker = new MyFallbackParserMaker();
```

The slot uses only `ITypeParserMaker`. Additional shapes remain independent entries in
`TypeParser.TypeParserMakers`; replace the fallback, insert one shape, or reorder the makers as separate choices.

## Schema compatibility

Every parser owns `CanParse(ColumnInfo[] schema)`. The global cache asks the cached parsers for `T` whether they accept the requested columns; it does not keep a second schema beside each parser. A wrapper such as `HashSetParser<T>` delegates to its element parser, while the built-in generated parser uses the final-program comparison described below.

For a generated parser, the built-in object maker negotiates and emits a candidate, then compares the final emitted program, reader behavior, and any bound state with the cached parser. The candidate never becomes a delegate when those final results are equal, and the generated parser does not retain the source `ColumnInfo[]`. This lets tuples, nested objects, alternative names, and custom simple plans reuse naturally without each plan having to describe which schema details mattered; two same-typed columns that negotiate different reads still cannot be silently swapped. This comparison is deliberately cold work: command caches retain the chosen parser, so it is never paid per row or on a warm command.

The candidate is negotiated from the registration that exists when compatibility is checked. Registration is
therefore expected to be complete before querying begins. Changing it later can change whether a subsequent
global-cache lookup considers an existing parser compatible, because that lookup may emit a different candidate.
It still does not alter, invalidate, or prevent execution of the already-generated parser, and a `QueryCommand`
that retained it continues to call it directly. This keeps the implementation small and avoids retaining an
ever-growing list of previously accepted schemas; the matching strategy can be replaced later without changing
the parser-cache contract.

A parser that discovers columns from the reader on every run can instead return `true`, so one instance serves every schema:

```csharp
public override bool CanParse(ColumnInfo[] schema) => true;
```

`CanParse` runs only while selecting or merging a cached parser, never per row. Makers and metadata factories
are likewise cold-path composition points. Once a command has retained its chosen parser, parsing calls that
parser directly. `CanParse` must return `true` only when `Parse` and `ParseAsync` can safely read that schema.

## Getting a parser

The makers run behind `TypeParser.GetTypeParser<T>(cols)`, which builds a parser for a schema and caches it. The parser itself decides whether it is the cached match. That cache is a linear scan kept to hold memory down, not for speed, so a lookup per query is slow. Run commands through a cache that keeps the parser after first use instead. The usage side, and how to hand a parser to a `DbCommand`, is on [any DbCommand](../running-queries/direct-dbcommand.md).

## Invalidation

Parsers are cached by default. Invalidate them afterward when a runtime change makes a retained parser obsolete. The global cache can find parsers by schema through the same `CanParse` contract used for selection, or remove one exact parser instance. It never needs to know whether the implementation maps a tuple, a named object, or an application-defined shape.

```csharp
TypeParser.Invalidate(columns, ParserInvalidationMode.CheckUsage);
TypeParser.Invalidate(parser, ParserInvalidationMode.InvalidateReferences);
TypeParser.InvalidateAll(ParserInvalidationMode.InvalidateReferences);
```

Schema invalidation removes every distinct global parser whose `CanParse(columns)` returns `true`. Because one parser may accept several schemas, invalidating one accepted schema removes that parser completely rather than leaving another global entry pointing to the same instance.

The mode says what owners of the exact parser do before it is disposed:

- `CheckUsage` removes it from the global cache, but a `QueryCommand`, `CachedTypeParser<T>`, or custom cache that still retains it cancels disposal and keeps using it.
- `InvalidateReferences` tells every subscribed cache to remove that exact reference. Disposal then proceeds and cancellation is ignored.

These are mutually exclusive policies, so `ParserInvalidationMode` is a normal enum rather than a flags enum.

Registration and parser-cache management are deliberately separate. Changing `TypeParsingInfo`, a grouping
rule, a construction path, a parser maker, or a default factory does not inspect, remove, notify, or
dispose any generated parser. Existing global, `QueryCommand`, `CachedTypeParser<T>`, and caller-held references
remain alive. Register at startup. If an application deliberately changes registration after querying has begun
and wants old cache entries discarded, it must explicitly invalidate the relevant parser or schema with the
methods above. Disposing a `QueryCommand` is the only automatic cleanup of that command's own parser references
and event subscription.

The cache owners also expose their own explicit local operations:

```csharp
command.InvalidateParsers(); // GlobalIfUnused: the default
command.InvalidateParsers(QueryParserInvalidationScope.Local); // only this QueryCommand
command.InvalidateParsers(QueryParserInvalidationScope.GlobalIfUnused); // explicit form of the default
command.InvalidateParsers(QueryParserInvalidationScope.Global); // force every owner to release the exact parsers
cachedParser.Invalidate();
```

The no-argument command call uses `GlobalIfUnused`. It releases the command first, then removes each exact parser from the global cache only
when no other subscribed cache retains it. If another `QueryCommand`, `CachedTypeParser<T>`, or custom cache
reports that it still uses the parser, the global entry is restored and that owner keeps working. `Global`
instead removes the exact parser globally with `InvalidateReferences`, forcing every subscribed cache to drop
that reference. Neither option uses schema invalidation, so another parser that merely accepts the same columns
is untouched. `Local` releases only that `QueryCommand`; the global parser remains available for reuse.

`QueryCommand.Dispose()` uses the same `GlobalIfUnused` default before clearing parameter accessors and disposing
the mapper. An application-lifetime command therefore does not matter, while disposing the last command owner
also releases the otherwise-unused global parser.

Invalidate only one parser by retrieving the exact instance for the relevant usage shape and handing it back
to the command. The individual overload has the same scopes and the same `GlobalIfUnused` default:

```csharp
command.TryGetCachedParser<MyResult>(usageMap, out var parser, resultSetIndex);
if (parser is not null)
    command.InvalidateParser(parser);
```

Every local cache entry pointing to that exact instance is removed; other parsers in the command remain. A
parser the command does not retain is ignored, so this API cannot use a command as an accidental handle for an
unrelated global parser. Use `TypeParser.Invalidate(parser, mode)` when the operation intentionally starts from
an independently obtained parser rather than from a command that owns it.

`CachedTypeParser<T>.Invalidate()` is likewise local to that cache.

`TypeParser.ParserDisposing` runs after the parser leaves the global cache and before disposal. Its arguments carry the exact parser and the mode. A cache subscribes with an instance method when it retains its first parser, compares by reference, and either sets `Cancel` for `CheckUsage` or drops the reference for `InvalidateReferences`. It unsubscribes when empty and in `Dispose`; this is an ordinary strong event subscription, so a cache with a shorter lifetime must be disposed.

When an owner voluntarily drops a parser, `TypeParser.Release(parser)` disposes it only if the global cache and every subscribed cache have also released that exact instance. Parser disposal is idempotent. A wrapper parser does not own a child parser supplied by its maker and must not dispose that child itself.
