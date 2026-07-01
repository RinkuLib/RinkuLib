# Custom handlers

*Register your own `_Letter` handler.*

The [built-in handlers](handlers.md) (`_N`, `_S`, `_R`, `_X`) are just entries in two global, swappable mappers. You can claim any unused letter for your own logic.

## Two kinds of handler

Both implement `IQuerySegmentHandler`.

- A **base handler** only edits the generated **SQL string** (like `_N`, `_S`, `_R`). It lives in `QueryFactory.BaseHandlerMapper`.
- A **`SpecialHandler`** also touches the **`DbCommand`**, binding parameters and more (like `_X`). It lives in `SpecialHandler.SpecialHandlerGetter`.

| Mapper | Element type | Use for |
| --- | --- | --- |
| `QueryFactory.BaseHandlerMapper` | `LetterMap<HandlerGetter<IQuerySegmentHandler>>` | SQL-string-only handlers |
| `SpecialHandler.SpecialHandlerGetter` | `LetterMap<HandlerGetter<SpecialHandler>>` | Handlers that touch the `DbCommand` |

## Registering

```csharp
// SQL-string only, @When_D -> a literal date expression
QueryFactory.BaseHandlerMapper['D'] = _ => new LegacyDateHandler();

// Touches the command, @Secret_P -> binds an encrypted parameter
SpecialHandler.SpecialHandlerGetter['P'] = name => new EncryptionHandler(name);
```

The factory delegate (`HandlerGetter<T>`) receives the variable name. Letters are **case-insensitive**, there are 26 slots (A to Z), and the mappers are **global**. Configure them once at startup, before any `QueryCommand` is compiled.
