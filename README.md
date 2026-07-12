# RinkuLib: A Modular Micro-ORM
[![Rinku](https://img.shields.io/nuget/v/Rinku)](https://www.nuget.org/packages/Rinku/)
[![Rinku](https://img.shields.io/nuget/dt/Rinku)](https://www.nuget.org/packages/Rinku/)

A micro-ORM for .NET, built directly on **ADO.NET**. You write the SQL, name a type, and get your objects back.

```csharp
public record Album(int Id, string Title);

// Create the command once (a static readonly field is ideal). Parsing happens here.
static readonly QueryCommand GetAlbums =
    new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 1 });
```

The mapping runs from a single scalar to a nested object graph. Conditional SQL, design-time code generation, and change tracking build on it.

## Install

```bash
dotnet add package Rinku
```

Targets **.NET 8** and **.NET 10**. The compile-time analyzers ship inside the package, no separate install. See [installation](https://rinkulib.github.io/RinkuLib/articles/getting-started/installation.html).

## Pick the result shape

The type argument decides what comes back. `Album` is one of your own types, any class, record, or struct works, with no attributes and no configuration.

```csharp
Album one                 = GetAlbums.Query<Album>(cnn, new { artistId = 1 });              // one row
IEnumerable<Album> stream = GetAlbums.Query<IEnumerable<Album>>(cnn, new { artistId = 1 }); // streamed
```

For a single value, `ExecuteScalar<T>` returns it. See [running queries](https://rinkulib.github.io/RinkuLib/articles/running-queries/index.html).

## Map onto nested types

Flat rows fill nested shapes, matched by column name.

```csharp
public record Customer(int Id, string Name) : IDbReadable;
public record Invoice(int Id, decimal Total, Customer Customer);

static readonly QueryCommand GetInvoices = new(
    "SELECT i.InvoiceId AS Id, i.Total, i.CustomerId, c.FirstName AS CustomerName " +
    "FROM invoices i JOIN customers c ON c.CustomerId = i.CustomerId");

List<Invoice> invoices = GetInvoices.Query<List<Invoice>>(cnn);
// each Invoice.Customer is filled from CustomerId and CustomerName
```

## Make parts of the SQL optional

Mark the optional parts of a template, and the values you supply decide what stays.

```csharp
static readonly QueryCommand Search =
    new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId AND Title LIKE ?@title");

// @title omitted, so its clause is pruned.
List<Album> albums = Search.Query<List<Album>>(cnn, new { artistId = 1 });
// SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

You define the template up front, so your code only decides what's used and never concatenates SQL, the validity of the statement holds wherever a value lands. Mapping works the same way. A configurable negotiation maps the flat result onto the shape of your type, and the type decides how the columns nest and how many rows it takes. A [builder](https://rinkulib.github.io/RinkuLib/articles/running-queries/parameters.html) is available when you'd rather toggle conditions in C#.

## Query from the SQL string

As an alternative to declaring the command, hand the SQL to the connection. The command is built once and cached by the string.

```csharp
List<Album> albums = cnn.Query<List<Album>>(
    "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId", new { artistId = 1 });
```

## Documentation

Full docs at <https://rinkulib.github.io/RinkuLib/> (or browse [`docs/`](docs/index.md) in the repo).

- [Quick start](https://rinkulib.github.io/RinkuLib/articles/getting-started/quick-start.html)
- [Running queries, by example](https://rinkulib.github.io/RinkuLib/articles/running-queries/index.html)
- [Mapping engine](https://rinkulib.github.io/RinkuLib/articles/mapping/index.html). Negotiation, nullability, tuples, `DynaObject`
- [Conditional SQL](https://rinkulib.github.io/RinkuLib/articles/conditional-sql/index.html). One query that adapts to its input
- [Code generation](https://rinkulib.github.io/RinkuLib/articles/codegen/index.html) and [tracking](https://rinkulib.github.io/RinkuLib/articles/tracking/index.html)
- [Coming from Dapper](https://rinkulib.github.io/RinkuLib/articles/reference/dapper.html) and the [benchmarks](https://rinkulib.github.io/RinkuLib/articles/reference/performance.html)

## Performance

Measured with BenchmarkDotNet against a real database (lower is better, ratios relative to the Dapper baseline). Comparable time, consistently lower allocations, Rinku builds the `DbCommand` directly and reuses compiled, schema-keyed mappers.

| Method | Mean | Ratio | Allocated | Alloc Ratio |
|---|---|---|---|---|
| Dapper_QueryFirst | 526.8 us | 1.00 | 3.66 KB | 1.00 |
| Rinku_QueryT | 508.6 us | 0.97 | 3.07 KB | 0.84 |
| Dapper_QueryUnbuffered | 602.6 us | 1.00 | 20.84 KB | 1.00 |
| Rinku_QueryIEnumerable | 601.1 us | 1.00 | 15.46 KB | 0.74 |
| Dapper_Execute | 1,476.8 us | 1.00 | 2.33 KB | 1.00 |
| Rinku_Execute | 1,443.6 us | 0.98 | 1.76 KB | 0.76 |

The full 15-group comparison and how to reproduce it are in [the performance reference](https://rinkulib.github.io/RinkuLib/articles/reference/performance.html). RinkuLib began as a Dapper extension. Building the whole `DbCommand` efficiently is what led to adding the mapping side too.

## License

Apache-2.0. See [LICENSE](LICENSE).
