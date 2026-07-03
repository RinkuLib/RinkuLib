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

Pass an object whose members match the parameter names, case-insensitive. Anonymous objects, records, and DTOs all work, and unmatched members are ignored.

```csharp
static readonly QueryCommand ByComposer = new(
    "SELECT TrackId AS Id, Name, UnitPrice FROM tracks WHERE Composer = @composer AND UnitPrice >= @minPrice");

var tracks = ByComposer.Query<List<Track>>(cnn, new { composer = "AC/DC", minPrice = 0.99m });
```

When C# logic should set the values instead, use a builder. Both roads are covered on [supplying values](parameters.md).

```csharp
var b = ByComposer.StartBuilder();
b.Use("@composer", "AC/DC");
b.Use("@minPrice", 0.99m);
var tracks = b.Query<List<Track>>(cnn);
```

## Writes and scalars

`Execute` returns the affected-row count, `ExecuteScalar<T>` a single value.

```csharp
static readonly QueryCommand UpdatePrice = new(
    "UPDATE tracks SET UnitPrice = @price WHERE TrackId = @id");
static readonly QueryCommand CountTracks = new("SELECT COUNT(*) FROM tracks");

int affected = UpdatePrice.Execute(cnn, new { id = 10, price = 1.29m });
int total    = CountTracks.ExecuteScalar<int>(cnn);
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

## Reuse one DbCommand across a batch

Bind a builder to a single `DbCommand` and a loop stops rebuilding it each pass.

```csharp
static readonly QueryCommand Insert = new("INSERT INTO playlists (Name) VALUES (@name)");

using var sqlCmd = new SqlCommand();
var batch = Insert.StartBuilder(sqlCmd);
foreach (var name in names) {
    batch.Use("@name", name);
    batch.Execute(cnn);
}
```

## Several result sets

One command, several selects, read in order.

```csharp
static readonly QueryCommand Dashboard = new(
    "SELECT * FROM artists WHERE ArtistId = @id; SELECT * FROM albums WHERE ArtistId = @id");

using var multi = Dashboard.ExecuteMultiReader(cnn, out DbCommand cmd, new { id = 1 });
using (cmd) {
    Artist artist      = multi.Query<Artist>();
    List<Album> albums = multi.Query<List<Album>>();
}
```

See [multiple result sets](multiple-results.md).

## A DbCommand you already have

The mapping side also runs on a command you built yourself.

```csharp
using var cmd = cnn.CreateCommand();
cmd.CommandText = "SELECT TrackId AS Id, Name, UnitPrice FROM tracks WHERE TrackId = @id";
cmd.Parameters.Add(new SqlParameter("@id", 10));

Track track = cmd.Query<Track>();
```

See [any DbCommand](direct-dbcommand.md).

## One command that adapts to its input

The template can mark parts optional, so the values you pass decide the SQL. `?@` marks a variable optional, `_X` spreads a collection.

```csharp
static readonly QueryCommand Search = new(
    "SELECT TrackId AS Id, Name, UnitPrice FROM tracks WHERE AlbumId = ?@albumId AND GenreId IN (?@genreIds_X)");

Search.Query<List<Track>>(cnn, new { albumId = 1 });
// SELECT TrackId AS Id, Name, UnitPrice FROM tracks WHERE AlbumId = @albumId

Search.Query<List<Track>>(cnn, new { genreIds = new[] { 1, 2, 3 } });
// SELECT TrackId AS Id, Name, UnitPrice FROM tracks WHERE GenreId IN (@genreIds_1, @genreIds_2, @genreIds_3)

static readonly QueryCommand UpdateTrack = new(
    "UPDATE tracks SET Name = ?@name, UnitPrice = ?@price WHERE TrackId = @trackId");

UpdateTrack.Execute(cnn, new { trackId = 10, name = "Remastered" });
// UPDATE tracks SET Name = @name WHERE TrackId = @trackId
```

Nothing about this is specific to filters. The engine treats every keyword section the same, so projected columns, joins, grouping, ordering, and `SET` lists toggle exactly like a `WHERE` clause. When several queries are really one query with parts switched on and off, one command replaces them all. The template syntax is its own section, [conditional SQL](../conditional-sql/index.md).

## The full method table

| Goal | Method | Sync return | Async return |
| --- | --- | --- | --- |
| Insert / Update / Delete | `Execute` | `int` | `Task<int>` |
| Single value | `ExecuteScalar<T>` | `T` | `Task<T>` |
| One row (throws if none) | `Query<T>` | `T` | `Task<T>` |
| One row or empty | `Query<Optional<T>>` | `Optional<T>` | `Task<Optional<T>>` |
| Exactly one row | `Query<Single<T>>` | `Single<T>` | `Task<Single<T>>` |
| Many (buffered) | `Query<List<T>>` | `List<T>` | `Task<List<T>>` |
| Many (streamed) | `Query<IEnumerable<T>>` | `IEnumerable<T>` | `Task<IEnumerable<T>>` |
| Many (async stream) | `StreamQueryAsync<T>` | n/a | `IAsyncEnumerable<T>` |
| A raw reader | `ExecuteReader` | `DbDataReader` | `Task<DbDataReader>` |
| Several result sets | `ExecuteMultiReader` | `MultiReader` | `Task<MultiReader>` |
