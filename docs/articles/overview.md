# Rinku overview

This page is a fast tour of Rinku. The examples intentionally use different forms so you can see the range of the library without reading every detailed guide first.

## Query and shape results

A reusable `QueryCommand` keeps the SQL template separate from the values used for one call.

```csharp
public record Album(int Id, string Title) : IDbReadable;

static readonly QueryCommand GetAlbum = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId");
static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

Album album = GetAlbum.Query<Album>(cnn, new { albumId = 12 });
List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
```

The requested type chooses how the result is consumed.

```csharp
Album first = GetAlbums.Query<Album>(cnn, new { artistId = 7 });
Album? maybe = GetAlbum.Query<Optional<Album>>(cnn, new { albumId = 999 });
Album exactlyOne = GetAlbum.Query<Single<Album>>(cnn, new { albumId = 12 });
Album[] array = GetAlbums.Query<Album[]>(cnn, new { artistId = 7 });
IEnumerable<Album> stream = GetAlbums.Query<IEnumerable<Album>>(cnn, new { artistId = 7 });
```

Use the SQL-string shortcut when a separately owned `QueryCommand` adds no value.

```csharp
List<Album> recent = cnn.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums WHERE ReleaseYear >= @fromYear", new { fromYear = 2020 });
// The exact SQL string is cached as a reusable QueryCommand.
```

Database `NULL` and no returned row are separate choices.

```csharp
string? title = GetNullableTitle.Query<MaybeNull<string>>(cnn);
// A row is required, database NULL is accepted.

OptionalNullable<string> maybeTitle = FindNullableTitle.Query<OptionalNullable<string>>(cnn);
// No row and database NULL are both accepted.
```

