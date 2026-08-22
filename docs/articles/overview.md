# Rinku overview

Install the package and run a query first.

```bash
dotnet add package Rinku
```

```csharp
using Rinku;

public record Album(int Id, string Title) : IDbReadable;

static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums");

List<Album> albums = GetAlbums.Query<List<Album>>(cnn);
```

A reusable `QueryCommand` owns the SQL template. Per call values and builder state stay outside the command.

## Pass values

```csharp
static readonly QueryCommand GetAlbum = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId");

Album album = GetAlbum.Query<Album>(cnn, new { albumId = 7 });
```

Readable public members supply values by name. See [supplying values](running-queries/values.md).

## Choose the result shape

```csharp
Album first = GetAlbum.Query<Album>(cnn, new { albumId = 7 });
Optional<Album> maybe = GetAlbum.Query<Optional<Album>>(cnn, new { albumId = 7 });
Single<Album> one = GetAlbum.Query<Single<Album>>(cnn, new { albumId = 7 });
List<Album> all = GetAlbums.Query<List<Album>>(cnn);
IEnumerable<Album> stream = GetAlbums.Query<IEnumerable<Album>>(cnn);
```

The requested type controls result count behavior and buffering. See [result shapes](running-queries/result-shapes.md).

## Build values from application logic

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@ArtistId AND Title LIKE ?@Title");

var search = SearchAlbums.StartBuilder();

if (artistId is int id)
    search.Use("@ArtistId", id);

if (!string.IsNullOrWhiteSpace(title))
    search.Use("@Title", title);

List<Album> albums = search.Query<List<Album>>(cnn);
```

A builder holds mutable state for one execution flow. The shared `QueryCommand` stays reusable. See [builders](running-queries/builders.md).

## Map nested objects

```csharp
public record Artist(int Id, string Name) : IDbReadable;
public record AlbumWithArtist(int Id, string Title, Artist Artist);

static readonly QueryCommand GetAlbumsWithArtist = new("SELECT al.AlbumId AS Id, al.Title, ar.ArtistId AS ArtistId, ar.Name AS ArtistName FROM albums al JOIN artists ar ON ar.ArtistId = al.ArtistId");

List<AlbumWithArtist> albums = GetAlbumsWithArtist.Query<List<AlbumWithArtist>>(cnn);
```

The `ArtistId` and `ArtistName` columns belong to the nested `Artist`. See [nested objects](mapping/nesting.md).

## Fold joined rows into collections

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record ArtistWithAlbums(int Id, string Name, List<Album> Albums);

static readonly QueryCommand GetArtists = new("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumsId, al.Title AS AlbumsTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId");

List<ArtistWithAlbums> artists = GetArtists.Query<List<ArtistWithAlbums>>(cnn);
```

Repeated parent rows can fill a nested collection when Rinku has a usable group boundary. See [collections](mapping/collections.md) and [grouping](mapping/grouping.md).

## Handle database NULL

```csharp
static readonly QueryCommand GetYear = new("SELECT ReleaseYear FROM albums WHERE AlbumId = @albumId");

int? year = GetYear.Query<int?>(cnn, new { albumId = 7 });
```

Database `NULL` and no result are separate choices. See [database NULL](mapping/nulls.md) and [result shapes](running-queries/result-shapes.md).

## Make SQL optional

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@artistId AND Title LIKE ?@title");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

The missing `title` removes its condition. See [conditional variables](conditional-sql/variables.md).

## Expand a collection

```csharp
static readonly QueryCommand GetByIds = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (@ids_X)");

List<Album> albums = GetByIds.Query<List<Album>>(cnn, new { ids = new[] { 2, 5, 9 } });
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (@ids_1, @ids_2, @ids_3)
```

See [collection expansion](conditional-sql/collections.md) for lists used inside conditional SQL.

## Execute SQL

```csharp
static readonly QueryCommand RenameAlbum = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId");

int affected = RenameAlbum.Execute(cnn, new { albumId = 7, title = "Blue" });
```

Use `ExecuteScalar<T>` when execution also returns one value. See [executing SQL](running-queries/execution.md).

## Run asynchronously

```csharp
List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(cnn, ct: cancellationToken);
```

```csharp
await foreach (Album album in GetAlbums.StreamQueryAsync<Album>(cnn, ct: cancellationToken))
    Console.WriteLine(album.Title);
```

See [async execution](running-queries/async.md) and [streaming](running-queries/streaming.md).

## Use a transaction

```csharp
using DbTransaction transaction = cnn.BeginTransaction();

RenameAlbum.Execute(cnn, new { albumId = 7, title = "Blue" }, transaction: transaction);

transaction.Commit();
```

See [transactions, timeouts, and cancellation](running-queries/execution-context.md).

## Read several result sets

```csharp
static readonly QueryCommand GetArtist = new("SELECT ArtistId AS Id, Name FROM artists WHERE ArtistId = @artistId; SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

using MultiReader results = GetArtist.ExecuteMultiReader(cnn, new { artistId = 7 });

Artist artist = results.Query<Artist>();
List<Album> albums = results.Query<List<Album>>();
```

See [multiple result sets](running-queries/multiple-results.md) for reading several result sets from one command.

## Call a stored procedure

```csharp
static readonly QueryCommand GetAlbumsForArtist = new("GetAlbumsForArtist", ["artistId"]);

List<Album> albums = GetAlbumsForArtist.Query<List<Album>>(cnn, new { artistId = 7 });
```

See [stored procedures](running-queries/stored-procedures.md) for procedure calls and output values.

## Use an existing DbCommand

```csharp
static readonly CachedTypeParser<Album> AlbumParser = new();

using DbCommand command = cnn.CreateCommand();
command.CommandText = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = 7";

Album album = AlbumParser.Query(command);
```

See [existing DbCommand](running-queries/dbcommand.md) when the application already owns the command instance.

See [fixed result schema](running-queries/fixed-result-schema.md) when compatible columns are read as several result types.

## Read rows yourself

```csharp
DbDataReader reader = GetAlbums.ExecuteReader(cnn, out DbCommand command);

using (command)
using (reader)
{
    while (reader.Read())
        Console.WriteLine(reader.GetValue(0));
}
```

See [raw readers](running-queries/readers.md) when application code needs the provider reader directly.

## Generate database commands

Rinku Power Tools can inspect configured database commands and generate typed `DbCommand` methods and result records.

```csharp
static readonly CachedTypeParser<List<GetAlbumsByArtistResult>> Parser = new();

List<GetAlbumsByArtistResult> albums = Parser.Query(cnn.GetAlbumsByArtist(artistId: 7));
```

The generated method creates a normal `DbCommand`. See [code generation](codegen/index.md) for configuration, query sources, generated results, and refresh behavior.

Generated result records also carry schema metadata that can be tracked by the analyzers shipped in `Rinku`. See [analyzers and code fixes](codegen/analyzers.md) for schema links, constructor contracts, and method invocation generation.

## Extend Rinku

Normal usage pages show the built in choices first. Use [advanced customization](customization/index.md) when you need a new result parser, mapping rule, parameter rule, or conditional SQL handler.

## Track application edits

```csharp
using Rinku.Tracking;
using Rinku.Tracking.Runtime;

Album original = new(12, "Blue");
IRuntimeTrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(original);

edit.Set(nameof(Album.Title), "Kind of Blue");
edit.ConfirmEdit();
```

See [tracking](tracking/index.md) for editable items, structural list changes, and binding support.
