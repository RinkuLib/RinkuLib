# Dynamic rows

Use `DynaObject` when the caller should read a row by column name or position.

```csharp
DynaObject row = GetAlbum.Query<DynaObject>(cnn);

int id = row.Get<int>("Id");
string title = row.Get<string>("Title");
object? first = row[0];
```

`Get<T>` converts to the requested type. The indexer returns `object?`.

```csharp
long id = row.Get<long>("Id");
object? title = row["Title"];
```

Lookups accept a string, a `ReadOnlySpan<char>`, or a column index.

```csharp
ReadOnlySpan<char> column = "Title";

string title = row.Get<string>(column);
int id = row.Get<int>(0);
```

The span overload avoids creating another string when the caller already has a span.

## Read several dynamic rows

`DynaObject` composes with the normal buffered and streamed result shapes.

```csharp
List<DynaObject> rows = GetAlbums.Query<List<DynaObject>>(cnn);
IEnumerable<DynaObject> stream = GetAlbums.Query<IEnumerable<DynaObject>>(cnn);
```

```csharp
await foreach (DynaObject row in GetAlbums.StreamQueryAsync<DynaObject>(cnn, ct: cancellationToken))
    Console.WriteLine($"{row.Get<int>("Id")}: {row.Get<string>("Title")}");
```

Later duplicate names receive a suffix.

```text
Id | Name | Id | Name
```

```csharp
int firstId = row.Get<int>("Id");
int secondId = row.Get<int>("Id#2");
```

A `DynaObject` can also be changed after it has been read.

```csharp
row.Set("Title", "New title");
row.Set(0, 99);
```

`Set` converts the supplied value to the column's mapped type and returns `false` when the key is missing or the value cannot be assigned.

It can also read columns left after a typed tuple element.

```csharp
(int id, DynaObject remaining) = GetAlbum.Query<(int, DynaObject)>(cnn);
// id takes the first column. remaining receives the others.
```

A dynamic row can also be nested in another mapped type.

```csharp
public record Artist(int Id, string Name, DynaObject Album);

static readonly QueryCommand GetArtist = new("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId, al.Title AS AlbumTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId WHERE ar.ArtistId = @artistId");

Artist artist = GetArtist.Query<Artist>(cnn, new { artistId = 7 });

int albumId = artist.Album.Get<int>("AlbumId");
string title = artist.Album.Get<string>("AlbumTitle");
```

`DynaObject` has no child members of its own. It takes every unused column that matches its current name path. Here that path is `Album`, so it receives `AlbumId` and `AlbumTitle`. The complete column names are kept inside the dynamic row.

This also allows more than one dynamic group in the same object.

```csharp
public record SearchRow(DynaObject Album, DynaObject Artist);

static readonly QueryCommand Search = new("SELECT al.AlbumId, al.Title AS AlbumTitle, ar.ArtistId, ar.Name AS ArtistName FROM albums al JOIN artists ar ON ar.ArtistId = al.ArtistId WHERE al.AlbumId = @albumId");

SearchRow result = Search.Query<SearchRow>(cnn, new { albumId = 12 });

int albumId = result.Album.Get<int>("AlbumId");
int artistId = result.Artist.Get<int>("ArtistId");
```

The complete nesting path is used.

```csharp
public record ArtistEnvelope(NestedArtist Artist);
public record NestedArtist(int Id, string Name, DynaObject Album) : IDbReadable;

static readonly QueryCommand GetArtistEnvelope = new("SELECT ar.ArtistId, ar.Name AS ArtistName, al.AlbumId AS ArtistAlbumId, al.Title AS ArtistAlbumTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId WHERE ar.ArtistId = @artistId");

ArtistEnvelope result = GetArtistEnvelope.Query<ArtistEnvelope>(cnn, new { artistId = 7 });

int albumId = result.Artist.Album.Get<int>("ArtistAlbumId");
string title = result.Artist.Album.Get<string>("ArtistAlbumTitle");
```

The `Album` value is inside `Artist`, so it receives the columns starting with `ArtistAlbum`.

Use `[NoName]` when the dynamic value should take every column left by its siblings.

```csharp
public record AuditRecord(int Id, [NoName] DynaObject Details);

static readonly QueryCommand GetAuditRecord = new("SELECT AuditId AS Id, Actor, IpAddress FROM audit_log WHERE AuditId = @auditId");

AuditRecord record = GetAuditRecord.Query<AuditRecord>(cnn, new { auditId = 42 });

string actor = record.Details.Get<string>("Actor");
string ipAddress = record.Details.Get<string>("IpAddress");
```

`[Alt]` adds another accepted prefix while keeping the member's normal prefix available.

```csharp
public record ReleaseRow([Alt("Record")] DynaObject Album);

static readonly QueryCommand GetRelease = new("SELECT AlbumId AS RecordId, Title AS RecordTitle FROM albums WHERE AlbumId = @albumId");

ReleaseRow release = GetRelease.Query<ReleaseRow>(cnn, new { albumId = 12 });
string title = release.Album.Get<string>("RecordTitle");
```

A custom `INameComparer` works the same way, so an application can define its own grouping convention.

```csharp
namespace DynamicRowsExample;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class DynamicPrefixAttribute(string prefix) : Attribute, INameComparerMaker {
    public INameComparer MakeComparer(Type type, ref INameComparer current, object[] attributes, object? member) => new DynamicPrefixComparer(prefix);
}

public sealed record DynamicPrefixComparer(string Prefix) : INameComparer {
    public string GetDefaultName() => Prefix;
    public bool Match(ReadOnlySpan<char> column, Span<INameComparer> parents) => parents.Length == 0 && column.Equals(Prefix, StringComparison.OrdinalIgnoreCase);
    public bool Contains(string name) => string.Equals(name, Prefix, StringComparison.OrdinalIgnoreCase);
}

public record EventRow([DynamicPrefix("Payload")] DynaObject Details);
```

