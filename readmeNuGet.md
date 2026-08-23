# Rinku

Rinku is a small .NET micro ORM built on ADO.NET. It keeps SQL explicit and maps database-shaped results into .NET types, with support for conditional SQL, code generation, and tracking.

## Install

```bash
dotnet add package Rinku
```

The examples assume `cnn` is an open provider-specific `DbConnection`.

## Quick start

```csharp
using Rinku;

public record Album(int Id, string Title);

static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
```

```csharp
List<Album> albums = cnn.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId", new { artistId = 7 });
```

## Conditional SQL

```csharp
QueryCommand searchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@artistId AND Title LIKE ?@title");

List<Album> albums = searchAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
// SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

When `title` is not supplied, the optional predicate is omitted. See the [conditional SQL documentation](https://rinkulib.github.io/RinkuLib/articles/conditional-sql/variables.html) for the full marker syntax.

[Documentation](https://rinkulib.github.io/RinkuLib/) · [GitHub](https://github.com/RinkuLib/RinkuLib)
