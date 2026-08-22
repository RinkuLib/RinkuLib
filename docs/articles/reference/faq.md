# FAQ

## Should a QueryCommand be created for every call?

Keep reusable commands in `static readonly` fields.

```csharp
static readonly QueryCommand GetAlbum = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId");

Album album = GetAlbum.Query<Album>(cnn, new { albumId = 12 });
```

The template is parsed once. Per call values remain outside the command.

## Can one command be shared across threads?

Yes. Sharing is the intended use. Per call execution state comes from the parameter object or builder operation, while the command's reusable caches are guarded.

```csharp
using DbConnection firstConnection = new SqlConnection(connectionString);
using DbConnection secondConnection = new SqlConnection(connectionString);

Album[] albums = await Task.WhenAll(GetAlbum.QueryAsync<Album>(firstConnection, new { albumId = 12 }), GetAlbum.QueryAsync<Album>(secondConnection, new { albumId = 46 }));
```

Each execution uses its own connection and values while sharing `GetAlbum`.

## Can one command run across different providers?

That usage is unsupported. A command may retain mapping and parameter metadata learned from earlier executions. Declare a separate command for each provider.

```csharp
static readonly QueryCommand SqlServerAlbums = new("SELECT AlbumId AS Id, Title FROM albums");
static readonly QueryCommand PostgreSqlAlbums = new("SELECT AlbumId AS Id, Title FROM albums");
```

## Does Rinku rewrite named parameters for positional providers?

No. Keep the provider's positional placeholders and declare the variables in provider order.

```csharp
var command = new QueryCommand("SELECT UserId, Name FROM users WHERE UserId = ? AND Status = ?", ["userId", "status"], CommandType.Text);
```

See [positional parameters](../running-queries/values.md#positional-parameters) for the supported positional value forms.

## Why did the provider report a missing parameter?

A plain parameter is required whenever its SQL remains.

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId
```

Use a conditional variable when the condition should disappear with an absent value.

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = ?@albumId
```

Without `albumId`, the complete optional condition disappears.

```sql
SELECT AlbumId AS Id, Title FROM albums
```

## How can optional filters avoid WHERE 1=1?

Mark each optional value where its condition appears.

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@artistId AND ReleaseYear >= ?@minimumYear
```

Supplying only `artistId` keeps only its matching condition.

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

## Why did Query<T> report no values?

With Rinku's default parser, an unwrapped `T` requires a first complete result.

```csharp
Optional<Album> album = FindAlbum.Query<Optional<Album>>(cnn, new { albumId = 999 });
```

Use one of the included wrappers or add another [custom result parser](../customization/result-parsers.md) when no result is valid.

## Why did IN (?@ids_X) disappear?

An empty collection counts as absent. The optional condition is removed instead of generating `IN ()`.

```csharp
List<Album> albums = FindAlbums.Query<List<Album>>(cnn, new { albumIds = Array.Empty<int>() });
```

```sql
SELECT AlbumId AS Id, Title FROM albums
```

Without `?`, the same empty required handler raises `RINKU2002`.

## Why is a nested type unavailable?

Under the default mapping system, a root result is an explicit request. A type reached only through another mapped value needs a registration before its construction paths can participate.

```csharp
public record Artist(int Id, string Name) : IDbReadable;
public record Album(int Id, string Title, Artist Artist);
```

The [registration guide](../mapping/registration.md) shows the available ways to make that nested type readable.

## Why did joined rows produce several parent objects?

Rows for one grouped result must be consecutive.

```sql
SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumsId, al.Title AS AlbumsTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId
```

See [grouping](../mapping/grouping.md) when the inferred boundary is not the intended one.

## When are streamed output parameters available?

After the stream's enumerator is disposed, including when enumeration stops early.

```csharp
static readonly QueryCommand ReadAndCountAlbums = QueryCommand.FromProc("ReadAndCountAlbums", setupConnection);

IEnumerable<Album> albums = ReadAndCountAlbums.Query<IEnumerable<Album>>(cnn, out DbCommand command);

using (command) {
    using (IEnumerator<Album> iterator = albums.GetEnumerator()) {
        if (iterator.MoveNext())
            Console.WriteLine(iterator.Current.Title);
    }

    int moved = command.GetOutputValue<int>("@moved");
}
```

## Where should a Dapper user start?

[Coming from Dapper](dapper.md)

## Does Tracking save changes to the database?

No. Tracking keeps local original, edit, collection, validation, and metadata state. Persistence remains application code. See the [Tracking overview](../tracking/index.md).

## Which connection should be used with a transaction?

Pass the same open connection that created it.

```csharp
using DbConnection cnn = db.Open();
using DbTransaction transaction = cnn.BeginTransaction();

UpdateAlbum.Execute(cnn, new { albumId = 12, title = "Updated" }, transaction: transaction);
```

Rinku does not switch to `transaction.Connection` or validate that relationship. Provider errors report mismatched or completed transactions.

## How are several result sets read?

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record class ArtistWithAlbums(int Id, string Name) {
    public List<Album> Albums { get; set; } = [];
}

using MultiReader results = GetDashboard.ExecuteMultiReader(cnn, new { artistId = 7 });

ArtistWithAlbums artist = results.Query<ArtistWithAlbums>();
artist.Albums = results.Query<List<Album>>();
```

See [multiple result sets](../running-queries/multiple-results.md) for ordered reads from one command.

## Which database can CodeGen inspect?

Rinku Power Tools supports SQL Server, PostgreSQL, and SQLite.

```text
SQL Server    SQL queries, SQL files, stored procedures
PostgreSQL    SQL queries, SQL files, stored procedures
SQLite        SQL queries, SQL files
```

Generated methods still return provider-neutral `DbCommand` values. See [code generation](../codegen/index.md) and [query sources](../codegen/queries.md).

## Do generated commands require the Rinku result parser?

No. The generated method returns a normal `DbCommand`.

```csharp
using DbCommand command = cnn.GetAlbumsByArtist(artistId: 7);
using DbDataReader reader = command.ExecuteReader();
```

Rinku can also read the same command through a cached parser.

```csharp
static readonly CachedTypeParser<List<GetAlbumsByArtistResult>> Parser = new();

List<GetAlbumsByArtistResult> albums = Parser.Query(cnn.GetAlbumsByArtist(artistId: 7));
```

See [generated commands](../codegen/generated-code.md).

## Why does a generated CodeGen file contain an error directive?

A query failed while CodeGen was discovering or generating its command.

```csharp
#error Query generation failed for method 'GetBrokenAlbums'
```

Other valid query entries are still generated. Fix the failed query or its metadata and refresh the configuration again. See [refresh generated code](../codegen/refresh.md).
