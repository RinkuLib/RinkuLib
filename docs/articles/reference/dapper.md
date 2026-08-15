# Coming from Dapper

The SQL and parameter objects remain familiar. Rinku uses the full `T` passed to `Query<T>` to choose a result parser. This also works with parsers added by the application.

## Run the first query

The same query in Dapper uses the element type as its generic argument.

```csharp
IEnumerable<Album> albums = Dapper.SqlMapper.Query<Album>(cnn, "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId", new { artistId = 7 });
```

The SQL-string shortcut puts the full requested result type in that position.

```csharp
List<Album> albums = cnn.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId", new { artistId = 7 });
```

A reusable command keeps the SQL outside the execution call.

```csharp
static readonly QueryCommand GetAlbumsByArtist = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

List<Album> albums = GetAlbumsByArtist.Query<List<Album>>(cnn, new { artistId = 7 });
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

The SQL-string call retrieves a globally cached `QueryCommand`. A declared command has an application-controlled identity and lifetime.

## Choose a result shape

This table shows common equivalents using the parsers included with Rinku.

```csharp
Album first = GetAlbum.Query<Album>(cnn, new { albumId = 12 });
Optional<Album> maybe = GetAlbum.Query<Optional<Album>>(cnn, new { albumId = 12 });
Single<Album> single = GetAlbum.Query<Single<Album>>(cnn, new { albumId = 12 });
List<Album> buffered = GetAlbums.Query<List<Album>>(cnn);
IEnumerable<Album> streamed = GetAlbums.Query<IEnumerable<Album>>(cnn);
```

| Dapper | Rinku |
| --- | --- |
| `QueryFirst<T>` | `Query<T>` |
| `QueryFirst<T?>` for a value type | `Query<T?>` |
| `QueryFirst<T?>` for a reference type | `Query<MaybeNull<T>>` |
| `QueryFirstOrDefault<T>` for a reference type | `Query<Optional<T>>` |
| `QueryFirstOrDefault<T>` for a value type | `Query<OptionalStruct<T>>` |
| `QueryFirstOrDefault<T?>` for a reference type | `Query<OptionalNullable<T>>` |
| `QueryFirstOrDefault<T?>` for a value type | `Query<OptionalNullableStruct<T>>` |
| `QuerySingle<T>` | `Query<Single<T>>` |
| `QuerySingleOrDefault<T>` for a reference type | `Query<SingleOrDefault<T>>` |
| `QuerySingleOrDefault<T>` for a value type | `Query<SingleOrDefaultStruct<T>>` |
| buffered `Query<T>` | `Query<List<T>>` |
| `Query<T>(buffered: false)` | `Query<IEnumerable<T>>` |
| async row enumeration | `StreamQueryAsync<T>` |

Database `NULL` and no result are separate choices. The [result-shape guide](../running-queries/result-shapes.md) covers the included wrappers and shows where to add custom parsers.

## Supply parameters

The same anonymous parameter object works.

```csharp
List<Album> albums = GetAlbumsByArtist.Query<List<Album>>(cnn, new { ArtistID = 7 });
```

Build the values incrementally when program logic decides which ones are present.

```csharp
static readonly QueryCommand ConditionalAlbumSearch = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@artistId AND Title LIKE ?@title AND /*CurrentOnly*/IsArchived = 0");

var values = ConditionalAlbumSearch.StartBuilder();
if (artistId is int id)
    values.Use("@artistId", id);
if (!string.IsNullOrWhiteSpace(title))
    values.Use("@title", title);
if (!canSeeArchived)
    values.Use("CurrentOnly");
List<Album> albums = values.Query<List<Album>>(cnn);
```

The [supplying-values guide](../running-queries/values.md) covers parameter objects, builders, included value attributes, positional parameters, and custom member rules.

## Execute a batch

Dapper can execute once for every item in a sequence.

```csharp
cnn.Execute("UPDATE albums SET Title = @title WHERE AlbumId = @albumId", albums);
```

Rinku binds one caller-owned command and replaces its values for each execution.

```csharp
static readonly QueryCommand UpdateAlbum = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId");

