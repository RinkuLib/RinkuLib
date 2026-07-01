# RinkuLib

A micro-ORM for .NET, built directly on ADO.NET. Write SQL, get your objects back.

```csharp
// Define the command once. Reuse it for the life of the app.
static readonly QueryCommand GetAlbums =
    new("SELECT AlbumId AS ID, Title FROM albums WHERE ArtistId = @artistId");

using DbConnection cnn = GetConnection();

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 1 });
```

That is the whole loop, a command, a connection, and the type you want back. The type argument decides the shape, here `List<Album>`, but `Album` for one row, `IEnumerable<Album>` to stream, and more. `Album` is just one of your own types. Nothing about it is special to Rinku.

If you are new here, the [quick start](articles/getting-started/quick-start.md) goes from a SQL string to mapped objects, and [core concepts](articles/getting-started/core-concepts.md) covers the mental model behind the call above.

## What's in the box

RinkuLib is four capabilities. You can reach for one on its own or several together.

- **[Mapping](articles/mapping/index.md)** turns result rows into your types, from a single scalar to a nested object graph.
- **[Conditional SQL](articles/conditional-sql/overview.md)** lets one template adapt to the values you pass, staying valid without string concatenation.
- **[Code generation](articles/aot-codegen/overview.md)** produces ready-to-run `DbCommand`s from your database schema at design time.
- **[Tracking](articles/tracking/overview.md)** adds edit / commit / revert change tracking over an `IEnumerable`.

In a real app, mapping is the part nearly everyone touches. The others build on it or feed into it. For every way to run a command, see [running queries](articles/executing/overview.md), and the [API reference](api/index.md) is generated from the source XML comments.
