# Running queries

Define a command once, run it however you need. Each block below is a complete example.

## Define once, run

A `QueryCommand` is built from a SQL string and reused for the life of the app. Execution methods sit directly on it.

```csharp
using RinkuLib.Queries;
using RinkuLib.Commands;

public record Track(int Id, string Name, decimal UnitPrice);

static readonly QueryCommand GetTracks = new(
    "SELECT TrackId AS Id, Name, UnitPrice FROM tracks WHERE AlbumId = @albumId");

using DbConnection cnn = GetConnection();

List<Track> tracks = GetTracks.Query<List<Track>>(cnn, new { albumId = 1 });
```

The command holds no per-call state and is safe to share across threads. Per-call values travel in the arguments.

## The type argument picks the shape

Same command, different `T`, different result.

```csharp
List<Track> all       = GetTracks.Query<List<Track>>(cnn, new { albumId = 1 });        // buffered
IEnumerable<Track> it = GetTracks.Query<IEnumerable<Track>>(cnn, new { albumId = 1 }); // streamed
Track track           = GetTrackById.Query<Track>(cnn, new { id = 10 });               // one, throws if absent
Optional<Track> maybe = GetTrackById.Query<Optional<Track>>(cnn, new { id = 99 });     // one or empty
```

Every shape and its zero-row behavior is on [result shapes](result-shapes.md).

## Parameters

Pass any object whose public readable fields or properties match the parameter names, case-insensitive. Unmatched members are ignored. This includes anonymous types, ordinary classes, records, and structs.

```csharp
static readonly QueryCommand ByComposer = new(
    "SELECT TrackId AS Id, Name, UnitPrice FROM tracks WHERE Composer = @composer AND UnitPrice >= @minPrice");

var tracks = ByComposer.Query<List<Track>>(cnn, new { Composer = "AC/DC", MINPRICE = 0.99m });
```

When C# logic should set the values instead, use a builder. Both roads are covered on [supplying values](parameters.md).

```csharp
var b = ByComposer.StartBuilder();
b.Use("@composer", "AC/DC");
b.Use("@minPrice", 0.99m);
var tracks = b.Query<List<Track>>(cnn);
```

## Writes and scalars

`Execute` returns the affected-row count. `ExecuteScalar<T>` is for a command
whose operation is execution and which also returns one value, such as an
`INSERT ... RETURNING Id`. A `SELECT` that reads one scalar is also a normal
query, so `Query<T>` is valid there.

```csharp
static readonly QueryCommand UpdatePrice = new(
    "UPDATE tracks SET UnitPrice = @price WHERE TrackId = @id");
static readonly QueryCommand CountTracks = new("SELECT COUNT(*) FROM tracks");
static readonly QueryCommand InsertTrack = new(
    "INSERT INTO tracks (Name) VALUES (@name) RETURNING TrackId");

int affected = UpdatePrice.Execute(cnn, new { id = 10, price = 1.29m });
int total    = CountTracks.Query<int>(cnn);
int trackId  = InsertTrack.ExecuteScalar<int>(cnn, new { name = "New" });
```

## Async

Every method has an async version. `StreamQueryAsync` returns an `IAsyncEnumerable<T>`.

```csharp
List<Track> tracks = await GetTracks.QueryAsync<List<Track>>(cnn, new { albumId = 1 }, ct: token);

await foreach (Track t in GetTracks.StreamQueryAsync<Track>(cnn, new { albumId = 1 }, ct: token))
    Process(t);
```

## Transactions, timeouts, cancellation

The optional context arguments come after the parameter object.

```csharp
using var trans = cnn.BeginTransaction();
UpdatePrice.Execute(cnn, new { id = 10, price = 1.29m }, transaction: trans);
trans.Commit();

var slow = GetTracks.Query<List<Track>>(cnn, new { albumId = 1 }, timeout: 60);

var rows = await GetTracks.QueryAsync<List<Track>>(cnn, new { albumId = 1 }, ct: token);
```

