# Across rows

A value can be read from multiple rows instead of just one.

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

## Single-row vs. multi-row types

Most types are single-row types. Their values are completely read from one row.

Collections like `List<T>` are multi-row types. They consume multiple consecutive rows and fold them into a single value. 

The parser only groups consecutive rows. Rows belonging to the same value must appear together. Use an `ORDER BY` when necessary.

## Default grouping rules

By default, every single-row member before the first multi-row member acts as the grouping value. 

When those values change, a new value begins.

```csharp
public record Regional(int Region, List<decimal> Amounts) : IDbReadable;
Regional first = GetSales.Query<Regional>(cnn);

// Region | Amounts
// 1      | 9.99
// 1      | 4.00
// 2      | 5.00
// -> Regional(1, [9.99, 4.00]), the read stopping at the 2
```

Members appearing after the first multi-row member are not part of the boundary. They are read exactly once, from the first row of the value.

```csharp
public record Basket(int Id, List<int> Items, decimal Total);

// Id | Items | Total
// 1  | 10    | 14.00
// 1  | 4     | 14.00
// -> Basket(1, [10, 4], 14.00)
```

### Default grouping edge cases

If a type contains *only* multi-row members, the default rule assumes every row belongs to a single value.

```csharp
public record Pair(List<int> Numbers, List<string> Words) : IDbReadable;

var (nums, words) = GetData.Query<(List<int>, List<string>)>(cnn);
Pair pair         = GetData.Query<Pair>(cnn);

// Numbers | Words
// 1       | a
// 2       | b
// -> nums and pair.Numbers are [1, 2], words and pair.Words are ["a", "b"]
```

If a single-row member appears *after* a multi-row member, with nothing before it to define a group, default grouping fails. 

The parser cannot infer the boundary and throws a `MissingGroupBoundary` exception.

```csharp
// Throws MissingGroupBoundary if no explicit grouping is provided
public record Report(List<int> Rows, int Total);
```

## Explicit grouping

When a column that changes between rows sits before the collection, the default rule splits each row into its own value. `[GroupKey]` names the real key so those rows fold together.

```csharp
public record Entry(int Id, decimal Delta) : IDbReadable;
public record Ledger([property: GroupKey] int Account, decimal Balance, List<Entry> Entries);

// Account | Balance | EntriesId | EntriesDelta
// 1       | 100.00  | 10        | -20.00
// 1       | 80.00   | 11        | -15.00
// 2       | 300.00  | 20        | -50.00
// -> Ledger(1, 100.00, [Entry(10, -20.00), Entry(11, -15.00)]), Ledger(2, 300.00, [Entry(20, -50.00)])
```

Without the key the running `Balance` joins it, and every row becomes its own ledger.

Several members compose one key. Here `Region` and `Day` group while the per-row `Rate` stays out of the boundary and is read once.

```csharp
public record Line(int Sku, int Qty) : IDbReadable;
public record Sale([property: GroupKey] int Region, [property: GroupKey] int Day, decimal Rate, List<Line> Lines);

// Region | Day | Rate | LinesSku | LinesQty
// 1      | 5   | 1.02 | 400      | 2
// 1      | 5   | 1.03 | 401      | 1
// 1      | 6   | 1.05 | 402      | 5
// -> Sale(1, 5, 1.02, [two lines]), Sale(1, 6, 1.05, [one line])
```

## Column prefixes

`[Alt]` on a collection changes the column prefix its elements read.

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, [Alt("Album")] List<Album> Albums);

// Id | Name  | AlbumId | AlbumTitle
// 1  | AC/DC | 10      | High Voltage
// -> Artist(1, "AC/DC", [Album(10, "High Voltage")])
```

A grouping key reads a column like any other value, so `[Alt]` moves the column it compares. The key is a rule over columns, not a constructor argument. `[GroupKey]` on a member or parameter only points that rule at the member's column.

```csharp
public record Session(int Id, string Agent) : IDbReadable;
public record User([property: GroupKey, Alt("UserId")] int Id, string Name, List<Session> Sessions);

// UserId | Name | SessionsId | SessionsAgent
// 1      | Ada  | 100        | firefox
// 1      | Ada  | 101        | chrome
// -> User(1, "Ada", [Session(100, "firefox"), Session(101, "chrome")])
```

## Missing rows

An element is skipped when the value to add is null, a nullable scalar that reads null or an object that collapses on an `[InvalidOnNull]` column that is null.

```csharp
public record Album([InvalidOnNull] int Id, string Title) : IDbReadable;
public record Artist([property: GroupKey] int Id, string Name, List<Album> Albums);

// Id | Name  | AlbumsId | AlbumsTitle
// 3  | Bjork | null     | null
// -> Artist(3, "Bjork", [])
```

An element that still builds is kept, null fields and all. With no column to collapse it, an all-null object is still an element.

```csharp
public record Tag(int? Id, string? Text) : IDbReadable;
public record Post([property: GroupKey] int Id, List<Tag> Tags);

// Id | TagsId | TagsText
// 5  | null   | null
// -> Post(5, [Tag(null, null)])
```

A nullable scalar is the case `[KeepNullElements]` is for. Its null reads as no element and drops, and the attribute keeps it in the collection.

```csharp
public record Palette([property: GroupKey] int Id, [KeepNullElements] List<string?> Colors);

// Id | Colors
// 1  | red
// 1  | null
// 1  | blue
// -> Palette(1, ["red", null, "blue"])
```

## Nested and side-by-side folding

Multi-row parsing is fully composable. Elements can themselves span multiple rows, allowing recursive folding.

```csharp
public record Lesson(int Id, string Title) : IDbReadable;
public record Module([property: GroupKey] int Id, string Name, List<Lesson> Lessons) : IDbReadable;
public record Course([property: GroupKey] int Id, string Name, List<Module> Modules);

// Id | Name | ModulesId | ModulesName | ModulesLessonsId | ModulesLessonsTitle
// 1  | C#   | 10        | Basics      | 100              | Variables
// 1  | C#   | 10        | Basics      | 101              | Loops
// 1  | C#   | 11        | Async       | 110              | Tasks
// ->
// Course(1, "C#", [
//   Module(10, "Basics", [Lesson(100, "Variables"), Lesson(101, "Loops")]),
//   Module(11, "Async", [Lesson(110, "Tasks")])])
```

Multiple collections can fold independently side-by-side from the same result set. Each collection only consumes its own mapped columns.

```csharp
public record Item(int Id, decimal Price) : IDbReadable;
public record Note(int Id, string Text) : IDbReadable;
public record Order([property: GroupKey] int Id, List<Item> Items, List<Note> Notes);

// Id | ItemsId | ItemsPrice | NotesId | NotesText
// 1  | 10      | 9.99       | null    | null
// 1  | 11      | 4.00       | null    | null
// 1  | null    | null       | 20      | gift wrap
// -> Order(1, [Item(10, 9.99), Item(11, 4.00)], [Note(20, "gift wrap")])
```