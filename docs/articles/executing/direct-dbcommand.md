# Direct execution via DbCommand

*Use the mapping engine on any DbCommand.*

The mapping engine is its own layer, reachable without a `QueryCommand`. The same extension methods sit directly on a `DbCommand` (and `IDbCommand`), so a command you built yourself still gets Rinku's compiled, schema-keyed [mapping](../mapping/index.md).

```csharp
using var cmd = cnn.CreateCommand();
cmd.CommandText = "SELECT TrackId AS Id, Name FROM tracks WHERE TrackId = @id";
cmd.Parameters.Add(new SqlParameter("@id", 10));

Track track = cmd.Query<Track>();
```

Supply the command fully configured, then call the extension. This plain form is not the fast path. With no parser passed, `Query<T>` runs the command, reads the result columns, and derives the parser for that shape, and it does so on every call. When the shape is stable and the call is hot, hand in a parser you built once (or a cache that holds one) so that derivation is skipped.

```csharp
// reuse a parser you obtained earlier (for example via reader.GetParser<Track>())
Track track = cmd.Query<Track>(parser: trackParser);
```

## What the call does

`Query<T>` reads with `CommandBehavior.SingleResult`. If the connection is closed it opens it and closes it again afterward. It maps the first row to `T`, or returns the parser's empty result when there are no rows (a plain `T` throws, `Optional<T>` is empty, `List<T>` is an empty list). With `disposeCommand` (the default), it clears the parameters and disposes the command when it finishes.

A streaming result is different. When the parser for `T` keeps the reader open, an `IEnumerable<T>` or `IAsyncEnumerable<T>`, it takes ownership of the reader and command and disposes them once you finish enumerating. Enumerate or dispose the result before you reuse the connection.

## The `DbCommand` extensions

All live in `DBCommandExtensions`. The common optional arguments are `disposeCommand` (dispose the command, and clear its parameters, after running) and `cache` (a parser or result cache to populate). Async versions add a `CancellationToken ct`.

| Method | Returns | Notes |
| --- | --- | --- |
| `Execute(bool disposeCommand, ICache? cache = null)` | `int` | affected rows. `ExecuteAsync(...)` returns `Task<int>` |
| `ExecuteScalar<T>(bool disposeCommand, ICache? cache = null)` | `T` | first column of the first row, parsed to `T`. async version too |
| `ExecuteReader(CommandBehavior behavior = default, ICache? cache = null)` | `DbDataReader` | opens and closes the connection as needed. async version too |
| `Query<T>(bool disposeCommand = true, ITypeParser<T>? parser = null, ICacheUsingParser<T>? cache = null)` | `T` | first row mapped to `T`, or the parser's empty result. `QueryAsync<T>(...)` and `StreamQueryAsync<T>(...)` also exist |
| `ExecuteMultiReader(QueryCommand command, bool[] usageMap, bool disposeCommand, CommandBehavior behavior = default)` | `MultiReader` | for [multiple result sets](multi-result.md). async version too |

There is also `reader.GetParser<T>()` on a `DbDataReader` to get the compiled `ITypeParser<T>` for the reader's current schema, and `cnn.GetCommand(transaction, timeout)` to create a pre-configured command.

## `IDbCommand` support

The same surface (`Execute`, `ExecuteScalar<T>`, `ExecuteReader`, `Query<T>`, `ExecuteMultiReader`, and their async forms) is mirrored for `IDbCommand`. When the underlying instance is actually a `DbCommand`, the async calls forward to the real async implementations. Otherwise they run synchronously and wrap the result in a completed `Task`, and a non-`DbDataReader` reader is adapted with `WrappedBasicReader`.

Pass `parser` or `cache` only when you want to reuse a pre-built parser. Otherwise the engine builds one from the result schema on the fly.
