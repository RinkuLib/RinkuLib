# Rinku

## Install

```bash
dotnet add package Rinku
```

## First query

```csharp
public record Album(int Id, string Title);

static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
```

[Overview](overview.md)

## Documentation

### Running queries

[Execution](running-queries/execution.md) · [Result shapes](running-queries/result-shapes.md) · [Values](running-queries/values.md) · [Builders](running-queries/builders.md) · [Async](running-queries/async.md) · [Streaming](running-queries/streaming.md) · [Stored procedures](running-queries/stored-procedures.md) · [Multiple results](running-queries/multiple-results.md)

### Mapping

[Objects](mapping/objects.md) · [Recursive mapping](mapping/nesting.md) · [Name adaptation](mapping/names.md) · [Construction](mapping/construction-paths.md) · [Multi-row mapping](mapping/collections.md) · [Grouping](mapping/grouping.md) · [Tuples](mapping/tuples.md) · [Dynamic rows](mapping/dynamic-rows.md) · [Database NULL](mapping/nulls.md)

### Conditional SQL

[Variables](conditional-sql/variables.md) · [Markers](conditional-sql/markers.md) · [Collections](conditional-sql/collections.md) · [Dynamic projection](conditional-sql/dynamic-projection.md) · [Handlers](conditional-sql/handlers.md) · [Cheat sheet](conditional-sql/cheatsheet.md)

### Advanced customization

[Customization](customization/index.md) · [Type registration](customization/type-registration.md) · [Multi-row mapping](customization/multi-row.md) · [Result parsers](customization/result-parsers.md) · [Parameters](customization/parameters.md) · [Method caller](customization/method-caller.md) · [Caches](customization/caches.md)

### Code generation

[Code generation](codegen/index.md) · [Configure](codegen/configure.md) · [Queries](codegen/queries.md) · [Generated code](codegen/generated-code.md) · [Refresh](codegen/refresh.md) · [Analyzers](codegen/analyzers.md)

### Tracking

[Tracking](tracking/index.md) · [Runtime tracking](tracking/runtime.md) · [Items](tracking/items.md) · [Lists](tracking/lists.md) · [Binding](tracking/binding.md) · [Validation](tracking/validation.md) · [Persistence](tracking/persistence.md)

### Reference

[Coming from Dapper](reference/dapper.md) · [Errors](reference/errors.md) · [FAQ](reference/faq.md) · [Performance](reference/performance.md)
