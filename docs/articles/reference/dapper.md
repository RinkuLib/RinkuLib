# Coming from Dapper

RinkuLib began as a Dapper extension, so most patterns have a direct equivalent. The structural difference: the SQL lives in a reusable `QueryCommand` instead of being passed inline on every call.

## Method mapping

Dapper hangs the call off the connection. Rinku hangs it off the command and passes the connection in. `p` is a parameter object, anonymous objects work.

| Dapper | RinkuLib |
| --- | --- |
| `cnn.QueryFirst<T>(sql, p)` | `cmd.Query<T>(cnn, p)` |
| `cnn.QueryFirstOrDefault<T>(...)` | `cmd.Query<Optional<T>>(cnn, p)` |
| `cnn.QuerySingle<T>(...)` | `cmd.Query<Single<T>>(cnn, p)` |
| `cnn.Query<T>(...)` (buffered) | `cmd.Query<List<T>>(cnn, p)` |
| `cnn.Query<T>(..., buffered: false)` | `cmd.Query<IEnumerable<T>>(cnn, p)` |
| `cnn.Execute(sql, p)` | `cmd.Execute(cnn, p)` |
| `cnn.ExecuteScalar<T>(...)` | `cmd.ExecuteScalar<T>(cnn, p)` |
| `cnn.QueryMultiple(...)` | `cmd.ExecuteMultiReader(cnn, out var dbCmd, p)` |
| `cnn.Query<dynamic>(...)` | `cmd.Query<DynaObject>(cnn, p)` |

The wrappers are on [result shapes](../running-queries/result-shapes.md).

## Parameters

The anonymous-object habit carries over unchanged.

```csharp
// Dapper
cnn.Query<Album>("... WHERE ArtistId = @artistId", new { artistId = 1 });

// RinkuLib
albumCmd.Query<List<Album>>(cnn, new { artistId = 1 });
```

Member names match variables case-insensitively, unmatched members are ignored. The extra abilities (usage attributes, builders) are on [supplying values](../running-queries/parameters.md).

## IN clauses

Dapper expands a collection parameter automatically. Rinku does it with the explicit `_X` suffix.

```csharp
// template
"SELECT * FROM tracks WHERE GenreId IN (@genreIds_X)"
// @genreIds = [1, 2, 3]  ->  GenreId IN (@genreIds_1, @genreIds_2, @genreIds_3)
```

## What replaces string-built SQL

Where a Dapper codebase concatenates SQL or leans on `WHERE 1=1`, one [conditional template](../conditional-sql/index.md) covers the variations.
