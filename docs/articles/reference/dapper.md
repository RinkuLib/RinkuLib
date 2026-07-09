# Coming from Dapper

RinkuLib began as a Dapper extension, so the patterns carry over. There are two calling styles, and both mirror a Dapper call.

- Hand the SQL to the connection. This reads almost like Dapper, and the command is built once and cached by the string.
- Declare a reusable `QueryCommand` and call methods on it. This is the primary Rinku form. The SQL is parsed once up front and each call skips the by-string lookup.

```csharp
// Dapper
IEnumerable<Album> albums = cnn.Query<Album>("SELECT * FROM albums WHERE ArtistId = @id", new { id = 1 });

// RinkuLib, SQL on the connection
List<Album> albums = cnn.Query<List<Album>>("SELECT * FROM albums WHERE ArtistId = @id", new { id = 1 });

// RinkuLib, reusable command
static readonly QueryCommand ByArtist = new("SELECT * FROM albums WHERE ArtistId = @id");
List<Album> albums = ByArtist.Query<List<Album>>(cnn, new { id = 1 });
```

## The shape is a type argument

Where Dapper picks the result shape with the method name, Rinku picks it with the `T` in `Query<T>`.

| Dapper | RinkuLib |
| --- | --- |
| `QueryFirst<T>` | `Query<T>` |
| `QueryFirstOrDefault<T>` | `Query<Optional<T>>` |
| `QuerySingle<T>` | `Query<Single<T>>` |
| `Query<T>` (buffered) | `Query<List<T>>` |
| `Query<T>` (`buffered: false`) | `Query<IEnumerable<T>>` |
| `Execute` | `Execute` |
| `ExecuteScalar<T>` | `ExecuteScalar<T>` |
| `QueryMultiple` | `ExecuteMultiReader` |
| `Query<dynamic>` | `Query<DynaObject>` |

Each reads either way, `cnn.Query<List<T>>(sql, p)` or `cmd.Query<List<T>>(cnn, p)`. The result wrappers are on [result shapes](../running-queries/result-shapes.md).

## Parameters

The anonymous-object habit carries over unchanged, and records or DTOs work the same. Member names match variables case-insensitively, unmatched members are ignored.

```csharp
// Dapper
cnn.Query<Album>("... WHERE ArtistId = @artistId", new { artistId = 1 });

// RinkuLib
cnn.Query<List<Album>>("... WHERE ArtistId = @artistId", new { artistId = 1 });
```

When C# logic should set the values instead of an object, a builder is the other road.

```csharp
var b = ByArtist.StartBuilder();
b.Use("@id", 1);
List<Album> albums = b.Query<List<Album>>(cnn);
```

The extra abilities (usage attributes, builders) are on [supplying values](../running-queries/parameters.md).

## IN clauses

Dapper expands a collection parameter automatically. Rinku does it with the explicit `_X` suffix on the variable.

```csharp
// Dapper
cnn.Query<Track>("SELECT * FROM tracks WHERE GenreId IN @genreIds", new { genreIds = new[] { 1, 2, 3 } });

// RinkuLib
cnn.Query<List<Track>>("SELECT * FROM tracks WHERE GenreId IN (@genreIds_X)", new { genreIds = new[] { 1, 2, 3 } });
// GenreId IN (@genreIds_1, @genreIds_2, @genreIds_3)
```

## What replaces string-built SQL

Where a Dapper codebase concatenates SQL or leans on `WHERE 1=1`, one [conditional template](../conditional-sql/index.md) covers the variations. Mark the optional parts and the values you pass decide the SQL.

```csharp
static readonly QueryCommand Search = new(
    "SELECT * FROM tracks WHERE AlbumId = ?@albumId AND GenreId IN (?@genreIds_X)");

Search.Query<List<Track>>(cnn, new { albumId = 1 });
// SELECT * FROM tracks WHERE AlbumId = @albumId
```
