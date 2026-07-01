# The workflow, by example

The [quick start](../getting-started/quick-start.md) ran one query. This page is the rest of the workflow. Define a `QueryCommand` once, then run it in whatever shape you need. Each block is a complete example. The deeper pages explain the parts. What `T` can be is covered in [mapping](../mapping/index.md), and making the SQL itself adapt is [conditional SQL](../conditional-sql/overview.md).

Everything here shares one setup. `Track` is one of your own types. The examples would read the same for any type.

```csharp
using RinkuLib.Queries;
using RinkuLib.Commands;

static readonly QueryCommand GetTracks    = new("SELECT TrackId AS Id, Name, UnitPrice FROM tracks WHERE AlbumId = @albumId");
static readonly QueryCommand GetTrackById = new("SELECT TrackId AS Id, Name, UnitPrice FROM tracks WHERE TrackId = @id");

using DbConnection cnn = GetConnection();
```

## Read rows into objects

The type argument picks the shape. `Query<T>` is the one entry point, and `T` decides the rest (see [choosing the result type](../mapping/simple-results.md)).

```csharp
List<Track> all       = GetTracks.Query<List<Track>>(cnn, new { albumId = 1 });       // buffered
IEnumerable<Track> it = GetTracks.Query<IEnumerable<Track>>(cnn, new { albumId = 1 }); // streamed
Track track           = GetTrackById.Query<Track>(cnn, new { id = 10 });              // one (throws if absent)
Optional<Track> maybe = GetTrackById.Query<Optional<Track>>(cnn, new { id = 99 });    // one or empty
```

## Pass parameters

Hand the command an object whose members match the variable names. Anonymous objects, records, and DTOs all work.

```csharp
var a = GetTracks.Query<List<Track>>(cnn, new { albumId = 1 });
var b = GetTracks.Query<List<Track>>(cnn, new TrackQuery { albumId = 1 });
```

## Scalars and non-queries

```csharp
static readonly QueryCommand CountTracks = new("SELECT COUNT(*) FROM tracks");
static readonly QueryCommand Reprice     = new("UPDATE tracks SET UnitPrice = @price WHERE TrackId = @id");

int total    = CountTracks.ExecuteScalar<int>(cnn);
int affected = Reprice.Execute(cnn, new { id = 10, price = 1.29m });
```

## Async

Every read and execute has an async version. `StreamQueryAsync` gives you an `IAsyncEnumerable<T>`.

```csharp
List<Track> tracks = await GetTracks.QueryAsync<List<Track>>(cnn, new { albumId = 1 }, ct: token);

await foreach (Track t in GetTracks.StreamQueryAsync<Track>(cnn, new { albumId = 1 }, ct: token))
    Process(t);
```

## Make the query adapt

Mark optional parts of the template, and the values you supply decide what stays (full details in [conditional SQL](../conditional-sql/overview.md)).

```csharp
static readonly QueryCommand Search =
    new("SELECT TrackId AS Id, Name FROM tracks WHERE AlbumId = @albumId AND UnitPrice >= ?@minPrice AND Name LIKE ?@name");

// @name omitted, so its clause is pruned.
var results = Search.Query<List<Track>>(cnn, new { albumId = 1, minPrice = 0.99m });
// SELECT TrackId AS Id, Name FROM tracks WHERE AlbumId = @albumId AND UnitPrice >= @minPrice
```

## Set the values yourself with a builder

Passing a parameter object is one way to supply the values. The other is a [builder](builders.md), which you set in C#, handy when logic decides which optional pieces are active. Both build and run the same command.

```csharp
var b = Search.StartBuilder();
b.Use("@albumId", 1);
if (filterByPrice) b.Use("@minPrice", 0.99m);
var results = b.Query<List<Track>>(cnn);
```

## Reuse one command across a batch

Bind a builder to a single `DbCommand` and a loop won't rebuild it each time.

```csharp
static readonly QueryCommand Insert = new("INSERT INTO playlists(Name) VALUES(@name)");

using var sqlCmd = new SqlCommand();
var batch = Insert.StartBuilder(sqlCmd);
foreach (var name in names) {
    batch.Use("@name", name);
    batch.Execute(cnn);
}
```

## Several result sets at once

One command, many selects, mapped in order with a [`MultiReader`](multi-result.md).

```csharp
static readonly QueryCommand Dashboard =
    new("SELECT * FROM artists WHERE ArtistId = @id; SELECT * FROM albums WHERE ArtistId = @id");

using var multi = Dashboard.ExecuteMultiReader(cnn, out DbCommand cmd, new { id = 1 });
using (cmd) {
    Artist artist      = multi.Query<Artist>();
    List<Album> albums = multi.Query<List<Album>>();
}
```

## Transactions, timeouts, cancellation

The same calls take a transaction, a timeout, and (async) a `CancellationToken` after the connection. On the command form they follow the optional parameter object, so name them (see [transactions](transactions.md)).

```csharp
using var trans = cnn.BeginTransaction();
Reprice.Execute(cnn, new { id = 10, price = 1.29m }, transaction: trans);
trans.Commit();

var slow = GetTracks.Query<List<Track>>(cnn, new { albumId = 1 }, timeout: 60);
```

## Map a `DbCommand` you already have

The mapping engine isn't tied to `QueryCommand`. Hand any `DbCommand` to the same extensions (see [direct DbCommand](direct-dbcommand.md)).

```csharp
using var cmd = cnn.CreateCommand();
cmd.CommandText = "SELECT TrackId AS Id, Name, UnitPrice FROM tracks WHERE TrackId = @id";
cmd.Parameters.Add(new SqlParameter("@id", 10));

Track track = cmd.Query<Track>();
```

## The full method table

| Goal | Method | Sync return | Async return |
| --- | --- | --- | --- |
| Update / Delete / Insert | `Execute` | `int` | `Task<int>` |
| Scalar | `ExecuteScalar<T>` | `T` | `Task<T>` |
| One row (throws if none) | `Query<T>` | `T` | `Task<T>` |
| One row or empty | `Query<Optional<T>>` | `Optional<T>` | `Task<Optional<T>>` |
| Assert a single row | `Query<Single<T>>` | `Single<T>` | `Task<Single<T>>` |
| One, may be null | `Query<MaybeNull<T>>` | `MaybeNull<T>` | `Task<MaybeNull<T>>` |
| Many (buffered) | `Query<List<T>>` | `List<T>` | `Task<List<T>>` |
| Many (streamed) | `Query<IEnumerable<T>>` | `IEnumerable<T>` | `Task<IEnumerable<T>>` |
| Many (async stream) | `StreamQueryAsync<T>` | n/a | `IAsyncEnumerable<T>` |
| A reader | `ExecuteReader` | `DbDataReader` | `Task<DbDataReader>` |
| Several result sets | `ExecuteMultiReader` | `MultiReader` | `Task<MultiReader>` |

## Where to go deeper

- The `T` in `Query<T>` is all of [mapping](../mapping/index.md): one object, lists, tuples, `DynaObject`, nested graphs, null handling.
- Marking the template so it changes with the input is [conditional SQL](../conditional-sql/overview.md).
- The pieces behind these calls: the [QueryCommand](query-command.md) blueprint, the [builders](builders.md), [direct DbCommand](direct-dbcommand.md), [multiple result sets](multi-result.md), and [parameter specialization](parameter-specialization.md).
