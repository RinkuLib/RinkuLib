# RinkuLib

A micro-ORM for .NET, built directly on ADO.NET. Write SQL, get your objects back.

```csharp
// Define the command once. Reuse it for the life of the app.
static readonly QueryCommand GetAlbums =
    new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

using DbConnection cnn = GetConnection();

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 1 });
```

`Album` is one of your own types. Any class, record, or struct works, with no attributes and no configuration. The type argument decides the result shape: `Query<Album>` for one row, `Query<List<Album>>` for all rows, `Query<IEnumerable<Album>>` to stream.

New here? Start with the [quick start](articles/getting-started/quick-start.md), then [running queries](articles/running-queries/index.md).

## The four capabilities

Reach for one on its own or several together.

- **[Mapping](articles/mapping/index.md)** turns result rows into your types, from a single scalar to a nested object graph.
- **[Conditional SQL](articles/conditional-sql/index.md)** lets one template adapt to the values you pass, staying valid without string concatenation.
- **[Code generation](articles/codegen/index.md)** produces ready-to-run `DbCommand`s from your database schema at design time.
- **[Tracking](articles/tracking/index.md)** adds edit, commit, and revert change tracking over an `IEnumerable`.

Mapping is the spine. The other three build on it or feed into it. The [API reference](api/index.md) is generated from the source XML comments.
