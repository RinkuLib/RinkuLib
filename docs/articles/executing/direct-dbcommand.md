# Direct execution via DbCommand

*Use the mapping engine on any DbCommand.*

The mapping engine is its own layer, reachable without a `QueryCommand`. The same extension methods sit directly on a `DbCommand` (and `IDbCommand`), so a command you built yourself still gets Rinku's compiled, schema-keyed [mapping](../mapping/index.md).

```csharp
using var cmd = cnn.CreateCommand();
cmd.CommandText = "SELECT TrackId AS Id, Name FROM tracks WHERE TrackId = @id";
cmd.Parameters.Add(new SqlParameter("@id", 10));

Track track = cmd.Query<Track>();
```

Because there is no template or builder state involved here, supply the command fully configured, then call the extension.

## The `DbCommand` extensions

All live in `DBCommandExtensions`. The common optional arguments are `disposeCommand` (dispose the command, and clear its parameters, after running) and `cache` (a parser or result cache to populate). Async versions add a `CancellationToken ct`.

| Method | Returns | Notes |
| --- | --- | --- |
| `Execute(bool disposeCommand, ICache? cache = null)` | `int` | affected rows. `ExecuteAsync(...)` returns `Task<int>` |
| `ExecuteScalar<T>(bool disposeCommand, ICache? cache = null)` | `T` | first column of the first row, parsed to `T`. async version too |
| `ExecuteReader(CommandBehavior behavior = default, ICache? cache = null)` | `DbDataReader` | opens and closes the connection as needed. async version too |
| `Query<T>(bool disposeCommand = true, ITypeParser<T>? parser = null, ICacheUsingParser<T>? cache = null)` | `T` | first row mapped to `T` (or `default`). `QueryAsync<T>(...)` and `StreamQueryAsync<T>(...)` also exist |
| `ExecuteMultiReader(QueryCommand command, bool[] usageMap, bool disposeCommand, CommandBehavior behavior = default)` | `MultiReader` | for [multiple result sets](multi-result.md). async version too |

There is also `reader.GetParser<T>()` on a `DbDataReader` to get the compiled `ITypeParser<T>` for the reader's current schema, and `cnn.GetCommand(transaction, timeout)` to create a pre-configured command.

## `IDbCommand` support

The same surface (`Execute`, `ExecuteScalar<T>`, `ExecuteReader`, `Query<T>`, `ExecuteMultiReader`, and their async forms) is mirrored for `IDbCommand`. When the underlying instance is actually a `DbCommand`, the async calls forward to the real async implementations. Otherwise they run synchronously and wrap the result in a completed `Task`, and a non-`DbDataReader` reader is adapted with `WrappedBasicReader`.

Pass `parser` or `cache` only when you want to reuse a pre-built parser. Otherwise the engine builds one from the result schema on the fly.
