# Collection expansion

## Expand to database parameters

```csharp
static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (@ids_X)");

int[] ids = [2, 5, 9];
List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { ids });
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (@ids_0, @ids_1, @ids_2)
```

The elements remain database parameters.

## Optional empty collection

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId AND AlbumId IN (?@ids_X)");

int[] ids = [];
List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new { artistId = 7, ids });
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

## Required empty collection

```csharp
int[] ids = [];
List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { ids });
// RINKU2002
```

[Errors](../reference/errors.md#rinku2002-required-handler-value)

## Any enumerable

```csharp
IEnumerable<int> ids = [1, 4, 8];
List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { ids });
```

## Same collection used twice

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (@ids_X) OR ParentAlbumId IN (@ids_X)
```

The repeated value uses the same generated parameter set.

## Change collection size on a bound builder

```csharp
using DbCommand command = cnn.CreateCommand();
var builder = GetAlbums.StartBuilder(command);

builder.Use("@ids", new[] { 1, 2 });
builder.Execute();

builder.Use("@ids", new[] { 4, 5, 6, 7 });
builder.Execute();
```

[Builders](../running-queries/builders.md)
