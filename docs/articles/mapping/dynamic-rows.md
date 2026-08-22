# Dynamic rows

Use `DynaObject` when the caller should read columns by name or position.

```csharp
DynaObject row = GetAlbum.Query<DynaObject>(cnn);

int id = row.Get<int>("Id");
string title = row.Get<string>("Title");
object? first = row[0];
```

`Get<T>` converts to the requested type. The indexer returns `object?`.

## Lookup forms

```csharp
ReadOnlySpan<char> column = "Title";

string title = row.Get<string>(column);
int id = row.Get<int>(0);
```

Lookups accept a string, a `ReadOnlySpan<char>`, or a column index.

## Several dynamic rows

```csharp
List<DynaObject> rows = GetAlbums.Query<List<DynaObject>>(cnn);
IEnumerable<DynaObject> stream = GetAlbums.Query<IEnumerable<DynaObject>>(cnn);
```

Async streaming uses the same row type.

```csharp
await foreach (DynaObject row in GetAlbums.StreamQueryAsync<DynaObject>(cnn, ct: cancellationToken))
    Console.WriteLine(row.Get<string>("Title"));
```

## Duplicate column names

Later duplicate names receive a suffix.

```text
Id | Name | Id | Name
```

```csharp
int firstId = row.Get<int>("Id");
int secondId = row.Get<int>("Id#2");
```

## Change a dynamic row

```csharp
row.Set("Title", "New title");
row.Set(0, 99);
```

`Set` returns false when the key is missing or the value cannot be assigned.

## Use remaining tuple columns

```csharp
(int id, DynaObject remaining) = GetAlbum.Query<(int, DynaObject)>(cnn);
```

The scalar claims the first column. `DynaObject` receives the remaining matching columns.

## Nest a dynamic row

```csharp
public record Artist(int Id, string Name, DynaObject Album);

Artist artist = GetArtist.Query<Artist>(cnn, new { artistId = 7 });

int albumId = artist.Album.Get<int>("AlbumId");
string title = artist.Album.Get<string>("AlbumTitle");
```

The dynamic value receives unused columns that match its current nested name path.

Use a normal mapped type when the row shape is stable and should be checked through C# members.
