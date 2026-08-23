# Rinku

[![NuGet](https://img.shields.io/nuget/v/Rinku.svg)](https://www.nuget.org/packages/Rinku/) [![Documentation](https://img.shields.io/badge/docs-online-blue)](https://rinkulib.github.io/RinkuLib/)

Rinku is a small .NET micro ORM built on ADO.NET. SQL stays explicit while Rinku maps database-shaped results into .NET types and supports conditional SQL, code generation, and tracking.

## Install

```bash
dotnet add package Rinku
```

The examples below assume `cnn` is an open provider-specific `DbConnection`.

## First query

```csharp
using Rinku;

public record Album(int Id, string Title);

static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
```

The SQL string form uses the same cached `QueryCommand` lookup through the exact SQL string.

```csharp
List<Album> albums = cnn.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId", new { artistId = 7 });
```

[Overview](docs/articles/overview.md) · [Running queries](docs/articles/running-queries/execution.md) · [Mapping](docs/articles/mapping/objects.md) · [Conditional SQL](docs/articles/conditional-sql/variables.md)

## Map a type shape

```csharp
public record Artist(int Id, string Name) : IDbReadable;
public record AlbumWithArtist(int Id, string Title, Artist Artist);

QueryCommand getAlbumsWithArtist = new("SELECT al.AlbumId AS Id, al.Title, ar.ArtistId AS ArtistId, ar.Name AS ArtistName FROM albums al JOIN artists ar ON ar.ArtistId = al.ArtistId");

List<AlbumWithArtist> albums = getAlbumsWithArtist.Query<List<AlbumWithArtist>>(cnn);
```

Mapping continues through the type shape.

```csharp
public record Employee(int Id, string Name, [Alt("Boss")] Employee? Manager = null) : IDbReadable;

QueryCommand getEmployee = new("SELECT e.EmployeeId AS Id, e.Name, m.EmployeeId AS ManagerId, m.Name AS ManagerName, b.EmployeeId AS ManagerBossId, b.Name AS ManagerBossName FROM employees e LEFT JOIN employees m ON m.EmployeeId = e.ManagerId LEFT JOIN employees b ON b.EmployeeId = m.ManagerId WHERE e.EmployeeId = @employeeId");

Employee employee = getEmployee.Query<Employee>(cnn, new { employeeId = 12 });
// ManagerId fills employee.Manager.Id.
// ManagerBossId fills employee.Manager.Manager.Id.
// This constructor can finish without another Manager when the path ends.
```

[Objects](docs/articles/mapping/objects.md) · [Recursive mapping](docs/articles/mapping/nesting.md) · [Name adaptation](docs/articles/mapping/names.md) · [Construction](docs/articles/mapping/construction-paths.md)

## Fold several rows

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record ArtistWithAlbums(int Id, string Name, [Alt("Album")] List<Album> Albums);

QueryCommand getArtists = new("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumId, al.Title AS AlbumTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId");

List<ArtistWithAlbums> artists = getArtists.Query<List<ArtistWithAlbums>>(cnn);
// List<Album> folds consecutive rows inside each ArtistWithAlbums.
// Album still uses the same recursive mapping process.
```

[Multi-row mapping](docs/articles/mapping/collections.md) · [Grouping](docs/articles/mapping/grouping.md)

## Conditional SQL

```csharp
QueryCommand searchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@artistId AND Title LIKE ?@title");

List<Album> albums = searchAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
// SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId

albums = searchAlbums.Query<List<Album>>(cnn);
// SELECT AlbumId AS Id, Title FROM albums
```

[Conditional SQL](docs/articles/conditional-sql/variables.md) · [Cheat sheet](docs/articles/conditional-sql/cheatsheet.md)

## Documentation

[Documentation index](docs/articles/index.md) · [Overview](docs/articles/overview.md) · [Async queries](docs/articles/running-queries/async.md) · [Result shapes](docs/articles/running-queries/result-shapes.md)

[Builders](docs/articles/running-queries/builders.md) · [Stored procedures](docs/articles/running-queries/stored-procedures.md) · [Code generation](docs/articles/codegen/index.md) · [Tracking](docs/articles/tracking/index.md)

[Advanced customization](docs/articles/customization/index.md) · [FAQ](docs/articles/reference/faq.md) · [Coming from Dapper](docs/articles/reference/dapper.md) · [Online documentation](https://rinkulib.github.io/RinkuLib/)
