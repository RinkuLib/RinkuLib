# Rinku documentation

Rinku is a micro ORM for .NET built on ADO.NET. SQL stays explicit instead of being generated from an object model. Rinku adapts between database-facing and .NET-facing shapes so both sides can keep the form that fits them best.

## Install

Add the `Rinku` NuGet package to your .NET project.

```bash
dotnet add package Rinku
```

You can also install `Rinku` from your IDE's NuGet package manager.

## First query

```csharp
public record Album(int Id, string Title) : IDbReadable;

static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
```

## Start here

**Continue with the [Rinku overview](https://rinkulib.github.io/RinkuLib/articles/overview.html).**

It is a fast tour of the main query, mapping, conditional SQL, code generation, execution, and tracking forms.

## Documentation map

The links below are useful when reading the Markdown directly or when you already know which topic you need.

### Running queries

[Execute and query SQL](https://rinkulib.github.io/RinkuLib/articles/running-queries/execution.html) · [Supplying values](https://rinkulib.github.io/RinkuLib/articles/running-queries/values.html) · [Builders](https://rinkulib.github.io/RinkuLib/articles/running-queries/builders.html) · [Result shapes](https://rinkulib.github.io/RinkuLib/articles/running-queries/result-shapes.html) · [Async](https://rinkulib.github.io/RinkuLib/articles/running-queries/async.html) · [Streaming](https://rinkulib.github.io/RinkuLib/articles/running-queries/streaming.html) · [Execution context](https://rinkulib.github.io/RinkuLib/articles/running-queries/execution-context.html) · [Multiple result sets](https://rinkulib.github.io/RinkuLib/articles/running-queries/multiple-results.html) · [Stored procedures](https://rinkulib.github.io/RinkuLib/articles/running-queries/stored-procedures.html) · [Existing DbCommand](https://rinkulib.github.io/RinkuLib/articles/running-queries/dbcommand.html) · [Fixed result schema](https://rinkulib.github.io/RinkuLib/articles/running-queries/fixed-result-schema.html) · [Raw readers](https://rinkulib.github.io/RinkuLib/articles/running-queries/readers.html) · [SQL-string shortcuts](https://rinkulib.github.io/RinkuLib/articles/running-queries/sql-string.html) · [IDbConnection support](https://rinkulib.github.io/RinkuLib/articles/running-queries/idbconnection.html) · [Parameter metadata](https://rinkulib.github.io/RinkuLib/articles/running-queries/parameter-metadata.html)

### Mapping

[Objects](https://rinkulib.github.io/RinkuLib/articles/mapping/objects.html) · [Construction paths](https://rinkulib.github.io/RinkuLib/articles/mapping/construction-paths.html) · [Nested objects](https://rinkulib.github.io/RinkuLib/articles/mapping/nesting.html) · [Collections](https://rinkulib.github.io/RinkuLib/articles/mapping/collections.html) · [Grouping](https://rinkulib.github.io/RinkuLib/articles/mapping/grouping.html) · [Adapt names](https://rinkulib.github.io/RinkuLib/articles/mapping/names.html) · [Database NULL](https://rinkulib.github.io/RinkuLib/articles/mapping/nulls.html) · [Tuples](https://rinkulib.github.io/RinkuLib/articles/mapping/tuples.html) · [Dynamic rows](https://rinkulib.github.io/RinkuLib/articles/mapping/dynamic-rows.html) · [Reading order](https://rinkulib.github.io/RinkuLib/articles/mapping/reading-order.html) · [Registration](https://rinkulib.github.io/RinkuLib/articles/mapping/registration.html)

### Conditional SQL

[Variables](https://rinkulib.github.io/RinkuLib/articles/conditional-sql/variables.html) · [Collections](https://rinkulib.github.io/RinkuLib/articles/conditional-sql/collections.html) · [Markers](https://rinkulib.github.io/RinkuLib/articles/conditional-sql/markers.html) · [Dynamic projection](https://rinkulib.github.io/RinkuLib/articles/conditional-sql/dynamic-projection.html) · [Handlers](https://rinkulib.github.io/RinkuLib/articles/conditional-sql/handlers.html) · [Template syntax](https://rinkulib.github.io/RinkuLib/articles/conditional-sql/template-syntax.html) · [Cheat sheet](https://rinkulib.github.io/RinkuLib/articles/conditional-sql/cheatsheet.html)

### Advanced customization

[Overview](https://rinkulib.github.io/RinkuLib/articles/customization/index.html) · [Type registrations](https://rinkulib.github.io/RinkuLib/articles/customization/type-registration.html) · [Mapping slot rules](https://rinkulib.github.io/RinkuLib/articles/customization/slot-rules.html) · [Multi-row mappings](https://rinkulib.github.io/RinkuLib/articles/customization/multi-row.html) · [Complete result parsers](https://rinkulib.github.io/RinkuLib/articles/customization/result-parsers.html) · [Parameter source rules](https://rinkulib.github.io/RinkuLib/articles/customization/parameter-members.html) · [Parameter binding](https://rinkulib.github.io/RinkuLib/articles/customization/parameters.html) · [Method caller](https://rinkulib.github.io/RinkuLib/articles/customization/method-caller.html) · [Conditional SQL handlers](https://rinkulib.github.io/RinkuLib/articles/customization/conditional-sql.html) · [Cache control](https://rinkulib.github.io/RinkuLib/articles/customization/caches.html)

### Code generation

[Overview](https://rinkulib.github.io/RinkuLib/articles/codegen/index.html) · [Configure](https://rinkulib.github.io/RinkuLib/articles/codegen/configure.html) · [Add queries](https://rinkulib.github.io/RinkuLib/articles/codegen/queries.html) · [Generated code](https://rinkulib.github.io/RinkuLib/articles/codegen/generated-code.html) · [Refresh](https://rinkulib.github.io/RinkuLib/articles/codegen/refresh.html) · [Configuration reference](https://rinkulib.github.io/RinkuLib/articles/codegen/configuration.html) · [Analyzers and code fixes](https://rinkulib.github.io/RinkuLib/articles/codegen/analyzers.html)

### Tracking

[Overview](https://rinkulib.github.io/RinkuLib/articles/tracking/index.html) · [Editable items](https://rinkulib.github.io/RinkuLib/articles/tracking/items.html) · [Tracking lists](https://rinkulib.github.io/RinkuLib/articles/tracking/lists.html) · [Runtime tracking](https://rinkulib.github.io/RinkuLib/articles/tracking/runtime.html) · [Binding](https://rinkulib.github.io/RinkuLib/articles/tracking/binding.html) · [Validation and metadata](https://rinkulib.github.io/RinkuLib/articles/tracking/validation.html) · [Persistence](https://rinkulib.github.io/RinkuLib/articles/tracking/persistence.html)

### Reference

[Coming from Dapper](https://rinkulib.github.io/RinkuLib/articles/reference/dapper.html) · [FAQ](https://rinkulib.github.io/RinkuLib/articles/reference/faq.html) · [Errors](https://rinkulib.github.io/RinkuLib/articles/reference/errors.html) · [Performance](https://rinkulib.github.io/RinkuLib/articles/reference/performance.html) · [API reference](https://rinkulib.github.io/RinkuLib/api/index.html)