```csharp
static readonly QueryCommand GetEvent = new("SELECT EventId, PayloadCode, PayloadText FROM events WHERE EventId = @eventId");

EventRow result = GetEvent.Query<EventRow>(cnn, new { eventId = 4 });
int code = result.Details.Get<int>("PayloadCode");
```

## DynaObject schema caching

The parser records column names, ordinals, and typed readers when it is created. Reusing the same command and projection reuses that work.

```csharp
static readonly QueryCommand AlbumProjection = new("?SELECT AlbumId AS Id, Title, ReleaseYear FROM albums");

var projection = AlbumProjection.StartBuilder();
projection.Use("Title");
List<DynaObject> albums = projection.Query<List<DynaObject>>(cnn);
```

Each distinct dynamic-projection key set can keep its own parser.

## A schema that changes at runtime

Use `Dictionary<string, object>` when the live reader’s schema must remain authoritative for every row.

```csharp
static readonly QueryCommand RawProjection = new("SELECT @columns_R FROM customers WHERE CustomerId = @customerId");

Dictionary<string, object> row = RawProjection.Query<Dictionary<string, object>>(cnn, new { columns = "Email, City", customerId = 1 });

object email = row["Email"];
```

```sql
SELECT Email, City FROM customers WHERE CustomerId = @customerId
```

`_R` writes raw SQL without escaping. Only use application-controlled values, never user input.

Database `NULL` becomes `null`. Names are case-insensitive, and duplicates receive `#2`, `#3`, and later suffixes.

A dictionary can also take remaining columns.

```csharp
(int id, Dictionary<string, object> remaining) = RawProjection.Query<(int, Dictionary<string, object>)>(cnn, new { columns = "CustomerId, Email, City", customerId = 1 });
```

```sql
SELECT CustomerId, Email, City FROM customers WHERE CustomerId = @customerId
```

A dictionary also takes every unused column that matches its current name path. Separate members can take separate groups from the same row.

```csharp
public record SearchRow(Dictionary<string, object> Album, Dictionary<string, object> Artist);

static readonly QueryCommand Search = new("SELECT al.AlbumId, al.Title AS AlbumTitle, ar.ArtistId, ar.Name AS ArtistName FROM albums al JOIN artists ar ON ar.ArtistId = al.ArtistId WHERE al.AlbumId = @albumId");

SearchRow result = Search.Query<SearchRow>(cnn, new { albumId = 12 });
int albumId = (int)result.Album["AlbumId"];
int artistId = (int)result.Artist["ArtistId"];
```

Nested dictionaries use the complete path in the same way as `DynaObject`.

```csharp
public record ArtistEnvelope(NestedArtist Artist);
public record NestedArtist(int Id, string Name, Dictionary<string, object> Album) : IDbReadable;

static readonly QueryCommand GetArtistEnvelope = new("SELECT ar.ArtistId, ar.Name AS ArtistName, al.AlbumId AS ArtistAlbumId, al.Title AS ArtistAlbumTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId WHERE ar.ArtistId = @artistId");

ArtistEnvelope result = GetArtistEnvelope.Query<ArtistEnvelope>(cnn, new { artistId = 7 });
int albumId = (int)result.Artist.Album["ArtistAlbumId"];
```

Use `[NoName]` when a dictionary should take every unused column.

```csharp
public record AuditRecord(int Id, [NoName] Dictionary<string, object> Details);

static readonly QueryCommand GetAuditRecord = new("SELECT AuditId AS Id, Actor, IpAddress FROM audit_log WHERE AuditId = @auditId");

AuditRecord record = GetAuditRecord.Query<AuditRecord>(cnn, new { auditId = 42 });
string actor = (string)record.Details["Actor"];
```

`[Alt]` and custom name comparers change the accepted prefix.

```csharp
public record ReleaseRow([Alt("Record")] Dictionary<string, object> Album);

static readonly QueryCommand GetRelease = new("SELECT AlbumId AS RecordId, Title AS RecordTitle FROM albums WHERE AlbumId = @albumId");

ReleaseRow release = GetRelease.Query<ReleaseRow>(cnn, new { albumId = 12 });
string title = (string)release.Album["RecordTitle"];
```

```csharp
public record RawEventRow([DynamicPrefix("Payload")] Dictionary<string, object> Details);

static readonly QueryCommand GetRawEvent = new("SELECT EventId, PayloadCode, PayloadText FROM events WHERE EventId = @eventId");

RawEventRow result = GetRawEvent.Query<RawEventRow>(cnn, new { eventId = 4 });
int code = (int)result.Details["PayloadCode"];
```

`DynamicPrefixAttribute` is the custom comparer from the example above.

`DynaObject` is the faster choice when the command can distinguish each projection. A dictionary reads the current schema on every row, which allows one parser to accept changing projections at a higher per-row cost.

The dictionary shape also works in collections and streams.

```csharp
List<Dictionary<string, object>> rows = RawProjection.Query<List<Dictionary<string, object>>>(cnn, new { columns = "CustomerId, Email, City", customerId = 1 });
```

[Dynamic projection](../conditional-sql/dynamic-projection.md) covers tracked projection keys. [Database `NULL`](nulls.md) covers null values. [Cache ownership](../customization/caches.md) covers parser invalidation.