See [execute and query SQL](https://rinkulib.github.io/RinkuLib/articles/running-queries/execution.html), [result shapes](https://rinkulib.github.io/RinkuLib/articles/running-queries/result-shapes.html), [SQL-string shortcuts](https://rinkulib.github.io/RinkuLib/articles/running-queries/sql-string.html), and [database NULL](https://rinkulib.github.io/RinkuLib/articles/mapping/nulls.html).

## Adapt the mapping

If SQL is the easiest side to shape, use normal SQL aliases.

```sql
SELECT customer_id AS Id, display_name AS Name FROM customers
```

If the database-facing names should stay unchanged, adapt the .NET side instead.

```csharp
public record Customer([Alt("customer_id")] int Id, [Alt("display_name")] string Name);
```

Nested objects use the same mapping rules.

```csharp
public record Artist(int Id, string Name) : IDbReadable;
public record AlbumWithArtist(int Id, string Title, Artist Artist);

static readonly QueryCommand GetAlbumsWithArtist = new("SELECT al.AlbumId AS Id, al.Title, ar.ArtistId AS ArtistId, ar.Name AS ArtistName FROM albums al JOIN artists ar ON ar.ArtistId = al.ArtistId");

List<AlbumWithArtist> albums = GetAlbumsWithArtist.Query<List<AlbumWithArtist>>(cnn);
// ArtistId and ArtistName fill AlbumWithArtist.Artist.
```

Repeated join rows can fill nested collections.

```csharp
public record ArtistWithAlbums(int Id, string Name, List<Album> Albums);

static readonly QueryCommand GetArtists = new("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumsId, al.Title AS AlbumsTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId");

List<ArtistWithAlbums> artists = GetArtists.Query<List<ArtistWithAlbums>>(cnn);
// Consecutive rows for one artist become one ArtistWithAlbums.
// AlbumsId and AlbumsTitle fill its Albums collection.
```

When neither SQL nor the model should carry the naming rule, register it at the boundary.

```csharp
TypeParsingInfo.GetOrAdd<Customer>().UpdateAltName(names =>
    names.GetDefaultName() switch
    {
        "Id" => new NameComparer("customer_id"),
        "Name" => new NameComparer("display_name"),
        _ => null
    });
```

See [object mapping](https://rinkulib.github.io/RinkuLib/articles/mapping/objects.html), [adapt names](https://rinkulib.github.io/RinkuLib/articles/mapping/names.html), [nested objects](https://rinkulib.github.io/RinkuLib/articles/mapping/nesting.html), [collections](https://rinkulib.github.io/RinkuLib/articles/mapping/collections.html), and [grouping](https://rinkulib.github.io/RinkuLib/articles/mapping/grouping.html).

## Build one execution

Conditional variables let one SQL template adapt to the values supplied for a call.

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@artistId AND Title LIKE ?@title AND /*CurrentOnly*/IsArchived = 0");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
// title is absent and CurrentOnly is inactive.
// SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

A builder holds the mutable state for one execution flow.

```csharp
var search = SearchAlbums.StartBuilder();

if (artistId is int id)
    search.Use('@', nameof(artistId), id);

if (!string.IsNullOrWhiteSpace(title))
    search.Use("@title", title);

if (!canSeeArchived)
    search.Use("CurrentOnly");

List<Album> albums = search.Query<List<Album>>(cnn);
```

The same builder can take a whole source object.

```csharp
var search = SearchAlbums.StartBuilder();
search.UseWith(filter);
search.Use("@artistId", restrictedArtistId);
// The explicit value wins for this builder state.
```

Collections expand into normal database parameters.

```csharp
static readonly QueryCommand GetByIds = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (@ids_X)");

int[] ids = [2, 5, 9];
List<Album> albums = GetByIds.Query<List<Album>>(cnn, new { ids });
// SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (@ids_0, @ids_1, @ids_2)
```

See [builders](https://rinkulib.github.io/RinkuLib/articles/running-queries/builders.html), [conditional variables](https://rinkulib.github.io/RinkuLib/articles/conditional-sql/variables.html), [markers](https://rinkulib.github.io/RinkuLib/articles/conditional-sql/markers.html), and [collection expansion](https://rinkulib.github.io/RinkuLib/articles/conditional-sql/collections.html).

## Use the database surface you already have

A stored procedure can be declared directly when its parameters are already known.

```csharp
static readonly QueryCommand GetAlbumsForArtist = new("GetAlbumsForArtist", ["artistId"]);

List<Album> albums = GetAlbumsForArtist.Query<List<Album>>(cnn, new { artistId = 7 });
```

Or discover its metadata once and reuse it for subsequent calls.

```csharp
QueryCommand renumberAlbums = QueryCommand.FromProc("RenumberAlbums", setupConnection);
```

A long-lived parser cache can consume an existing `DbCommand` from any source.

```csharp
static readonly CachedTypeParser<Album> AlbumParser = new();

using DbCommand command = cnn.CreateCommand();
command.CommandText = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = 12";

Album album = AlbumParser.Query(command);
```

Power Tools can generate provider-neutral `DbCommand` methods from configured SQL, SQL files, and stored procedures.

```csharp
static readonly CachedTypeParser<List<GetAlbumsByArtistResult>> GeneratedAlbumsParser = new();

List<GetAlbumsByArtistResult> albums = GeneratedAlbumsParser.Query(cnn.GetAlbumsByArtist(artistId: 7));
```

Generated result records can carry schema metadata used by the analyzers shipped with `Rinku`.

```csharp
/// <Schema LastUpdated="2026-08-21T14:00Z" />
public partial record GetAlbumResult(int Id, string Title);

/// <BasedOn cref="GetAlbumResult" LastUpdated="2026-08-21T14:00Z" />
public record AlbumDto(int Id, string Title);
```

See [stored procedures](https://rinkulib.github.io/RinkuLib/articles/running-queries/stored-procedures.html), [existing DbCommand](https://rinkulib.github.io/RinkuLib/articles/running-queries/dbcommand.html), [code generation](https://rinkulib.github.io/RinkuLib/articles/codegen/index.html), and [analyzers](https://rinkulib.github.io/RinkuLib/articles/codegen/analyzers.html).

## Choose how execution runs

The same command model works across normal execution, scalar results, async work, streaming, transactions, and multiple result sets.

```csharp
static readonly QueryCommand RenameAlbum = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId");
static readonly QueryCommand CountAlbums = new("SELECT COUNT(*) FROM albums WHERE ArtistId = @artistId");

int affected = RenameAlbum.Execute(cnn, new { albumId = 12, title = "Kind of Blue" });
int count = CountAlbums.Query<int>(cnn, new { artistId = 7 });
```

```csharp
List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(cnn, new { artistId = 7 }, ct: cancellationToken);

await foreach (Album album in GetAlbums.StreamQueryAsync<Album>(cnn, new { artistId = 7 }, ct: cancellationToken))
    Console.WriteLine(album.Title);
```

```csharp
using DbTransaction transaction = cnn.BeginTransaction();

RenameAlbum.Execute(cnn, new { albumId = 12, title = "Kind of Blue" }, transaction: transaction);
UpdateArtist.Execute(cnn, new { artistId = 7, modifiedAt = DateTime.UtcNow }, transaction: transaction);
transaction.Commit();
```

```csharp
static readonly QueryCommand GetDashboard = new("SELECT ArtistId AS Id, Name FROM artists WHERE ArtistId = @artistId; SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

using MultiReader results = GetDashboard.ExecuteMultiReader(cnn, new { artistId = 7 });
Artist artist = results.Query<Artist>();
List<Album> albums = results.Query<List<Album>>();
```

You can also drop to the provider reader when that is the right level.

```csharp
DbDataReader reader = GetAlbums.ExecuteReader(cnn, out DbCommand command, new { artistId = 7 });

using (command)
using (reader)
{
    while (reader.Read())
        Console.WriteLine(reader.GetString(1));
}
```

See [async execution](https://rinkulib.github.io/RinkuLib/articles/running-queries/async.html), [streaming](https://rinkulib.github.io/RinkuLib/articles/running-queries/streaming.html), [execution context](https://rinkulib.github.io/RinkuLib/articles/running-queries/execution-context.html), [multiple result sets](https://rinkulib.github.io/RinkuLib/articles/running-queries/multiple-results.html), and [raw readers](https://rinkulib.github.io/RinkuLib/articles/running-queries/readers.html).

## Track application state

Tracking keeps the accepted value separate from the current edit without generating persistence SQL.

```csharp
Album original = new(12, "Blue");
IRuntimeTrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(original);

edit.Set(nameof(Album.Title), "Kind of Blue");

Console.WriteLine(original.Title);                         // Blue
Console.WriteLine(edit.Get<string>(nameof(Album.Title))); // Kind of Blue
```

Persist explicitly, then confirm the edit only after persistence succeeds.

```csharp
static readonly QueryCommand UpdateAlbum = new("UPDATE albums SET Title = @Title WHERE AlbumId = @Id");

if (edit.HasChanges())
{
    UpdateAlbum.Execute(cnn, edit);
    edit.ConfirmEdit();
}
```

Generated edits can expose normal typed properties instead of runtime member names.

```csharp
public interface IAlbumEdit : IRuntimeTrackingItem<Album>
{
    string Title { get; set; }
}

RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();
IAlbumEdit typedEdit = options.GetRegistration<IAlbumEdit>().Create(original);
typedEdit.Title = "Kind of Blue";
```

Lists add structural tracking around the items.

```csharp
List<Album> source = GetAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
TrackingList<IRuntimeTrackingItem<Album>> tracked = source.ToTrackingList();

IRuntimeTrackingItem<Album> added = tracked.AddNew();
added.Set(nameof(Album.Title), "New album");
tracked.RemoveAt(0);

Console.WriteLine(tracked.AddedCount);
Console.WriteLine(tracked.RemovedCount);
```

See [tracking](https://rinkulib.github.io/RinkuLib/articles/tracking/index.html), [editable items](https://rinkulib.github.io/RinkuLib/articles/tracking/items.html), [runtime tracking](https://rinkulib.github.io/RinkuLib/articles/tracking/runtime.html), [tracking lists](https://rinkulib.github.io/RinkuLib/articles/tracking/lists.html), and [persistence](https://rinkulib.github.io/RinkuLib/articles/tracking/persistence.html).

## Extend the boundary

Use normal mapping first. When a type genuinely needs another rule, registration can change how it is interpreted without changing the query or the database shape.

```csharp
public readonly record struct PositionalValue<T>(T Value);

TypeParsingInfo.AddOrSet(typeof(PositionalValue<>), CtorTypeInfo.Instance);

static readonly QueryCommand CountAllAlbums = new("SELECT COUNT(*) FROM albums");
PositionalValue<int> count = CountAllAlbums.Query<PositionalValue<int>>(cnn);
```

Parameter binding, complete result parsers, multi-row mappings, method adaptation, conditional SQL handlers, and cache control are also exposed when the built-in behavior is not enough.

See [advanced customization](https://rinkulib.github.io/RinkuLib/articles/customization/index.html).
