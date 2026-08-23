# Dynamic rows

## Read by name or index

```csharp
DynaObject row = cnn.Query<DynaObject>("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });

int id = row.Get<int>("Id");
string title = row.Get<string>("Title");
object? first = row[0];
```

`Get<T>` converts the stored value to the requested type. The indexer returns `object?`.

## Serialize as JSON

`DynaObject` composes with `System.Text.Json` and serializes as a plain JSON object whose properties are the returned column names. Deserializing JSON into a `DynaObject` is not supported.

```csharp
DynaObject row = cnn.Query<DynaObject>("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });

string json = JsonSerializer.Serialize(row);
```

## Span and index lookup

```csharp
ReadOnlySpan<char> column = "Title";

string title = row.Get<string>(column);
int id = row.Get<int>(0);
```

## Several rows

```csharp
const string sql = "SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId";

List<DynaObject> rows = cnn.Query<List<DynaObject>>(sql);
IEnumerable<DynaObject> stream = cnn.Query<IEnumerable<DynaObject>>(sql);
```

```csharp
await foreach (DynaObject row in cnn.StreamQueryAsync<DynaObject>(sql, ct: cancellationToken))
    Console.WriteLine(row.Get<string>("Title"));
```

## Duplicate names

```text
Id | Name | Id | Name
```

```csharp
int firstId = row.Get<int>("Id");
int secondId = row.Get<int>("Id#2");
```

Later duplicate names receive a numeric suffix.

## Set a value

```csharp
bool titleChanged = row.Set("Title", "New title");
bool firstChanged = row.Set(0, 99);
```

`Set` returns false when the key is missing or the value cannot be assigned.

## Dynamic value in a tuple

```csharp
(int id, DynaObject remaining) = cnn.Query<(int, DynaObject)>("SELECT AlbumId, Title, ArtistId FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
// The first tuple slot claims AlbumId.
// remaining receives the columns available to the second slot.
```

## Dynamic value in a mapped path

```csharp
public record Artist(int Id, string Name, DynaObject Album);

Artist artist = cnn.Query<Artist>("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId, al.Title AS AlbumTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId WHERE ar.ArtistId = @artistId", new { artistId = 7 });

int albumId = artist.Album.Get<int>("AlbumId");
string title = artist.Album.Get<string>("AlbumTitle");
```

The dynamic value receives columns available at its current mapping path.

[Reading order](reading-order.md)
