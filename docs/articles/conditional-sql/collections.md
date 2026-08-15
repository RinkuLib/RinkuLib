# Expand a collection

The `_X` handler creates one database parameter for every item in a collection and writes their names into the SQL.

```csharp
static readonly QueryCommand AlbumsByGenre = new("SELECT AlbumId AS Id, Title FROM albums WHERE GenreId IN (@genreIds_X)");

List<Album> albums = AlbumsByGenre.Query<List<Album>>(cnn, new { genreIds = new[] { 1, 2, 3 } });
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE GenreId IN (@genreIds_1, @genreIds_2, @genreIds_3)
```

The provider receives three normal parameters whose values are `1`, `2`, and `3`. The original `@genreIds_X` marker is not sent to the database.

## Required collections

Without `?`, the collection is required and must contain at least one item.

```csharp
List<Album> albums = AlbumsByGenre.Query<List<Album>>(cnn, new { genreIds = Array.Empty<int>() });
// RINKU2002
```

Rinku raises the binding error before calling the provider. It never generates `IN ()`.

A missing or null collection has the same result.

```csharp
List<Album> albums = AlbumsByGenre.Query<List<Album>>(cnn);
// RINKU2002
```

## Optional collections

Add `?` when an absent or empty collection should remove its surrounding SQL.

```csharp
static readonly QueryCommand OptionalGenres = new("SELECT AlbumId AS Id, Title FROM albums WHERE GenreId IN (?@genreIds_X)");

List<Album> albums = OptionalGenres.Query<List<Album>>(cnn, new { genreIds = Array.Empty<int>() });
```

```sql
SELECT AlbumId AS Id, Title FROM albums
```

A non-empty collection keeps the condition.

```csharp
List<Album> albums = OptionalGenres.Query<List<Album>>(cnn, new { genreIds = new[] { 1, 2 } });
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE GenreId IN (@genreIds_1, @genreIds_2)
```

## Use any enumerable value

Arrays, lists, sets, and lazy `IEnumerable<T>` values can be expanded.

```csharp
HashSet<int> genreIds = [1, 2, 3];
List<Album> albums = AlbumsByGenre.Query<List<Album>>(cnn, new { genreIds });
```

```csharp
IEnumerable<int> genreIds = Enumerable.Range(1, 3).Where(id => id != 2);
List<Album> albums = AlbumsByGenre.Query<List<Album>>(cnn, new { genreIds });
```

A lazy sequence is enumerated while the execution command is prepared. Avoid a sequence whose enumeration has side effects or depends on mutable state.

## Reuse one expansion

The same collection marker can appear more than once. Every occurrence writes the same generated parameter names.

```csharp
static readonly QueryCommand Genres = new("SELECT GenreId AS Id, Name FROM genres WHERE GenreId IN (@ids_X) OR ParentGenreId IN (@ids_X)");

List<Genre> genres = Genres.Query<List<Genre>>(cnn, new { ids = new[] { 2, 5 } });
```

```sql
SELECT GenreId AS Id, Name FROM genres WHERE GenreId IN (@ids_1, @ids_2) OR ParentGenreId IN (@ids_1, @ids_2)
```

Only two provider parameters are created.

## Combine collections with other filters

Collection expansion composes with ordinary and conditional parameters.

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId AND GenreId IN (?@genreIds_X) AND ReleaseYear >= ?@minimumYear");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new { artistId = 7, genreIds = new[] { 1, 2 }, minimumYear = 2000 });
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId AND GenreId IN (@genreIds_1, @genreIds_2) AND ReleaseYear >= @minimumYear
```

Leaving `genreIds` empty removes only the genre condition. Leaving `minimumYear` null removes only the year condition.

## Reuse a bound command

A bound builder updates the numbered parameters when the collection size changes.

```csharp
using DbCommand command = cnn.CreateCommand();
var search = SearchAlbums.StartBuilder(command);

search.Use("@artistId", 7);
search.Use("@genreIds", new[] { 1, 2, 3 });
List<Album> first = search.Query<List<Album>>();

search.Use("@genreIds", new[] { 4 });
List<Album> second = search.Query<List<Album>>();
```

The second execution keeps one generated genre parameter and removes the extras from the command.

## Parameter metadata

Expanded elements share one cached parameter strategy. After the provider reports the first element's database metadata, later expansions reuse that strategy for every generated element.

```csharp
AlbumsByGenre.Parameters.Reset();
```

Resetting the command parameters also resets the cached strategy used by `_X` elements.

[Value handlers](handlers.md) covers `_N`, `_S`, `_R`, and handler errors. [Conditional variables](variables.md) explains how the surrounding SQL is removed. [Parameter metadata](../running-queries/parameter-metadata.md) covers learned and pinned database types.
