# RinkuLib
[![Rinku](https://img.shields.io/nuget/v/Rinku)](https://www.nuget.org/packages/Rinku/)
[![Rinku](https://img.shields.io/nuget/dt/Rinku)](https://www.nuget.org/packages/Rinku/)

A micro-ORM for .NET built directly on ADO.NET. You keep control of the SQL, and the requested result type selects the parsing and mapping behavior.

Get the package from [NuGet](https://www.nuget.org/packages/Rinku/) and read the [documentation](https://rinkulib.github.io/RinkuLib/).

```csharp
public record Album(int Id, string Title);

// Create the command once (a static readonly field is ideal). The SQL template is parsed here.
static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 1 });
```

The same command can use different result parsers. The included parsers handle values, objects, nested objects, buffered collections, streams, and wrappers that check result counts. More parsers and mapping rules can be added. Rinku also includes conditional SQL, code generation, and tracking.

## Install

```bash
dotnet add package Rinku
```

Targets **.NET 8** and **.NET 10**. The compile-time analyzers ship inside the package, no separate install.

## Pick the result shape

The type argument chooses the result parser. A class, record, or struct can be used directly when the columns match a constructor or writable members. Nested types follow separate registration rules. Parsers can also be added or replaced.

```csharp
Album first               = GetAlbums.Query<Album>(cnn, new { artistId = 1 });              // first album
IEnumerable<Album> stream = GetAlbums.Query<IEnumerable<Album>>(cnn, new { artistId = 1 }); // streamed
```

For a value returned by an execution, `ExecuteScalar<T>` returns it. A scalar
`SELECT` can use `Query<T>` as a normal one-row read. See [result shapes](https://rinkulib.github.io/RinkuLib/articles/running-queries/result-shapes.html).

## Map onto nested types

Flat rows fill nested shapes by column name.

```csharp
public record Customer(int Id, string Name) : IDbReadable;
public record Invoice(int Id, decimal Total, Customer Customer);

static readonly QueryCommand GetInvoices = new("SELECT i.InvoiceId AS Id, i.Total, i.CustomerId, c.FirstName AS CustomerName FROM invoices i JOIN customers c ON c.CustomerId = i.CustomerId");

List<Invoice> invoices = GetInvoices.Query<List<Invoice>>(cnn);
// each Invoice.Customer is filled from CustomerId and CustomerName
```

When a mapped type contains a registered collection and the rows provide a usable group boundary, Rinku can fold repeated join rows into nested results.

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, List<Album> Albums);

static readonly QueryCommand GetArtists = new("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumsId, al.Title AS AlbumsTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId");

List<Artist> artists = GetArtists.Query<List<Artist>>(cnn);
// artists[0].Albums holds the albums gathered from that artist's rows
```

## Make parts of the SQL optional

Mark the optional parts of a template, and the values you supply decide what stays.

```csharp
static readonly QueryCommand Search = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId AND Title LIKE ?@title");

// @title omitted, so its clause is pruned.
List<Album> albums = Search.Query<List<Album>>(cnn, new { artistId = 1 });
// SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

The template stays defined in one place. Application code selects its active parts without concatenating SQL, and a [builder](https://rinkulib.github.io/RinkuLib/articles/running-queries/builders.html) is available when those choices are made step by step.

## Query from the SQL string

As an alternative to declaring the command, hand the SQL to the connection. The command is built once and cached by the string.

```csharp
List<Album> albums = cnn.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId", new { artistId = 1 });
```

Already using Dapper? The [Coming from Dapper](https://rinkulib.github.io/RinkuLib/articles/reference/dapper.html) guide maps its common operations to Rinku and explains the behavioral differences.

## Documentation

Full docs at <https://rinkulib.github.io/RinkuLib/> (or browse the [documentation overview](docs/articles/index.md) in the repo).

- [Running queries](https://rinkulib.github.io/RinkuLib/articles/running-queries/values.html). Inputs, result shapes, streaming, execution context, and command forms
- [Mapping](https://rinkulib.github.io/RinkuLib/articles/mapping/objects.html). Construction, nesting, collections, names, nullability, and registration
- [Conditional SQL](https://rinkulib.github.io/RinkuLib/articles/conditional-sql/variables.html). One template that adapts to its inputs
- [Advanced customization](https://rinkulib.github.io/RinkuLib/articles/customization/index.html). Registration, parsers, parameters, handlers, and caches
- [Code generation](https://rinkulib.github.io/RinkuLib/articles/codegen/index.html) and [tracking](https://rinkulib.github.io/RinkuLib/articles/tracking/index.html)
- [Coming from Dapper](https://rinkulib.github.io/RinkuLib/articles/reference/dapper.html)
- [Benchmarks](https://rinkulib.github.io/RinkuLib/articles/reference/performance.html)

## Performance

The performance reference contains the current BenchmarkDotNet comparison and instructions to reproduce it on your own database.

## License

Apache-2.0. See [LICENSE](LICENSE).
