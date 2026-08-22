# Collections

Use `_X` to expand a collection into normal database parameters.

```csharp
static readonly QueryCommand GetAlbums = new("""
SELECT AlbumId AS Id, Title
FROM albums
WHERE AlbumId IN (@ids_X)
""");

int[] ids = [2, 5, 9];

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { ids });
```

The generated SQL uses one parameter for each item.

```sql
SELECT AlbumId AS Id, Title
FROM albums
WHERE AlbumId IN (@ids_0, @ids_1, @ids_2)
```

The values still use database parameters. They are not written directly into the SQL text.

## Optional collections

Add `?` when an empty or missing collection should remove the condition.

```csharp
static readonly QueryCommand SearchAlbums = new("""
SELECT AlbumId AS Id, Title
FROM albums
WHERE ArtistId = @artistId
AND AlbumId IN (?@ids_X)
""");

int[] ids = [];

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new
{
    artistId = 7,
    ids
});
```

The generated SQL no longer contains the `IN` condition.

```sql
SELECT AlbumId AS Id, Title
FROM albums
WHERE ArtistId = @artistId
```

## Required empty collections

A required `_X` value must contain at least one item.

```sql
SELECT AlbumId AS Id, Title
FROM albums
WHERE AlbumId IN (@ids_X)
```

An empty or missing `ids` value produces `RINKU2002`.

Use `?@ids_X` when an empty collection means that the filter should not be applied.

## Enumerable values

The handler accepts values from any enumerable sequence.

```csharp
IEnumerable<int> ids = [1, 4, 8];

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { ids });
```

## Repeated use

The same collection marker can appear more than once.

```sql
SELECT AlbumId AS Id, Title
FROM albums
WHERE AlbumId IN (@ids_X)
OR ParentAlbumId IN (@ids_X)
```

Rinku reuses the generated parameter set for that value.

## Builders

A bound builder can be reused with a different collection size.

```csharp
using DbCommand command = cnn.CreateCommand();
var builder = GetAlbums.StartBuilder(command);

builder.Use("ids", new[] { 1, 2 });
builder.Execute();

builder.Use("ids", new[] { 4, 5, 6, 7 });
builder.Execute();
```

See [Builders](../running-queries/builders.md) for per call state and bound commands.
