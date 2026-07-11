# RinkuLib

A micro-ORM for .NET, built directly on ADO.NET. You write the SQL, name a type, and get your objects back.

```csharp
public record Album(int Id, string Title);

static readonly QueryCommand GetAlbums = new(
    "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

using DbConnection cnn = GetConnection();

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 1 });
```

The command is created once and reused. Each call binds the parameters, runs the query, and builds the objects from the result columns, the ADO.NET plumbing you would otherwise write by hand. The SQL stays yours.

`Album` is one of your own types. Any class, record, or struct works, with no attributes and no configuration. The type argument decides the result shape, `Query<Album>` for one row, `Query<List<Album>>` for all, `Query<IEnumerable<Album>>` to stream.

New here? Start with the [quick start](articles/getting-started/quick-start.md), then [running queries](articles/running-queries/index.md).

## What's here

Reach for one on its own or several together.

- **[Mapping](articles/mapping/index.md)** turns result rows into your types, from a single scalar to a nested object graph.
- **[Conditional SQL](articles/conditional-sql/index.md)** lets one template adapt to the values you pass, staying valid without string concatenation.
- **[Code generation](articles/codegen/index.md)** produces ready-to-run `DbCommand`s from your database schema at design time.
- **[Tracking](articles/tracking/index.md)** adds edit, commit, and revert change tracking over an `IEnumerable`.

Mapping is the spine. The others build on it or feed into it. The [API reference](api/index.md) is generated from the source XML comments.