using DbCommand command = cnn.CreateCommand();
var batch = UpdateAlbum.StartBuilder(command);

foreach (Album album in albums) {
    batch.UseWith(album);
    batch.Execute();
}
```

```sql
UPDATE albums SET Title = @title WHERE AlbumId = @albumId
```

## Read several result sets

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record class ArtistWithAlbums(int Id, string Name) {
    public List<Album> Albums { get; set; } = [];
}

using MultiReader results = GetDashboard.ExecuteMultiReader(cnn, new { artistId = 7 });

ArtistWithAlbums artist = results.Query<ArtistWithAlbums>();
artist.Albums = results.Query<List<Album>>();
```

`ExecuteMultiReader` corresponds to Dapper's `QueryMultiple`. Each read selects a Rinku result parser in the same way as `Query<T>`.

## Expand an IN value

In Dapper, a sequence expands directly inside the parameter placeholder.

```csharp
IEnumerable<Album> albums = Dapper.SqlMapper.Query<Album>(cnn, "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN @albumIds", new { albumIds = new[] { 2, 5 } });
```

In Rinku, the `_X` handler expands the sequence into numbered parameters.

```csharp
List<Album> albums = cnn.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (@albumIds_X)", new { albumIds = new[] { 2, 5 } });
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (@albumIds_1, @albumIds_2)
```

## Build conditional SQL

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@artistId AND AlbumId IN (?@albumIds_X)");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

Conditional markers replace the common `SqlBuilder` role. The included value handlers add collection spreading, numeric text, quoted text, and trusted raw fragments. You can add other suffixes.

## Map nested values

Dapper commonly uses multi-mapping with `splitOn`. Rinku maps nested objects from registrations, constructors, factories, naming rules, and the returned columns.

```csharp
public record Artist(int Id, string Name) : IDbReadable;
public record Album(int Id, string Title, Artist Artist);

static readonly QueryCommand GetAlbum = new(
    "SELECT al.AlbumId AS Id, al.Title, ar.ArtistId AS ArtistId, ar.Name AS ArtistName FROM albums al JOIN artists ar ON ar.ArtistId = al.ArtistId WHERE al.AlbumId = @albumId");

Album album = GetAlbum.Query<Album>(cnn, new { albumId = 12 });
```

```sql
SELECT al.AlbumId AS Id, al.Title, ar.ArtistId AS ArtistId, ar.Name AS ArtistName FROM albums al JOIN artists ar ON ar.ArtistId = al.ArtistId WHERE al.AlbumId = @albumId
```

## Find the matching entry point

| Dapper capability | Rinku entry point |
| --- | --- |
| `Execute` | `QueryCommand.Execute` or builder `Execute` |
| `ExecuteScalar<T>` | `QueryCommand.ExecuteScalar<T>` |
| `ExecuteReader` | `QueryCommand.ExecuteReader` |
| stored procedure | `CommandType.StoredProcedure` or `QueryCommand.FromProc` |
| output and return values | directional `DbParamInfo` and `DbCommand` value helpers |
| `DynamicParameters` | builder, parameter type, or `DbParamInfo` |
| `SqlBuilder` | conditional SQL and value handlers |
| `QueryMultiple` | `ExecuteMultiReader` and `MultiReader` |
| `GetRowParser<T>` | `TypeParser.GetTypeParser<T>` |
| per-row type switching | `GetCurrentSetParser<T>` or a caller-selected parser |
| multi-mapping | nested types, tuples, grouping, or a construction path |
| custom mapped value | `TypeParsingInfo` |
| custom complete-result behavior | `ITypeParserMaker` |
| custom parameter handler | `ConvertedDbParamInfo<T>` or another `DbParamInfo` |
| `DbString` | pinned `DbParamInfo` type and size |
| literal replacement | `_N`, `_S`, `_R`, or a custom SQL handler |
| dynamic row | `DynaObject` or `Dictionary<string, object>` |
| buffered and streamed results | `List<T>`, arrays, `IEnumerable<T>`, `StreamQueryAsync<T>` |
| transaction, timeout, cancellation | matching execution arguments |
