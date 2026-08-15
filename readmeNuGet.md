# RinkuLib

A micro-ORM for .NET built directly on **ADO.NET**. You keep the SQL and choose a result type. Rinku reads the result into that type.

Read the [documentation](https://rinkulib.github.io/RinkuLib/), see the [Dapper guide](https://rinkulib.github.io/RinkuLib/articles/reference/dapper.html), or browse the [source on GitHub](https://github.com/RinkuLib/RinkuLib).

```csharp
using Rinku;

public record Album(int Id, string Title);

// Create the command once (a static readonly field is ideal). The SQL template is parsed here.
static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 1 });
// GetAlbums.Query<Album>(cnn, ...)               -> the first album
// GetAlbums.Query<IEnumerable<Album>>(cnn, ...)  -> streamed
```

The type argument chooses the result parser. A class, record, or struct can be used directly when the columns match a constructor or writable members. Nested types follow separate registration rules. Parsers can also be added or replaced.

Rinku includes these features.

- **Object mapping.** Map returned columns to your types and change the rules when needed.
- **Conditional SQL.** Mark optional SQL in one template without assembling strings at the call site.
- **Code generation.** Generate ready-to-run `DbCommand`s from your database schema at design time.
- **Tracking.** Edit, commit, and revert change tracking over an `IEnumerable`.

Mapping is the spine, and the rest builds on it. Targets .NET 8 and .NET 10.

## Conditional SQL

When a query must change shape at runtime, mark the optional parts (`?@var`, `/*...*/`) and the values you supply decide what stays.

```csharp
static readonly QueryCommand Search = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId AND Title LIKE ?@title");

// @title omitted, so its clause is pruned.
List<Album> albums = Search.Query<List<Album>>(cnn, new { artistId = 1 });
// Resulting SQL: SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

## How it works

You define the template first, so application code chooses which marked parts are active without joining SQL strings or adding `WHERE 1=1`. The result type chooses a parser. The parser and mapping rules decide how to read the returned columns and rows.
