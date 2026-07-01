# Mapping engine

Turning result rows into your objects. Ask for a type, get instances back.

```csharp
List<Album> albums = GetAlbums.Query<List<Album>>(cnn);
```

Give the engine the columns a query returns and a target type. It works out a construction path (a constructor, a factory, members, and any nested objects), compiles that path into a fast reader (`ITypeParser<T>`, essentially a `Func<DbDataReader, T>`), and caches it by result shape. `Album` is just an example here. A class, record, or struct all map the same way.

## How a row becomes an object

1. **Read the schema.** The result columns (`ColumnInfo[]`, name, type, nullability).
2. **Match.** Walk the type's construction paths and take the **first one the schema fully satisfies**. Paths are kept most-specific first, so that first complete match is usually the best one. Nested types recurse.
3. **Compile and cache.** Build IL for that path and store it, keyed by schema, so an identical result shape reuses it.

## `Query<T>` carries no behavior of its own

It runs the command and hands each result to the parser chosen for `T`. Whether zero rows throws or returns empty, how many rows are taken, what a `NULL` does, all of that is decided by `T`. A different result rule is a different `T`, not a different method.

```csharp
Album one           = GetAlbumById.Query<Album>(cnn, new { id = 1 });            // throws if absent
Optional<Album> opt = GetAlbumById.Query<Optional<Album>>(cnn, new { id = 1 });  // empty if absent
List<Album> many    = GetAlbums.Query<List<Album>>(cnn);                         // every row
```

`List<T>`, `Optional<T>`, and the rest are themselves small parsers, one shape each. They wrap the same element parser with a different rule for "no rows" and "many rows," and change nothing about how the element is built. Nesting, attributes, and null handling behave identically whether you ask for `Album`, `List<Album>`, or `Optional<Album>`. See [choosing the result type](simple-results.md).

## Registration

A type becomes eligible for mapping once it's registered, and registration follows a rule.

- A type you ask for **directly** (the `T` in `Query<T>`) is registered the first time you request it.
- A type reached only **indirectly** (a constructor parameter whose type is one of yours) must be registered first: implement `IDbReadable`, register it explicitly, query it directly somewhere earlier, or let a `TypeParsingInfo` register it.

See [registration & discovery](registration-and-discovery.md).

## Going a layer down

The engine isn't tied to `QueryCommand`. Any `DbDataReader` can produce a parser, and any `DbCommand` can map its results (see [direct DbCommand](../executing/direct-dbcommand.md)).

```csharp
ColumnInfo[] cols = reader.GetColumns();
ITypeParser<Album> parser = TypeParser<Album>.GetTypeParser(ref cols);
Album a = parser.Parse(reader);
```

Two cache layers sit under this. `TypeParser<T>` holds a **global** cache from schema to parser. A `QueryCommand` keeps **its own** cache of the same parsers, tuned to that command, so its hot path skips the global lookup. Holding a parser yourself skips even that.

How a type is built, how a member matches a column, what a `NULL` does, which parser is chosen. Every one of these is a real implementation you can adjust with an attribute or replace outright. See [custom parsing & matching](custom-parsing.md).

## Pages in this section

- [Choosing the result type](simple-results.md). One entry point, behavior carried by `T`.
- [Complex objects](complex-objects.md). Constructors, factories, members, nesting.
- [Nullability](nullability.md). Behavior on `NULL`.
- [Value tuples](value-tuples.md). Positional, name-agnostic mapping.
- [DynaObject](dyna-object.md). Dynamic, dictionary-like rows.
- [Registration & discovery](registration-and-discovery.md). How types and construction paths are found and ordered.
- [Custom parsing & matching](custom-parsing.md). Adjust or replace any level of the default.
