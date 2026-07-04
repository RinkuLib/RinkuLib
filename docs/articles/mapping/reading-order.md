# Reading order

Matching has no order by default. A slot takes any unconsumed column its names and type accept, and consumes it. Two flags refine that regime, per slot or inherited from the context:

| Attribute | Effect |
| --- | --- |
| `[CanNotLookAnywhere]` | Sequential, only the column right after the last one consumed. |
| `[CanLookAnywhere]` | Free, any unconsumed column, lifting an inherited sequential. |
| `[MayReuseCol]` | May take a column another slot already consumed. |

They apply to constructor parameters, fields, and properties. The runtime form replaces the slot with one carrying the flag, on the default info that exposes the paths:

```csharp
if (TypeParsingInfo.GetOrAdd<Customer>() is DefaultTypeParsingInfo info) {
    info.Init();
    var slots = info.PossibleConstructors[0].Parameters;
    slots[2] = new ParamInfoPlus(slots[2].Type, slots[2].NullColHandler, slots[2].NameComparer,
        FlagUpdater.CanReuse, IFallbackParserGetter.Nothing);   // what [MayReuseCol] would have done
}
```

A parsing info can set the regime for everything it negotiates. `CtorTypeInfo`, the positional one behind tuples, is the built-in that asks for sequential (see [registering with another info](registration.md#registering-with-another-info)).

Freeing one slot in a sequential context has a consequence worth knowing: the columns it jumps over sit behind the cursor, so the following slots must be freed the same way to find them.

```csharp
public record struct Contact(int Id, string Name, [CanLookAnywhere] string? Email = null);
public record struct Follower([CanLookAnywhere] int Id, string Name, string? Email = null);

// Columns: Id | Name | Id | Name | Email
var (a, b) = cmd.Query<(Contact, Follower)>(cnn);
// a takes Id(0), Name(1) and reaches ahead for Email(4).
// b's Id searches anywhere and finds Id(2) behind the cursor, Name follows at (3).
```
