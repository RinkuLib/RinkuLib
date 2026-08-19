# Rinku overview

This page is the full tour of Rinku. The [documentation index](index.md) is the shorter starting point when you only need to find the right module.

Get Rinku from [NuGet](https://www.nuget.org/packages/Rinku/) or browse the [source on GitHub](https://github.com/RinkuLib/RinkuLib). Add it to a .NET 8 or .NET 10 project with the following command.

```bash
dotnet add package Rinku
```

## Query rows into a type

Declare a command for SQL that will be used more than once.

```csharp
using Rinku;

public record Album(int Id, string Title) : IDbReadable;

static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums");

List<Album> albums = GetAlbums.Query<List<Album>>(cnn);
```

The same query can be written inline.

```csharp
List<Album> albums = cnn.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums");
```

[See how SQL-string shortcuts work](running-queries/sql-string.md).

## Pass values

Pass parameters to the SQL by matching object names.

```csharp
public record Album(int Id, string Title) : IDbReadable;

static readonly QueryCommand GetAlbum = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId");

Album album = GetAlbum.Query<Album>(cnn, new { albumId = 7 });
```

[Read about supplying values](running-queries/values.md).

## Choose how results are read

```csharp
public record Album(int Id, string Title) : IDbReadable;

static readonly QueryCommand GetAlbum = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId");
static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums");

Album first = GetAlbum.Query<Album>(cnn, new { albumId = 1 });
Optional<Album> maybe = GetAlbum.Query<Optional<Album>>(cnn, new { albumId = 1 });
Album exactlyOne = GetAlbum.Query<Single<Album>>(cnn, new { albumId = 1 });
List<Album> albums = GetAlbums.Query<List<Album>>(cnn);
IEnumerable<Album> stream = GetAlbums.Query<IEnumerable<Album>>(cnn);
```

`Album` requires a row. `Optional<Album>` allows none, and `Single<Album>` also rejects a second row.

See [result shapes](running-queries/result-shapes.md) and [streaming](running-queries/streaming.md).

## Build from application logic

```csharp
public record NewAlbum(string Title, int ArtistId);

static readonly QueryCommand AddAlbum = new("INSERT INTO albums (Title, ArtistId, Status, CreatedBy) VALUES (@Title, @ArtistId, @Status, @CreatedBy)");

var values = AddAlbum.StartBuilder();
values.UseWith(new NewAlbum("Blue", 7));
values.Use("@Status", publishNow ? "published" : "draft");
values.Use('@', "CreatedBy", currentUser.Id);
int affected = values.Execute(cnn);
```

[Read about builders](running-queries/builders.md).

## Map nested objects

The `ArtistId` and `ArtistName` columns fill the `Artist` member.

```csharp
public record Artist(int Id, string Name) : IDbReadable;
public record AlbumWithArtist(int Id, string Title, Artist Artist);

static readonly QueryCommand GetAlbumsWithArtists = new("SELECT al.AlbumId AS Id, al.Title, ar.ArtistId AS ArtistId, ar.Name AS ArtistName FROM albums al JOIN artists ar ON ar.ArtistId = al.ArtistId");

List<AlbumWithArtist> albums = GetAlbumsWithArtists.Query<List<AlbumWithArtist>>(cnn);
```

See [object mapping](mapping/objects.md) and [nested objects](mapping/nesting.md).

## Read several result sets

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record class ArtistWithAlbums(int Id, string Name) {
    public List<Album> Albums { get; set; } = [];
}

static readonly QueryCommand GetArtistWithAlbums = new("SELECT ArtistId AS Id, Name FROM artists WHERE ArtistId = @artistId; SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

using MultiReader results = GetArtistWithAlbums.ExecuteMultiReader(cnn, new { artistId = 7 });

ArtistWithAlbums artist = results.Query<ArtistWithAlbums>();
artist.Albums = results.Query<List<Album>>();
```

[Read about multiple result sets](running-queries/multiple-results.md).

## Fill collections from joined rows

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record ArtistWithAlbums(int Id, string Name, List<Album> Albums);

static readonly QueryCommand GetArtists = new("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumsId, al.Title AS AlbumsTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId");

List<ArtistWithAlbums> artists = GetArtists.Query<List<ArtistWithAlbums>>(cnn);
```

```text
Id  Name   AlbumsId  AlbumsTitle
1   Queen  10        Jazz
1   Queen  11        The Game

artists[0].Albums.Count == 2
```

See [collections from joins](mapping/collections.md) and [grouping](mapping/grouping.md).

## Read columns in order with a tuple

### Scalar values

```csharp
static readonly QueryCommand GetAlbumSummary = new("SELECT AlbumId, Title FROM albums WHERE AlbumId = @albumId");

(int id, string title) = GetAlbumSummary.Query<(int, string)>(cnn, new { albumId = 1 });
```

### An object with its parent id

```csharp
public record Album(int Id, string Title) : IDbReadable;

static readonly QueryCommand GetAlbumWithArtistId = new("SELECT ArtistId, AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId");

(int artistId, Album album) = GetAlbumWithArtistId.Query<(int, Album)>(cnn, new { albumId = 1 });
```

### An employee and their manager

```csharp
public record Employee(int Id, string Name) : IDbReadable;

static readonly QueryCommand GetEmployeeAndManager = new("SELECT e.EmployeeId AS Id, e.Name, m.EmployeeId AS Id, m.Name FROM employees e JOIN employees m ON m.EmployeeId = e.ManagerId WHERE e.EmployeeId = @employeeId");

(Employee employee, Employee manager) = GetEmployeeAndManager.Query<(Employee, Employee)>(cnn, new { employeeId = 1 });
```

[Read about tuples](mapping/tuples.md).

## Read a row without declaring a type

### Whole row

```csharp
static readonly QueryCommand GetAlbumRow = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId");

DynaObject row = GetAlbumRow.Query<DynaObject>(cnn, new { albumId = 1 });

int id = row.Get<int>("Id");
string title = row.Get<string>("Title");
```

### Typed value followed by dynamic columns

```csharp
static readonly QueryCommand GetAlbumRemainder = new("SELECT AlbumId, Title, ReleaseYear FROM albums WHERE AlbumId = @albumId");

(int id, DynaObject remaining) = GetAlbumRemainder.Query<(int, DynaObject)>(cnn, new { albumId = 1 });

string title = remaining.Get<string>("Title");
int releaseYear = remaining.Get<int>("ReleaseYear");
```

[Read about dynamic rows](mapping/dynamic-rows.md).

## Read database NULL

### Nullable value

```csharp
static readonly QueryCommand GetAlbumPrice = new("SELECT Price FROM albums WHERE AlbumId = @albumId");

decimal? price = GetAlbumPrice.Query<decimal?>(cnn, new { albumId = 1 });
// Database NULL becomes null. No row still raises RINKU4001.
```

### Missing nested object

```csharp
public record LatestAlbum([AbortOnNull] int Id, string Title) : IDbReadable;
public record ArtistWithLatest(int Id, string Name, LatestAlbum? LatestAlbum);

static readonly QueryCommand GetArtist = new("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS LatestAlbumId, al.Title AS LatestAlbumTitle FROM artists ar LEFT JOIN albums al ON al.AlbumId = ar.LatestAlbumId WHERE ar.ArtistId = @artistId");

ArtistWithLatest artist = GetArtist.Query<ArtistWithLatest>(cnn, new { artistId = 1 });
// NULL LatestAlbumId makes artist.LatestAlbum null.
```

[Read about database NULL](mapping/nulls.md).

## Select a constructor or factory from the columns

### Constructor

```csharp
public sealed class AlbumLabel {
    public AlbumLabel(int id) => Id = id;
    public AlbumLabel(int id, string title) => (Id, Title) = (id, title);

    public int Id { get; }
    public string? Title { get; }
}

static readonly QueryCommand GetAlbumLabel = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId");

AlbumLabel album = GetAlbumLabel.Query<AlbumLabel>(cnn, new { albumId = 1 });
// Id and Title select AlbumLabel(int, string).
```

### Static factory

```csharp
public interface IShape {
    public static IShape FromCircle(double radius) => new Circle(radius);
}

public record Circle(double Radius) : IShape;

static readonly QueryCommand GetShape = new("SELECT Radius FROM shapes WHERE ShapeId = @shapeId");

IShape shape = GetShape.Query<IShape>(cnn, new { shapeId = 1 });
// The Radius column selects FromCircle(double).
```

[Read about construction paths](mapping/construction-paths.md).

## Remove SQL when a value is missing

Put `?` before a value when its condition should exist only when that value is supplied.

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@artistId AND Title LIKE ?@title");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

Supplying both values keeps both conditions.

```csharp
List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new { artistId = 7, title = "%blue%" });
```

[Read about conditional variables](conditional-sql/variables.md).

## Expand a collection

`_X` creates one database parameter for each item.

```csharp
List<Album> albums = cnn.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (?@albumIds_X)", new { albumIds = new[] { 1, 4, 9 } });
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (@albumIds_1, @albumIds_2, @albumIds_3)
```

[Read about collection expansion](conditional-sql/collections.md).

## Pick returned columns

`?SELECT` lets the supplied keys choose the projection.

```csharp
static readonly QueryCommand AlbumProjection = new("?SELECT AlbumId AS Id, Title, ReleaseYear FROM albums");

var projection = AlbumProjection.StartBuilder();
projection.Use("Title");
List<DynaObject> albums = projection.Query<List<DynaObject>>(cnn);
```

```sql
SELECT Title FROM albums
```

[Read about dynamic projection](conditional-sql/dynamic-projection.md).

## Execute SQL

```csharp
int affected = cnn.Execute("UPDATE albums SET Title = @title WHERE AlbumId = @albumId", new { albumId = 1, title = "Blue" });
```

Use `ExecuteScalar<T>` when executing the SQL also returns one value.

```csharp
int albumId = cnn.ExecuteScalar<int>("INSERT INTO albums (Title, ArtistId) VALUES (@title, @artistId) RETURNING AlbumId", new { title = "Blue", artistId = 7 });
```

[Read about executing SQL](running-queries/execution.md).

## Await a query

```csharp
List<Album> albums = await cnn.QueryAsync<List<Album>>("SELECT AlbumId AS Id, Title FROM albums", ct: cancellationToken);
```

[Read about async calls](running-queries/async.md).

## Stream rows asynchronously

```csharp
await foreach (Album album in cnn.StreamQueryAsync<Album>("SELECT AlbumId AS Id, Title FROM albums", ct: cancellationToken))
    Show(album);
```

[Read about streaming](running-queries/streaming.md).

## Use a transaction and timeout

```csharp
using IDbTransaction transaction = cnn.BeginTransaction();
int affected = cnn.Execute("UPDATE albums SET Title = @title WHERE AlbumId = @albumId", new { albumId = 1, title = "Blue" }, transaction: transaction, timeout: 30);
```

[Read about transactions, timeouts, and cancellation](running-queries/execution-context.md).

## Call a stored procedure

```csharp
static readonly QueryCommand GetAlbumsForArtist = new("GetAlbumsForArtist", ["artistId"]);

List<Album> albums = GetAlbumsForArtist.Query<List<Album>>(cnn, new { artistId = 7 });
```

[Read about stored procedures and output values](running-queries/stored-procedures.md).

## Read a stored procedure output value

```csharp
static readonly QueryCommand RenumberAlbums = CreateRenumberAlbums();

static QueryCommand CreateRenumberAlbums() {
    using DbConnection setupConnection = GetConnection();
    return QueryCommand.FromProc("RenumberAlbums", setupConnection);
}

RenumberAlbums.Execute(cnn, out DbCommand command, new { albumId = 12 });

using (command) {
    int moved = command.GetOutputValue<int>("@moved");
}
```

[Read about output and return values](running-queries/stored-procedures.md#read-an-output-parameter).

## Map an existing DbCommand

```csharp
static readonly CachedTypeParser<Album> AlbumParser = new();

using DbCommand command = cnn.CreateCommand();
command.CommandText = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = 1";

Album album = AlbumParser.Query(command);
```

[Read about existing DbCommand instances](running-queries/dbcommand.md).

## Read rows yourself

```csharp
static readonly QueryCommand GetAlbumRows = new("SELECT AlbumId, Title FROM albums");

DbDataReader reader = GetAlbumRows.ExecuteReader(cnn, out DbCommand command);
using (command)
using (reader) {
    while (reader.Read())
        Show(reader.GetInt32(0), reader.GetString(1));
}
```

[Read about raw readers](running-queries/readers.md).

## Use IDbConnection

```csharp
IDbConnection cnn = GetLegacyConnection();
List<Album> albums = cnn.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums");
```

[Read about IDbConnection support](running-queries/idbconnection.md).

## Change a mapping rule

```csharp
public readonly record struct PositionalValue<T>(T Value);

TypeParsingInfo.AddOrSet(typeof(PositionalValue<>), CtorTypeInfo.Instance);

static readonly QueryCommand CountAlbums = new("SELECT COUNT(*) FROM albums");
PositionalValue<int> count = CountAlbums.Query<PositionalValue<int>>(cnn);
```

[See more mapping changes](customization/type-registration.md) or [write a result parser](customization/result-parsers.md).

## Generate typed commands

```csharp
static readonly CachedTypeParser<List<GetAlbumsByArtistResult>> AlbumParser = new();

using SqlConnection sqlConnection = new(connectionString);

List<GetAlbumsByArtistResult> albums = AlbumParser.Query(sqlConnection.GetAlbumsByArtist(artistId: 7));
```

[Read about RinkuPowerTools](codegen/index.md).

## Track edits

Runtime tracking can generate an editable wrapper directly from an ordinary object.

```csharp
using Rinku.Tracking;
using Rinku.Tracking.Runtime;

public record Album(int Id, string Title);

Album original = new(12, "Blue");
IRuntimeDynamicTrackingItem<Album> album = original.ToTrackingItem();

album.Set(nameof(Album.Title), "Kind of Blue");
bool changed = album.IsEditing;
```

Collections can track item edits and structural changes together.

```csharp
using Rinku.Tracking;
using Rinku.Tracking.Runtime;

Album[] originals = [new(1, "Blue"), new(2, "Green")];
TrackingList<IRuntimeDynamicTrackingItem<Album>> albums = originals.ToTrackingList();

albums.RemoveAt(0);
bool changed = albums.HasChanges();
```

Tracking does not persist anything by itself. Accept the local state only after the application has saved it successfully.

[Read about tracking](tracking/index.md), [tracking items](tracking/items.md), [tracking lists](tracking/lists.md) and [runtime tracking](tracking/runtime.md).

## Find details

The [Dapper guide](reference/dapper.md) maps familiar operations to Rinku. The [performance notes](reference/performance.md), [error reference](reference/errors.md), and [FAQ](reference/faq.md) cover evaluation and troubleshooting. The generated API pages list public types and members.
