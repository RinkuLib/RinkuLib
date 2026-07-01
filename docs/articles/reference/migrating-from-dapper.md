# Coming from Dapper

*The Dapper calls you know, and their RinkuLib equivalents.*

RinkuLib began as an extension to Dapper, so most patterns have a direct equivalent. The one structural difference is that the SQL lives in a reusable `QueryCommand` instead of being passed inline on every call.

## Method mapping

Where Dapper hangs the call off the connection, Rinku hangs it off the reusable `cmd` (a `QueryCommand`) and passes the connection in. `p` is a parameter object (anonymous objects work).

| Dapper | RinkuLib |
| --- | --- |
| `cnn.QueryFirst<T>(sql, p)` | `cmd.Query<T>(cnn, p)` |
| `cnn.QueryFirstOrDefault<T>(...)` | `cmd.Query<Optional<T>>(cnn, p)` |
| `cnn.QuerySingle<T>(...)` | `cmd.Query<Single<T>>(cnn, p)` |
| `cnn.Query<T>(...)` (buffered) | `cmd.Query<List<T>>(cnn, p)` |
| `cnn.Query<T>(..., buffered:false)` | `cmd.Query<IEnumerable<T>>(cnn, p)` |
| `cnn.Execute(sql, p)` | `cmd.Execute(cnn, p)` |
| `cnn.ExecuteScalar<T>(...)` | `cmd.ExecuteScalar<T>(cnn, p)` |
| `cnn.QueryAsync<dynamic>(...)` | `cmd.QueryAsync<DynaObject>(cnn, p)` |

No builder appears here. For a fixed query you call straight on the command. Reach for a [builder](../executing/builders.md) only when C# logic must decide which optional pieces of the SQL are active. See [choosing the result type](../mapping/simple-results.md) for `Optional<T>`, `Single<T>`, and `MaybeNull<T>`.

## Parameters

Dapper anonymous parameter objects map onto Rinku's [one-step building](../executing/builders.md#one-step-building).

```csharp
// Dapper
cnn.Query<Album>("... WHERE ArtistId = @artistId", new { artistId = 1 });

// RinkuLib
albumCmd.Query<List<Album>>(cnn, new { artistId = 1 });
```

The object's **property and field names are matched to the template's variables**, a name with no matching variable is ignored, and an optional `?@Var` whose member is absent stays out of the query. Three shapes exist on the direct `Query` and `Execute` extensions:

- `Query<T>(cnn, object? parametersObj = null, ...)`. The convenient form. Reads the object reflectively (anonymous objects, DTOs).
- `Query<T, TObj>(cnn, TObj parametersObj, ...) where TObj : notnull`. A generic overload that avoids boxing for value-type parameter holders.
- `Query<T, TObj>(cnn, ref TObj parametersObj, ...)`. A `ref` variant for large structs.

If you'd rather set values explicitly, use the builder: `cmd.StartBuilder()` then `.Use("@artistId", 1)` or `.UseWith(new { artistId = 1 })`.

## IN clauses

Dapper expands a collection parameter automatically. Rinku uses the [`_X` handler](../conditional-sql/handlers.md).

```csharp
// template
"SELECT * FROM tracks WHERE GenreId IN (@genreIds_X)"
// @genreIds = [1, 2, 3]  ->  GenreId IN (@genreId_1, @genreId_2, @genreId_3)
```
