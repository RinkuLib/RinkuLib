# FAQ

## How is a QueryCommand accessed

```csharp
static readonly QueryCommand GetAlbum = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId");

Album album = GetAlbum.Query<Album>(cnn, new { albumId = 12 });
// The application holds this QueryCommand directly.
```

```csharp
Album album = cnn.Query<Album>("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
// The exact SQL string accesses the cached QueryCommand.
```

[SQL string cache](../running-queries/sql-string.md)

## Can one command be shared across threads

```csharp
static readonly QueryCommand GetAlbum = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId");

Album[] albums = await Task.WhenAll(
    GetAlbum.QueryAsync<Album>((DbConnection)firstConnection, new { albumId = 12 }),
    GetAlbum.QueryAsync<Album>((DbConnection)secondConnection, new { albumId = 46 }));
```

Per-call values stay outside the `QueryCommand`. Builder state is also held separately from the command.

[Builders](../running-queries/builders.md)

## Can one command run across different providers

A `QueryCommand` can retain mapping and parameter metadata learned from a provider. A command shared across different providers is unsupported.

```csharp
static readonly QueryCommand SqlServerAlbums = new("SELECT AlbumId AS Id, Title FROM albums");
static readonly QueryCommand PostgreSqlAlbums = new("SELECT AlbumId AS Id, Title FROM albums");
```

[Parameter metadata](../running-queries/parameter-metadata.md) · [Cache control](../customization/caches.md)

## Does Rinku rewrite named parameters for positional providers

```csharp
var command = new QueryCommand("SELECT UserId, Name FROM users WHERE UserId = ? AND Status = ?", ["userId", "status"], CommandType.Text);
```

The placeholders remain provider syntax. The declared variables give Rinku the parameter order.

[Positional parameters](../running-queries/values.md#positional-variables)

## Why did the provider report a missing parameter

A plain variable remains required while its SQL remains.

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId
```

A conditional variable removes its complete condition when its value is absent.

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = ?@albumId
```

Without `albumId` the SQL becomes.

```sql
SELECT AlbumId AS Id, Title FROM albums
```

[Conditional variables](../conditional-sql/variables.md)

## How can optional filters avoid WHERE 1=1

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@artistId AND ReleaseYear >= ?@minimumYear");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
// SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

[Conditional variables](../conditional-sql/variables.md)

## Why did Query<T> report no values

An unwrapped result requests the first complete mapped value.

```csharp
static readonly QueryCommand FindAlbum = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId");

Album album = FindAlbum.Query<Album>(cnn, new { albumId = 999 });
// No complete Album produces RINKU4001.
```

A wrapper can represent no returned value.

```csharp
Optional<Album> album = FindAlbum.Query<Optional<Album>>(cnn, new { albumId = 999 });
```

[Result shapes](../running-queries/result-shapes.md)

## Why did IN with an empty collection disappear

```csharp
static readonly QueryCommand FindAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (?@albumIds_X)");

List<Album> albums = FindAlbums.Query<List<Album>>(cnn, new { albumIds = Array.Empty<int>() });
// SELECT AlbumId AS Id, Title FROM albums
```

An empty collection is absent for the collection handler. A required `@albumIds_X` raises `RINKU2002` instead.

[Collection expansion](../conditional-sql/collections.md) · [RINKU2002](errors.md#rinku2002-required-handler-value)

## Why is a nested type unavailable

```csharp
public record Artist(int Id, string Name) : IDbReadable;
public record Album(int Id, string Title, Artist Artist);
```

`Album` is the explicitly requested root. `Artist` becomes available for nested mapping through `IDbReadable`.

The same registration can be made externally.

```csharp
public record Artist(int Id, string Name);

TypeParsingInfo.GetOrAdd<Artist>();
```

[Registration](../mapping/registration.md)

## Why did joined rows produce several parent objects

Rows that fold into one parent group must be consecutive.

```sql
SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumsId, al.Title AS AlbumsTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId
```

The grouping page shows inferred boundaries, explicit keys, and custom grouping rules.

[Grouping](../mapping/grouping.md)

## When are streamed output parameters available

Output values are available after the stream finishes or its enumerator is disposed.

```csharp
QueryCommand readAndCountAlbums = QueryCommand.FromProc("ReadAndCountAlbums", cnn);
IEnumerable<Album> albums = readAndCountAlbums.Query<IEnumerable<Album>>(cnn, out DbCommand command);

using (command)
{
    using (IEnumerator<Album> iterator = albums.GetEnumerator())
    {
        if (iterator.MoveNext())
            Console.WriteLine(iterator.Current.Title);
    }

    int moved = command.GetOutputValue<int>("@moved");
}
```

[Stored procedure output values](../running-queries/stored-procedures.md)

## Where is the Dapper comparison

[Coming from Dapper](dapper.md)

## Does Tracking save changes to the database

```csharp
Album original = new(12, "Blue", new Artist(7, "Miles"));
IRuntimeTrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(original);
edit.Set(nameof(Album.Title), "Kind of Blue");

foreach (TrackingChange change in edit.GetChanges())
    Console.WriteLine($"{change.Name} {change.OriginalValue} -> {change.Value}");
```

```csharp
static readonly QueryCommand UpdateAlbum = new("UPDATE albums SET Title = @Title WHERE AlbumId = @Id");

using DbTransaction transaction = cnn.BeginTransaction();

if (edit.HasChanges())
    UpdateAlbum.Execute(cnn, edit, transaction: transaction);

transaction.Commit();
edit.ConfirmEdit();
```

[Tracking persistence](../tracking/persistence.md)

## Which connection is used with a transaction

Pass the same open connection that created the transaction.

```csharp
using DbTransaction transaction = cnn.BeginTransaction();

UpdateAlbum.Execute(cnn, new { albumId = 12, title = "Updated" }, transaction: transaction);
```

Rinku passes the supplied connection and transaction to the provider.

[Execution context](../running-queries/execution-context.md)

## How are several result sets read

```csharp
static readonly QueryCommand GetDashboard = new("SELECT ArtistId AS Id, Name FROM artists WHERE ArtistId = @artistId; SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

using MultiReader results = GetDashboard.ExecuteMultiReader(cnn, new { artistId = 7 });

Artist artist = results.Query<Artist>();
List<Album> albums = results.Query<List<Album>>();
```

[Multiple result sets](../running-queries/multiple-results.md)

## Which databases can CodeGen inspect

```text
SQL Server    SQL queries, SQL files, stored procedures
PostgreSQL    SQL queries, SQL files, stored procedures
SQLite        SQL queries, SQL files
```

[CodeGen configuration](../codegen/configure.md) · [Query sources](../codegen/queries.md)

## Do generated commands require a Rinku result parser

```csharp
using DbCommand command = cnn.GetAlbumsByArtist(artistId: 7);
using DbDataReader reader = command.ExecuteReader();
// Generated methods return `DbCommand` instances.
```

```csharp
static readonly CachedTypeParser<List<GetAlbumsByArtistResult>> AlbumsParser = new();

List<GetAlbumsByArtistResult> albums = AlbumsParser.Query(cnn.GetAlbumsByArtist(artistId: 7));
```

[Generated commands](../codegen/generated-code.md)

## Why does generated CodeGen output contain an error directive

```csharp
#error Query generation failed for method 'GetBrokenAlbums'
```

The query named by the directive failed during discovery or generation. Other valid query entries can still be generated.

[Refresh generated code](../codegen/refresh.md)
