# Collections

A value can hold a collection. There are two main ways to handle it: read separate result sets or fold a join.

## Multiple result sets

Select the parents and the children as two result sets and group the children yourself. The parent is read once, never repeated across its children, and the grouping key stays out of the built value by reading each child as a `(key, child)` pair. This assumes the children arrive ordered by their parent.

```sql
SELECT Id, Name FROM Artists ORDER BY Id;
SELECT ArtistId, Id, Title FROM Albums ORDER BY ArtistId
```

```csharp
public record Album(int Id, string Title);
public record Artist(int Id, string Name) {
    public List<Album> Albums { get; } = [];
}

using var multi = GetGraph.ExecuteMultiReader(cnn);
List<Artist> artists = multi.Query<List<Artist>>();
using var albums = multi.Query<IEnumerable<(int ArtistId, Album Album)>>().GetEnumerator();

bool more = albums.MoveNext();
foreach (var artist in artists)
    while (more && albums.Current.ArtistId == artist.Id) {
        artist.Albums.Add(albums.Current.Album);
        more = albums.MoveNext();
    }
```

The reader owns the command and disposes it with itself. More on the [multiple result sets](../running-queries/multiple-results.md) reader.

## The join fold

Select a nested shape over a join. Rinku folds the repeated rows back, each parent holding its children, and no grouping is written by hand. The repeating entries are handled.

```sql
SELECT ar.Id, ar.Name, al.Id AS AlbumsId, al.Title AS AlbumsTitle
FROM Artists ar JOIN Albums al ON al.ArtistId = ar.Id
ORDER BY ar.Id
```

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, List<Album> Albums);

List<Artist> artists = GetArtists.Query<List<Artist>>(cnn);

// Id | Name  | AlbumsId | AlbumsTitle
// 1  | AC/DC | 10       | High Voltage
// 1  | AC/DC | 11       | Let There Be Rock
// 2  | Queen | 20       | Jazz
// ->
// Artist(1, "AC/DC", [Album(10, "High Voltage"), Album(11, "Let There Be Rock")])
// Artist(2, "Queen", [Album(20, "Jazz")])
```

A collection like `List<T>` is a multi-row type, consuming consecutive rows and folding them into one value. Rinku folds only consecutive rows, so a value's rows must arrive together. Order the query by the grouping value when the source does not already. Which columns group the rows is the boundary, covered in [Grouping](../mapping/grouping.md), and by default it is the values before the first collection.

## Column prefixes

`[Alt]` on a collection changes the column prefix its elements read.

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, [Alt("Album")] List<Album> Albums);

// Id | Name  | AlbumId | AlbumTitle
// 1  | AC/DC | 10      | High Voltage
// -> Artist(1, "AC/DC", [Album(10, "High Voltage")])
```

## Missing rows

A null element is skipped.

```csharp
public record Roster(int Team, List<string> Players);

// Team | Players
// 1    | Ada
// 1    | null
// 1    | Bo
// -> Roster(1, ["Ada", "Bo"])
```

An object drops when it collapses on an `[AbortOnNull]` column that is null.

```csharp
public record Album([AbortOnNull] int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, List<Album> Albums);

// Id | Name  | AlbumsId | AlbumsTitle
// 3  | Bjork | null     | null
// -> Artist(3, "Bjork", [])
```

When a `LEFT JOIN` produces `NULL` values, Rinku attempts to instantiate the type using those `NULL`s. Without `[AbortOnNull]`, the outcome depends entirely on your parameter types:

```csharp
// Value types throw: non-nullable parameters (like int) cannot accept database NULL.
public record Album(int Id, string Title) : IDbReadable;
// -> Throws an exception when encountering nulls.

// Reference types (like string) accept null by default, creating an element full of nulls.
public record Tag(string Id, string Text) : IDbReadable;
public record Post(int Id, List<Tag> Tags);

// Id | TagsId | TagsText
// 5  | null   | null
// -> Post(5, [Tag(null, null)]) // Adds a probably unwanted partial object

```

`[KeepNullElements]` keeps null elements as the default. Without it, nulls are skipped. `[NotNull]` throws when a null element is encountered.

```csharp
// Id | Colors
// 1  | red
// 1  | null
// 1  | blue

public record Palette(int Id, List<string> Colors);
// -> Palette(1, ["red", "blue"])

public record PaletteKept(int Id, [KeepNullElements] List<string?> Colors);
// -> PaletteKept(1, ["red", null, "blue"])

public record PaletteNotNull(int Id, [NotNull] List<string> Colors);
// -> throws NullValueAssignmentException
```

## Nested

A collection holds another multi-row type, and the fold runs to any depth.

```csharp
public record Lesson(int Id, string Title) : IDbReadable;
public record Module(int Id, string Name, List<Lesson> Lessons) : IDbReadable;
public record Course(int Id, string Name, List<Module> Modules);

// Id | Name | ModulesId | ModulesName | ModulesLessonsId | ModulesLessonsTitle
// 1  | C#   | 10        | Basics      | 100              | Variables
// 1  | C#   | 10        | Basics      | 101              | Loops
// 1  | C#   | 11        | Async       | 110              | Tasks
// ->
// Course(1, "C#", [
//   Module(10, "Basics", [Lesson(100, "Variables"), Lesson(101, "Loops")]),
//   Module(11, "Async", [Lesson(110, "Tasks")])])
```

## Side by side

Two collections fold from the same rows. A row that carries no data for one of them collapses that element on its `[AbortOnNull]` column, so each keeps only the rows that built.

```csharp
public record Item([AbortOnNull] int Id, decimal Price) : IDbReadable;
public record Note([AbortOnNull] int Id, string Text) : IDbReadable;
public record Order(int Id, List<Item> Items, List<Note> Notes);

// Id | ItemsId | ItemsPrice | NotesId | NotesText
// 1  | 10      | 9.99       | null    | null
// 1  | 11      | 4.00       | null    | null
// 1  | null    | null       | 20      | gift wrap
// -> Order(1, [Item(10, 9.99), Item(11, 4.00)], [Note(20, "gift wrap")])
```

A collection other than the built-in `List`, `IEnumerable`, and arrays is registered on [Custom multi-row types](../mapping/custom-multi-row-types.md).