## Set parameters manually

Use a `QueryBuilder` when the code assembles the parameter values manually.


```csharp
static readonly QueryCommand Search = new("""
    SELECT * FROM tracks WHERE ArtistId = @artistId AND 
    UnitPrice >= @minPrice AND Name LIKE CONCAT('%', @name, '%')
""");

var builder = Search.StartBuilder();
builder.Use("@artistId", artistId);
builder.Use("@minPrice", minimumPrice);
builder.Use('@', "name", namePattern);

List<Track> tracks = builder.Query<List<Track>>(cnn);
```

## Reuse one DbCommand across a batch

Bind the command once. Each `UseWith` replaces the parameter values before the
next execution.

```csharp
static readonly QueryCommand UpdatePlaylist = new(
    "UPDATE playlists SET Name = @name, IsPublic = @isPublic WHERE PlaylistId = @playlistId");

using var sqlCmd = cnn.CreateCommand();
var batch = UpdatePlaylist.StartBuilder(sqlCmd);
foreach (var playlist in playlists) {
    batch.UseWith(playlist);
    batch.Execute();
}
```

## Several result sets

One command, several selects, read in order.

```csharp
static readonly QueryCommand Dashboard = new(
    "SELECT * FROM artists WHERE ArtistId = @id; SELECT * FROM albums WHERE ArtistId = @id");

using var multi = Dashboard.ExecuteMultiReader(cnn, new { id = 1 });
Artist artist      = multi.Query<Artist>();
artist.Albums      = multi.Query<List<Album>>();
```

See [multiple result sets](multiple-results.md).

## A join folds into nested objects

A join repeats the parent on every child row.

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, List<Album> Albums);

static readonly QueryCommand GetArtists = new(
    "SELECT ar.Id, ar.Name, al.Id AS AlbumsId, al.Title AS AlbumsTitle " +
    "FROM artists ar JOIN albums al ON al.ArtistId = ar.Id ORDER BY ar.Id");

List<Artist> artists = GetArtists.Query<List<Artist>>(cnn);
// artists[0].Albums holds the albums gathered from that artist's rows
```

See [collections](collections.md).


## The SQL string on the connection

Skip declaring a `QueryCommand` and hand the SQL to the connection. It caches the command by the string. More on [the SQL string](sql-string.md).

```csharp
List<Track> tracks = cnn.Query<List<Track>>(
    "SELECT TrackId AS Id, Name, UnitPrice FROM tracks WHERE AlbumId = @albumId",
    new { albumId = 1 });
```

## Stored procedures

A procedure name has no SQL text for Rinku to inspect, so provide its parameter
names when declaring the command.

```csharp
static readonly QueryCommand Renumber = new(
    "dbo.RenumberTracks", ["albumId", "moved"]);

int affected = Renumber.Execute(cnn, new { albumId = 1, moved = 0 });
```

A connection can provide the list instead, reading the procedure declaration
from the database, including parameter names, types, sizes, and directions.

```csharp
static readonly QueryCommand Renumber = QueryCommand.FromProc("dbo.RenumberTracks", cnn);
```

The same explicit-name constructor also works for normal SQL. `CommandType.Text`
selects SQL instead of a stored procedure.

```csharp
static readonly QueryCommand UpdateTrack = new(
    "UPDATE tracks SET Name = @name WHERE TrackId = @id",
    ["name", "id"],
    CommandType.Text);
```

## A DbCommand you already have

The mapping side also runs on a command you built yourself.

```csharp
static readonly CachedTypeParser<Track> Tracks = new();

using var cmd = cnn.CreateCommand();
cmd.CommandText = "SELECT TrackId AS Id, Name, UnitPrice FROM tracks WHERE TrackId = @id";
cmd.Parameters.Add(new SqlParameter("@id", 10));

