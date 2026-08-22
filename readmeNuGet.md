# Rinku

Rinku maps database results into the type requested by the caller while keeping SQL in application code.

## First query

```csharp
using Rinku;

public record Album(int Id, string Title) : IDbReadable;

static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
```

## Result shapes

```csharp
Album first = GetAlbum.Query<Album>(cnn, new { albumId = 12 });
Optional<Album> maybe = GetAlbum.Query<Optional<Album>>(cnn, new { albumId = 12 });
List<Album> all = GetAlbums.Query<List<Album>>(cnn);
IEnumerable<Album> streamed = GetAlbums.Query<IEnumerable<Album>>(cnn);
```

The requested type selects result count behavior and buffering.

## Conditional SQL

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@artistId AND Title LIKE ?@title");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
```

Without `title` the second condition is removed.

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

## Nested mapping

```csharp
public record Artist(int Id, string Name) : IDbReadable;
public record AlbumWithArtist(int Id, string Title, Artist Artist);

static readonly QueryCommand GetAlbums = new("SELECT al.AlbumId AS Id, al.Title, ar.ArtistId AS ArtistId, ar.Name AS ArtistName FROM albums al JOIN artists ar ON ar.ArtistId = al.ArtistId");

List<AlbumWithArtist> albums = GetAlbums.Query<List<AlbumWithArtist>>(cnn);
```

## Code generation

Rinku Power Tools can generate typed `DbCommand` methods from configured SQL, SQL files, and stored procedures.

```csharp
static readonly CachedTypeParser<List<GetAlbumsByArtistResult>> Parser = new();

List<GetAlbumsByArtistResult> albums = Parser.Query(cnn.GetAlbumsByArtist(artistId: 7));
```

## Analyzers and code fixes

The `Rinku` package includes analyzers and code fixes. They can track reviewed schemas with `BasedOn`, require constructor shapes with `MatchConstructor`, generate missing constructors, and complete method invocations.

```csharp
/// <Schema LastUpdated="2026-08-21T14:00Z" />
public record AlbumSchema(int Id, string Title);

/// <BasedOn cref="AlbumSchema" LastUpdated="2026-08-21T14:00Z" />
public record AlbumDto(int Id, string Title);
```

No separate analyzer package or PowerTools installation is required. See [analyzers and code fixes](https://rinkulib.github.io/RinkuLib/articles/codegen/analyzers.html).

## Async

```csharp
List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(cnn, ct: cancellationToken);
```

See the full [Rinku documentation](https://rinkulib.github.io/RinkuLib/articles/index.html) for queries, mapping, conditional SQL, customization, code generation, analyzers, tracking, errors, and the Dapper comparison.
