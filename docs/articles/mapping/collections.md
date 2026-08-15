# Collections from database results

There are two common ways to build a parent with children. Several result sets avoid repeating the parent columns. A joined result can be folded directly into nested collections.

## Use several result sets

Read the parents once, then read each child with its parent key.

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record class Artist(int Id, string Name) {
    public List<Album> Albums { get; } = [];
}

static readonly QueryCommand GetArtistsAndAlbums = new("SELECT ArtistId AS Id, Name FROM artists ORDER BY ArtistId; SELECT ArtistId, AlbumId AS Id, Title FROM albums ORDER BY ArtistId");

using MultiReader results = GetArtistsAndAlbums.ExecuteMultiReader(cnn);

List<Artist> artists = results.Query<List<Artist>>();
using IEnumerator<(int ArtistId, Album Album)> albums = results.Query<IEnumerable<(int, Album)>>().GetEnumerator();

bool more = albums.MoveNext();
foreach (Artist artist in artists) {
    while (more && albums.Current.ArtistId == artist.Id) {
        artist.Albums.Add(albums.Current.Album);
        more = albums.MoveNext();
    }
}
```

The two result sets must use the same key order for this merge. This approach is useful when repeating large parent rows would make the joined result unnecessarily wide.

## Fold joined rows

A collection in the requested type can consume consecutive rows and build one parent.

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, List<Album> Albums);

static readonly QueryCommand GetArtists = new("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumsId, al.Title AS AlbumsTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId");

List<Artist> artists = GetArtists.Query<List<Artist>>(cnn);
```

```text
Id  Name    AlbumsId  AlbumsTitle
1   AC/DC   10        High Voltage
1   AC/DC   11        Let There Be Rock
2   Queen   20        Jazz
```

```text
Artist(1, "AC/DC", [Album(10, "High Voltage"), Album(11, "Let There Be Rock")])
Artist(2, "Queen", [Album(20, "Jazz")])
```

Rows for one parent must be consecutive. Order the SQL by the parent key when the database does not already guarantee that order.

## Column prefixes

The collection member name prefixes the columns used by its elements.

```text
AlbumsId
AlbumsTitle
```

`[Alt]` accepts another prefix when the SQL uses a singular name.

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, [Alt("Album")] List<Album> Albums);
```

```sql
SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumId, al.Title AS AlbumTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId
```

## Keep parents with no children

A `LEFT JOIN` returns database `NULL` for the missing child. Put `[AbortOnNull]` on the child identity so that row does not create an empty child object.

```csharp
public record Album([AbortOnNull] int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, List<Album> Albums);

static readonly QueryCommand GetArtists = new("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumsId, al.Title AS AlbumsTitle FROM artists ar LEFT JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId");

List<Artist> artists = GetArtists.Query<List<Artist>>(cnn);
```

```text
Id  Name   AlbumsId  AlbumsTitle
3   Bjork  NULL      NULL

Artist(3, "Bjork", [])
```

Without `[AbortOnNull]`, a non-nullable value such as `Album.Id` rejects database `NULL`. A child made only from nullable reference values may instead produce an unwanted object whose members are all null.

## Null scalar elements

Null collection elements are skipped by default.

```csharp
public record Playlist(int Id, List<string> Tags);

Playlist playlist = GetPlaylistTags.Query<Playlist>(cnn);
// Tags: "rock" | NULL | "live" -> ["rock", "live"]
```

`[KeepNullElements]` retains null elements in their original positions.

```csharp
public record Playlist(int Id, [KeepNullElements] List<string?> Tags);

Playlist playlist = GetPlaylistTags.Query<Playlist>(cnn);
// Tags: "rock" | NULL | "live" -> ["rock", null, "live"]
```

`[NotNull]` rejects null elements instead of skipping them.

```csharp
public record Playlist(int Id, [NotNull] List<string> Tags);

Playlist playlist = GetPlaylistTags.Query<Playlist>(cnn);
// A NULL tag raises RINKU4003.
```

## Nested collections

Collections can nest to any depth supported by the mapped types.

```csharp
public record Lesson(int Id, string Title) : IDbReadable;
public record Module(int Id, string Name, List<Lesson> Lessons) : IDbReadable;
public record Course(int Id, string Name, List<Module> Modules);

List<Course> courses = GetCourses.Query<List<Course>>(cnn);
```

```text
Id  Name  ModulesId  ModulesName  ModulesLessonsId  ModulesLessonsTitle
1   C#    10         Basics       100               Variables
1   C#    10         Basics       101               Loops
1   C#    11         Async        110               Tasks
```

```text
Course(1, "C#", [
    Module(10, "Basics", [Lesson(100, "Variables"), Lesson(101, "Loops")]),
    Module(11, "Async", [Lesson(110, "Tasks")])
])
```

## Side-by-side collections

One parent can collect different child types from the same rows. Each child uses `[AbortOnNull]` so a row intended for the other collection contributes nothing.

```csharp
public record OrderItem([AbortOnNull] int Id, decimal Price) : IDbReadable;
public record OrderNote([AbortOnNull] int Id, string Text) : IDbReadable;
public record Order(int Id, List<OrderItem> Items, List<OrderNote> Notes);
```

```text
Id  ItemsId  ItemsPrice  NotesId  NotesText
1   10       9.99        NULL     NULL
1   11       4.00        NULL     NULL
1   NULL     NULL        20       gift wrap
```

```text
Order(1, [OrderItem(10, 9.99), OrderItem(11, 4.00)], [OrderNote(20, "gift wrap")])
```

## Supported collection shapes

`List<T>`, arrays, and `IEnumerable<T>` have built-in multi-row mappings. Other collection types can be registered with `MultiRowTypeParsingInfo`.

[Grouping](grouping.md) controls where one parent ends and the next begins. [Database NULL](nulls.md) covers collapsed children and null elements. [Custom multi-row types](../customization/multi-row.md) covers other collection implementations.
