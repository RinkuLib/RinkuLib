# Rinku documentation

Rinku keeps SQL in application code and maps database results into the shape requested by the caller.

```csharp
public record Album(int Id, string Title) : IDbReadable;

static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
```

Start with the [overview](overview.md) if Rinku is new to you.

## Main areas

* [Running queries](running-queries/execution.md) covers commands, values, builders, result shapes, async work, streaming, transactions, stored procedures, and direct reader access.
* [Mapping](mapping/objects.md) covers objects, constructors, nested types, collections, grouping, names, null handling, tuples, dynamic rows, and registration.
* [Conditional SQL](conditional-sql/variables.md) covers optional parameters, collections, conditional markers, dynamic projection, and value handlers.
* [Advanced customization](customization/index.md) covers custom registrations, parsers, parameter behavior, method signatures, conditional SQL handlers, and cache ownership.
* [Code generation](codegen/index.md) covers Rinku Power Tools configuration, query discovery, generated commands, and refresh behavior.
* [Analyzers and code fixes](codegen/analyzers.md) ship with `Rinku` and cover schema links, constructor contracts, and method invocation generation.
* [Tracking](tracking/index.md) covers editable items, tracking lists, runtime generated edit types, validation, and metadata.
* [Coming from Dapper](reference/dapper.md) puts common Dapper operations beside the matching Rinku forms.

Use the [FAQ](reference/faq.md), [errors](reference/errors.md), [performance notes](reference/performance.md), and [API reference](../api/index.md) when you need a direct reference.