Track track = Tracks.Query(cmd);
```

See [any DbCommand](direct-dbcommand.md).

## One command that adapts to its input

### Conditional markers

An optional variable removes its footprint when its value is absent.

```csharp
static readonly QueryCommand Tracks = new(
    "SELECT TrackId, Name FROM tracks WHERE AlbumId = ?@albumId");

var albumTracks = Tracks.Query<List<Track>>(cnn, new { albumId = 1 });
// SELECT TrackId, Name FROM tracks WHERE AlbumId = @albumId

var allTracks = Tracks.Query<List<Track>>(cnn);
// SELECT TrackId, Name FROM tracks
```

Comment conditions can remove a join when its optional filter is absent.

```csharp
static readonly QueryCommand Invoices = new(
    "SELECT i.InvoiceId FROM invoices i /*@country*/INNER JOIN customers c ON i.CustomerId = c.CustomerId WHERE c.Country = ?@country");

var canadian = Invoices.Query<List<int>>(cnn, new { country = "CA" });
// SELECT i.InvoiceId FROM invoices i INNER JOIN customers c ON i.CustomerId = c.CustomerId WHERE c.Country = @country

var allInvoices = Invoices.Query<List<int>>(cnn);
// SELECT i.InvoiceId FROM invoices i
```

### Handlers

`_X` expands a collection into individual parameters.

```csharp
static readonly QueryCommand TracksByGenre = new(
    "SELECT TrackId AS Id, Name, UnitPrice FROM tracks WHERE GenreId IN (?@genreIds_X)");

var tracks = TracksByGenre.Query<List<Track>>(cnn, new { genreIds = new[] { 1, 2, 3 } });
// SELECT TrackId AS Id, Name, UnitPrice FROM tracks WHERE GenreId IN (@genreIds_1, @genreIds_2, @genreIds_3)
```

### Dynamic projection

`?SELECT` makes projected columns respond to keys.

```csharp
static readonly QueryCommand TrackColumns = new(
    "?SELECT TrackId AS Id!, Name, UnitPrice FROM tracks WHERE AlbumId = @albumId");

var names = TrackColumns.StartBuilder();
names.Use("@albumId", 1);
names.Use("Name");
var tracksByName = names.Query<List<Track>>(cnn);
// SELECT TrackId AS Id, Name FROM tracks WHERE AlbumId = @albumId

var prices = TrackColumns.StartBuilder();
prices.Use("@albumId", 1);
prices.Use("UnitPrice");
var tracksByPrice = prices.Query<List<Track>>(cnn);
// SELECT TrackId AS Id, UnitPrice FROM tracks WHERE AlbumId = @albumId
```

See [conditional SQL](../conditional-sql/index.md) for the full syntax and the other combinations.

## Cheatsheet

| Goal | Method | Sync return | Async return |
| --- | --- | --- | --- |
| Insert / Update / Delete | `Execute` | `int` | `Task<int>` |
| One scalar from a `SELECT` | `Query<T>` | `T` | `Task<T>` |
| One value returned by an execution | `ExecuteScalar<T>` | `T` | `Task<T>` |
| One row (throws if none) | `Query<T>` | `T` | `Task<T>` |
| One row or empty | `Query<Optional<T>>` | `Optional<T>` | `Task<Optional<T>>` |
| Exactly one row | `Query<Single<T>>` | `Single<T>` | `Task<Single<T>>` |
| Many (buffered) | `Query<List<T>>` | `List<T>` | `Task<List<T>>` |
| Many (streamed) | `Query<IEnumerable<T>>` | `IEnumerable<T>` | `Task<IEnumerable<T>>` |
| Many (async stream) | `StreamQueryAsync<T>` | n/a | `IAsyncEnumerable<T>` |
| A raw reader | `ExecuteReader` | `DbDataReader` | `Task<DbDataReader>` |
| Several result sets | `ExecuteMultiReader` | `MultiReader` | `Task<MultiReader>` |

The `T` in `Query<T>` is open, not a fixed menu. New [result shapes](result-shapes.md) plug in the same way the built-in ones do.
